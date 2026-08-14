#!/usr/bin/env python3
"""Promote per-class spell levels from the spells harvest into SpellLevels.json.

The level-unlocks feature answered the ding from the AA catalog alone; this
promotion gives it the other half — which SPELLS open up at a level. Only the
name and the class/level rows travel: descriptions, effects, cast times stay in
the harvest (the unlock card names the spell, the wiki explains it).

Row discipline:
  - levels: integers 1..60 only. 0/absent is the wiki's "unknown" and never a
    level; the two rows above 60 (Improved Invisibility to Undead 61/63) are
    Live-era imports on the wiki page, past Legends' cap.
  - class names: "Shadowknight" folds into "Shadow Knight" — the app's spelling
    (QuestClassFilter.Classes); every other harvest name already matches.
    "Beastlord" rows are kept as data even though the class picker doesn't
    offer it yet.
  - duplicate names (case-insensitive): the wiki holds a few spells on extra
    pages (epic guides, "Spell: X" shells, misspelled twins) with stale levels.
    Entries whose page_title exactly equals their name win the group — the
    spell's own page is authoritative; remaining conflicts merge to the
    earliest level per class (available-at means the first level you can use
    it). Display name is the group's first case-sensitive sort.

Serialization is single-line compact JSON with a fixed entry key order: the
catalog is reviewed as a diff in knowledge-refresh PRs, and a stable shape is
what keeps those diffs about DATA, not formatting.
"""

import json
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[2] / "src" / "EQBuddy.Core" / "Data" / "SpellLevels.json"

LEVEL_CAP = 60
CLASS_FOLD = {"Shadowknight": "Shadow Knight"}


def main():
    spells = json.loads((HERE / "spells.json").read_text(encoding="utf-8"))

    groups = defaultdict(list)
    for s in spells:
        name = (s.get("name") or "").strip()
        if name:
            groups[name.casefold()].append(s)

    entries, rows_total, dropped = [], 0, 0
    for group in groups.values():
        exact = [e for e in group if e.get("page_title") == e["name"]]
        picked = exact or group
        levels = {}
        for e in picked:
            for c in e.get("classes") or []:
                cls = CLASS_FOLD.get(c.get("class") or "", c.get("class") or "")
                lv = c.get("level")
                if not cls or not isinstance(lv, int) or not 1 <= lv <= LEVEL_CAP:
                    dropped += 1
                    continue
                levels[cls] = min(levels.get(cls, lv), lv)
        if not levels:
            continue
        entries.append({
            "name": sorted(e["name"] for e in picked)[0],
            "classes": [{"class": cls, "level": lv}
                        for cls, lv in sorted(levels.items())],
        })
        rows_total += len(levels)

    entries.sort(key=lambda e: (e["name"].casefold(), e["name"]))
    catalog = {"spells": entries}
    OUT.write_text(json.dumps(catalog, separators=(",", ":"), ensure_ascii=False),
                   encoding="utf-8")
    print(f"wrote {OUT}: {len(entries)} spells, {rows_total} class-level rows "
          f"({dropped} rows dropped: level absent/0 or past {LEVEL_CAP}, "
          f"{len(spells) - len(entries)} harvest pages without usable rows)")


if __name__ == "__main__":
    main()
