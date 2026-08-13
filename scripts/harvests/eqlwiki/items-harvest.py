#!/usr/bin/env python3
"""Fetch every eqlwiki item page's wikitext for the embedded item catalog.

Closing the item-knowledge gap (2026-08-13, David's call): the Gear Locker and
item tooltips should answer instantly and offline, the way the quest and spell
catalogs already do. This script only FETCHES — enumeration via
Category:Items (10,986 members at first run) and batched revisions queries,
50 titles per request, cached to items-wikitext.jsonl so re-runs (the weekly
refresh) only pull pages whose revision moved.

Parsing deliberately does NOT happen here: the app already owns a battle-tested
item parser (EqlWikiItemService.Parse + ItemStatsBlock.Parse), and a second
parser in python would drift from it. The companion build step
(itemcatalog-build, C#) reads this dump THROUGH the app's own parser and emits
src/EQBuddy.Core/Data/ItemCatalog.json.gz.

Output:
  cache/items-wikitext.jsonl   - {"title","revid","wikitext"} per line (NOT committed)
  items-harvest-report.md      - counts + skipped pages
"""

import json
import time
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
CACHE = HERE / "cache" / "items-wikitext.jsonl"
REPORT = HERE / "items-harvest-report.md"
API = "https://eqlwiki.com/api.php"
UA = {"User-Agent": "EQBuddy-Harvest/1.0 (github.com/DranakCorps-bot/EQBuddy)"}
PAUSE = 0.6   # between requests — the same etiquette every harvest here uses


def get(params: dict) -> dict:
    params = {**params, "format": "json", "maxlag": "5"}
    url = API + "?" + urllib.parse.urlencode(params)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
                data = json.loads(r.read())
            if "error" in data and data["error"].get("code") == "maxlag":
                time.sleep(5)
                continue
            return data
        except Exception:
            if attempt == 3:
                raise
            time.sleep(3 * (attempt + 1))
    raise RuntimeError("unreachable")


def item_titles() -> list[tuple[str, int]]:
    """Every member of Category:Items with its latest revid (for refresh skips)."""
    out, cont = [], {}
    while True:
        d = get({"action": "query", "generator": "categorymembers",
                 "gcmtitle": "Category:Items", "gcmlimit": "500", "gcmnamespace": "0",
                 "prop": "revisions", "rvprop": "ids", **cont})
        for p in d.get("query", {}).get("pages", {}).values():
            revid = p.get("revisions", [{}])[0].get("revid", 0)
            out.append((p["title"], revid))
        cont = d.get("continue", {})
        if not cont:
            break
        time.sleep(PAUSE)
    return out


def main() -> None:
    have: dict[str, int] = {}
    if CACHE.exists():
        for line in CACHE.read_text(encoding="utf-8").splitlines():
            if line.strip():
                rec = json.loads(line)
                have[rec["title"]] = rec.get("revid", 0)

    titles = item_titles()
    fresh = [(t, r) for (t, r) in titles if have.get(t) != r]
    print(f"{len(titles)} items in Category:Items; {len(fresh)} to fetch "
          f"({len(titles) - len(fresh)} cached at current revision)")

    CACHE.parent.mkdir(parents=True, exist_ok=True)
    fetched, missing = 0, []
    with CACHE.open("a", encoding="utf-8") as sink:
        for i in range(0, len(fresh), 50):
            batch = fresh[i:i + 50]
            d = get({"action": "query", "prop": "revisions", "rvprop": "content|ids",
                     "titles": "|".join(t for t, _ in batch)})
            pages = d.get("query", {}).get("pages", {})
            for p in pages.values():
                title = p["title"]
                revs = p.get("revisions")
                if not revs or "*" not in revs[0]:
                    missing.append(title)
                    continue
                sink.write(json.dumps({"title": title, "revid": revs[0].get("revid", 0),
                                       "wikitext": revs[0]["*"]}, ensure_ascii=False) + "\n")
                fetched += 1
            sink.flush()
            done = min(i + 50, len(fresh))
            print(f"  {done}/{len(fresh)} fetched", end="\r")
            time.sleep(PAUSE)

    # Compact the JSONL: later revisions of a title supersede earlier lines.
    latest: dict[str, dict] = {}
    for line in CACHE.read_text(encoding="utf-8").splitlines():
        if line.strip():
            rec = json.loads(line)
            latest[rec["title"]] = rec
    wanted = {t for t, _ in titles}
    kept = [latest[t] for t in sorted(latest) if t in wanted]
    CACHE.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in kept) + "\n",
                     encoding="utf-8")

    REPORT.write_text(
        "# items harvest report\n\n"
        f"- Category:Items members: {len(titles)}\n"
        f"- fetched this run: {fetched}\n"
        f"- in dump after compaction: {len(kept)}\n"
        f"- pages with no readable revision: {len(missing)}\n\n"
        + "".join(f"  - {t}\n" for t in missing[:50])
        + "\nNext step: itemcatalog-build (C#) parses this dump through the app's own "
          "parser and writes src/EQBuddy.Core/Data/ItemCatalog.json.gz.\n",
        encoding="utf-8")
    print(f"\n{fetched} fetched; dump holds {len(kept)}; report -> {REPORT.name}")


if __name__ == "__main__":
    main()
