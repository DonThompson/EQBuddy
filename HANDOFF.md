# EQBuddy — handoff

Paste this as your first message next session.

Don't re-derive the codebase. `CLAUDE.md` loads automatically and carries the commands, the
non-negotiable rules, the where-things-live index, the trap list (now 11), and the
surface-allocation rule. `docs/Architecture.md` and `docs/TestPlan.md` sit behind it;
`DocumentationTests` fails the build if any go stale. **Start with
`pwsh -NoProfile -File scripts/status.ps1`.**

Gates: `pwsh -NoProfile -File scripts/check.ps1` (never pipe it into `tail` and chain a commit
off it — the pipe swallows the exit code). E2E is separate and needs a desktop session:
`dotnet build EQBuddy.slnx -c Release` then
`dotnet test tests/EQBuddy.E2E/EQBuddy.E2E.csproj -c Release`.

---

## State

**v1.88.0 is STAGED AND STILL UNRELEASED.** Working tree clean, everything pushed,
1,494 unit + 109 Avalonia + 9 E2E green. `WhatsNew.json`'s 1.88.0 block has ~18 entries.

**The release is the one thing outstanding that only David can do.** An attempt to run
`scripts/release.ps1` was **blocked by the permission classifier**; nothing partially ran
(verified: no local or remote tag, GitHub still shows v1.87.0, OneDrive still holds the
Aug-15 artifacts). It needs David to run it, or to grant a Bash permission rule:

```
pwsh -NoProfile -File scripts/release.ps1 -Tag v1.88.0
```

## What 1.88.0 carries

The quest surfaces, and a run of reporter-found bugs:

- **EQBuddy Mobile's quest surface** — three tabs, ~1,200-quest search index shipped once
  per device by stamp, tap-to-track, 120 ms debounce, 60-card cap.
- **The widget's Quests card** replaced the Sky Quest and Epics cards and opens the Quest
  Tracker; `OverlaySections` migration folds the old `sky`/`epic` keys onto `quests`.
- **Avalonia's three quest tabs**, closing the gap that consolidation opened, plus
  hand-ticking restored in BOTH desktop trackers (the rows had been read-only text).
- **#153** custom `.wav` volume — a missing file fell through to the OS ding at OS volume.
- **#175** Beastlord missing from all six class pickers (and its abbrev falling back to "BEA").
- **#176** Golden Hilt is Isle 7, not Isle 2 (verified four ways).
- **#154** mote potency, plus two ladder errors of ours that counting had hidden.
- **#120** class detection could never be argued back down — every signal was melee.
- **#169** Linux/macOS ran a SECOND FULL COPY of EQBuddy on every launch.
- **#173** the CPU/memory readout resized the widget every 3 s, forever.
- **#166** (Wine Segoe), **#171** + **#179** (liminalwarmth's alchemy catalog, closes #167).

## Next task: PR #178 — needs David's decision first

`macOS/Wine: opt-in overlay so the widget floats over fullscreen EverQuest`. CI green.
The **C# side is sound** — opt-in, Wine-gated on the same `wine_get_version` probe
`WineFonts.cs` uses, inert on Windows.

**But it ships a 581 KB prebuilt binary** (`scripts/crossover/prebuilt/winemac.so-cx26.3.0`),
and `setup-overlay.sh` copies it over a file *inside the user's CrossOver installation* and
re-signs it ad-hoc. That is an unreviewable blob replacing part of a commercial product on a
user's machine, from a project whose pitch is log-only and trustworthy — and Defender
false-positives are already a live problem (see the memory note). The PR includes the source
(`winemac-overlay.patch`, `winlevels.m`) and a build path, so the prebuilt is convenience,
not necessity.

**Recommendation: take the mechanism, drop the prebuilt, let the script build from the
patch.** David's call; raised, not decided.

## Open, with the ball in someone else's court

- **#173** — fix shipped, but the step from "resized every 3 s" to "keyboard dies" is a
  DIAGNOSIS. Asked KoboldCoterie for `xprop -root -spy _NET_ACTIVE_WINDOW`, which settles
  it: active window changing every ~3 s means focus stealing after all and our fix is
  insufficient. The always-on-top-over-fullscreen half is untouched and looks like
  compositor policy, not our bug.
- **#169** — same shape. Asked joma65 for `pgrep -a -i eqbuddy` (two copies = confirmed) and
  whether `error.log` gains the new "changed underneath this EQBuddy" line.
- **#153** — fix is unverified BY EAR. Tests prove the volume value reaches the player and
  that a missing file now announces itself; nobody has confirmed the loudness, and the
  manual pass (rename a chosen `.wav`, expect a banner) needs a desktop session with sound.
- **#123** — donation link, parked on David's identity review. It's money; leave it.

## Known gap, needs a wording call

**The two "hide the widget while the game isn't focused / isn't running" tick-boxes do
nothing on Linux.** `MainWindow.ShouldHideForFocus` has no X11/Wayland foreground probe —
it logs once and returns false. After the #169 fix the settings will now *persist* and still
not hide anything, which is a silent no-op in CLAUDE.md's terms. Either implement the probe
or say so in Options. Suggested text, needs David's approval and a WhatsNew line:
*"Linux: not available yet — there is no way to ask X11 or Wayland which window is in front,
so the widget stays visible."*

## Approved but not started

**#174 (n3cr0nk1tt3n) — David approved all three QoL requests.** All are phone/tablet
surface by the CLAUDE.md filter:
1. Mob lookup by name → zone, camp loc, drops, **faction hits** (we already parse faction
   lines and only count them).
2. "What is this for?" from a Loot row → quests, recipes, what drops it, cross-linked.
   Closest to shipping: loot rows already know quest turn-ins.
3. Upgrade preview at +N with mote cost and breakpoints — "most-requested in game". The
   mote arithmetic is now firm from #154: the exp-per-mote ladder and the cost-to-reach-a-tier
   curve (1, 2, 4, 8 … 512) are both verified against the wiki.

## Standing decisions added this session

- **eqlwiki is the tie-breaker** (David, 2026-08-16, from #163): other sources are allowed
  where the wiki is silent; where they disagree the wiki wins; anything sourced elsewhere is
  marked as such. Now in `CLAUDE.md` beside the match-the-wiki rule.
- Trap 9 (a layout class carrying behaviour), trap 10 (a fallback that skips the knobs the
  main path honours), trap 11 (a table of evidence only one side can produce) — all in
  `CLAUDE.md`.

## Notes that cost time this session

- **The wiki contradicts itself on mote tier numbering** — 0-based on Mote Guide (whose
  column is actually "Item Tier Limit"), 1-based on Item Upgrade System, and both inside one
  paragraph of Constructed Potential. `Motes.cs` deliberately stores NO tier index. Don't
  "fix" that without reading the comment.
- **WebFetch refuses verbatim reproduction** (copyright guard) and returns paraphrase, which
  is why two reads of the same wiki page disagreed. For anything that must be quoted exactly,
  drive a browser and read `get_page_text`, or fetch the raw wikitext via `action=edit`.
- **Derived artifacts get REGENERATED, never hand-merged.** Both alchemy PRs conflicted
  mostly on generated files; `buffs-harvest.py` and `fades-harvest.py` run offline from
  `spells.json`, so the resolution is to take the contributor's script change and re-run.
  Twice the regenerated result was a strict superset of the PR's by exactly one entry —
  that is the union proving itself, and a useful check that the merge was right.
- Discussion replies need the GraphQL `addDiscussionComment` mutation (`gh` has no native
  command); issues take `gh issue comment`.
