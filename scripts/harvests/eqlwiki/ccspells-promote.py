"""Promote cast times from the spells harvest into Data/CcSpells.json.

The CC catalog (categories) was generated 2026-08-06 from the Spellpage sweep;
its generator lived in a session scratchpad. This script is the durable half:
it JOINS spells.json's cast_time_seconds onto the existing CcSpells.json
entries WITHOUT touching categories - the curated category decisions (bard
mez/charm song split etc.) are log-verified and must never be regenerated
blindly. Cast times feed the per-spell charm ARM WINDOW (task 2026-08-13:
a charm landing claims only within cast time + 1.5s of our own cast start,
so a bystander's charm 20s after our failed one can't steal the pet).

Run from repo root:  python scripts/harvests/eqlwiki/ccspells-promote.py
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[3]
CC_PATH = ROOT / "src" / "EQBuddy.Core" / "Data" / "CcSpells.json"
HARVEST = ROOT / "scripts" / "harvests" / "eqlwiki" / "spells.json"


def main():
    cc = json.loads(CC_PATH.read_text(encoding="utf-8"))
    harvest = json.loads(HARVEST.read_text(encoding="utf-8"))
    times = {}
    for s in harvest:
        name = (s.get("name") or "").strip()
        ct = s.get("cast_time_seconds")
        # Strictly positive only: the wiki writes 0 for instant-cast spells AND for
        # unknown, and either way the app must fall back to its generic window - a
        # 0 here would arm a charm for just the slack (review 2026-08-13).
        if name and isinstance(ct, (int, float)) and ct > 0:
            times[name.lower()] = float(ct)

    matched = missing = 0
    for spell in cc["spells"]:
        ct = times.get(spell["name"].strip().lower())
        if ct is not None:
            spell["castTimeSeconds"] = ct
            matched += 1
        else:
            spell.pop("castTimeSeconds", None)
            missing += 1

    cc["comment"] = (
        "Crowd-control spells harvested from eqlwiki Spellpage templates 2026-08-06 "
        "(Chaosrah: CC alerts missed enchanter stuns like Color Flux). Categories are "
        "curated - never regenerate them blindly. castTimeSeconds joined from the "
        "spells harvest by ccspells-promote.py; it powers the per-spell charm arm "
        "window (cast time + slack)."
    )
    CC_PATH.write_text(json.dumps(cc, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"cast times: {matched} matched, {missing} without (fall back to the generic window)")


if __name__ == "__main__":
    main()
