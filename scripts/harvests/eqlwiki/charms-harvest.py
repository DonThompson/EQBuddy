#!/usr/bin/env python3
"""Generate the embedded charm catalog from the eqlwiki spell harvest.

Offline: reads spells.json and writes src/EQBuddy.Core/Data/CharmSpells.json —
the catalog behind the PER-SPELL CHARM ARM WINDOW. When you cast a charm, the
"it is now charmed / it is your pet" evidence arrives roughly one cast time
later, so a landing line is ours only within that spell's own cast time plus a
slack. A flat window is wrong in both directions: too short for Cajole Undead
(9 s) and far too generous for Charm (2.4 s). The spread is real, so the window
has to be per spell — and per spell means the cast times must be DATA, kept
true by the weekly refresh, not a list somebody typed once.

Charm-family membership is decided from EFFECTS, never from names:

  a slot effect beginning "Charm" ("Charm up to level 51", "Charm (up to L49)")
  on a DETRIMENTAL spell

Names cannot do this job in either direction. Half the family is named for what
it sounds like rather than what it does — Beguile, Allure, Dictate, Thrall of
Bones, Call of Karana, Tunare`s Request, Solon's Song of the Sirens — and the
names that DO contain "charm" include three spells that are not charms at all
(Naki's Charm of Pernicity is spell haste; Tavee's Charm of Diuturnity is spell
duration). That last group is why the catalog carries a "notCharms" list as
well: SpellCatalog falls back to name fragments when nothing else knows a
spell, and "Allure of Death" — a necromancer mana regen — matched the "allure"
fragment and armed a 30 s charm window every time a necro cast it. Evidence
that a spell is NOT a charm is worth shipping, because the fallback that would
otherwise guess wrong is in code and cannot be regenerated.

Outputs:
  ../../../src/EQBuddy.Core/Data/CharmSpells.json  - the catalog
  charms-report.md                                 - summary + what was vetoed
"""

import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
SPELLS = HERE / "spells.json"
CC = HERE.parents[2] / "src" / "EQBuddy.Core" / "Data" / "CcSpells.json"
OUT = HERE.parents[2] / "src" / "EQBuddy.Core" / "Data" / "CharmSpells.json"
REPORT = HERE / "charms-report.md"

# The wiki writes the effect as "Charm up to level 51" or "Charm (up to L49)";
# \b keeps "Charm" from matching a description that merely mentions charming.
CHARM_EFFECT = re.compile(r"^charm\b", re.IGNORECASE)

# Mirrors the charm half of SpellCatalog.Families (C#) — the name fragments the
# app falls back to when neither the seed nor a catalog knows a spell. Anything
# here that ISN'T a charm has to be vetoed by name, because that fallback has no
# other way to be told.
CHARM_NAME_FRAGMENTS = ["charm", "beguil", "dominat", "enslave", "allure",
                        "befriend", "cajol"]


def is_charm(spell) -> bool:
    """Effect evidence only. Detrimental is required as a second signal: a
    BENEFICIAL spell with a Charm-shaped effect line would be a wiki mistake or
    a charm-resist buff, and a wrong entry here arms a false pet claim."""
    if spell.get("beneficial") is True:
        return False
    return any(CHARM_EFFECT.match((e.get("effect") or "").strip())
               for e in spell.get("slot_effects") or [])


def names(spell):
    """A spell's name and its page title, when they differ. Solon's Bravura is
    filed under "Solon's Bewitching Bravura" — the log can write either."""
    out = []
    for key in ("name", "page_title"):
        n = (spell.get(key) or "").strip()
        if n and n not in out:
            out.append(n)
    return out


def main():
    spells = json.loads(SPELLS.read_text(encoding="utf-8"))

    charms, seen = [], set()
    unknown_cast = []
    for s in spells:
        if not is_charm(s):
            continue
        ct = s.get("cast_time_seconds")
        # The wiki writes 0 for instant AND leaves the field blank for unknown.
        # Both mean "no per-spell window": the app falls back to its generic one
        # rather than to a 1.5 s trap, so neither is recorded as a cast time.
        ct = float(ct) if isinstance(ct, (int, float)) and ct > 0 else None
        for name in names(s):
            if name in seen:
                continue
            seen.add(name)
            entry = {"name": name}
            if ct is not None:
                entry["castTimeSeconds"] = ct
            else:
                unknown_cast.append(name)
            charms.append(entry)

    # Vetoes: everything the app would otherwise call a charm on name evidence
    # (its own fragment fallback, or the curated CC catalog) that the wiki's own
    # effects contradict. Only spells the harvest actually has effects for can be
    # vetoed — silence is not evidence.
    cc_charms = {e["name"] for e in json.loads(CC.read_text(encoding="utf-8"))["spells"]
                 if e.get("category") == "Charm"}
    not_charms = {}
    for s in spells:
        if is_charm(s) or not (s.get("slot_effects") or []):
            continue
        for name in names(s):
            if name in seen:
                continue
            fragment = next((f for f in CHARM_NAME_FRAGMENTS if f in name.lower()), None)
            if fragment is None and name not in cc_charms:
                continue
            not_charms[name] = (
                f"name fragment {fragment!r}" if fragment else "CC catalog says Charm",
                (s.get("slot_effects") or [{}])[0].get("effect") or "?",
            )

    catalog = {
        "comment": "Charm spells and their wiki cast times, behind the per-spell charm "
                   "arm window: a charm landing is OURS only within the spell's own cast "
                   "time plus slack of our cast starting. Membership is decided from slot "
                   "effects (\"Charm up to level N\") on detrimental spells, never from "
                   "names; notCharms vetoes the app's name-fragment fallback where the "
                   "effects contradict it. A missing castTimeSeconds means instant or "
                   "unknown — the app falls back to its generic window, never a tighter "
                   "one. Generated by scripts/harvests/eqlwiki/charms-harvest.py — "
                   "regenerate, never hand-edit. Details in charms-report.md.",
        "charms": sorted(charms, key=lambda e: e["name"]),
        "notCharms": sorted(not_charms),
    }
    OUT.write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    timed = [c for c in charms if "castTimeSeconds" in c]
    lines = [
        "# Charm catalog report", "",
        f"- {len(charms)} charm names ({len(timed)} with a wiki cast time, "
        f"{len(unknown_cast)} instant/unknown -> generic window)",
        f"- {len(not_charms)} vetoed: named like a charm, proven otherwise by their effects",
        "", "## Arm windows (cast time + slack)", "",
    ]
    for c in sorted(timed, key=lambda e: e["castTimeSeconds"]):
        lines.append(f"- {c['name']}: {c['castTimeSeconds']:g}s")
    lines += ["", "## Instant or unknown cast time (generic window applies)", ""]
    lines += [f"- {n}" for n in sorted(unknown_cast)]
    lines += ["", "## Vetoed (notCharms)", ""]
    for name, (why, effect) in sorted(not_charms.items()):
        lines.append(f"- {name}: {why}; first effect is {effect!r}")
    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(charms)} charm names ({len(timed)} timed), "
          f"{len(not_charms)} vetoed -> {OUT.name}")


if __name__ == "__main__":
    main()
