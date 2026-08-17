# EQBuddy — handoff

**Don't re-derive the codebase.** `CLAUDE.md` loads automatically and carries the commands,
the non-negotiable rules, the where-things-live index, the trap list (14) and the
surface-allocation rule. `docs/Architecture.md` and `docs/TestPlan.md` sit behind it, and
`DocumentationTests` fails the build if any go stale. Start with
`pwsh -NoProfile -File scripts/status.ps1`.

---

## Run the gates in the form the allowlist grants

This cost real time on 2026-08-17. The permission rules exist, but they match a **specific
invocation shape**, and `CLAUDE.md` documents a different one. Use these exactly:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File C:/Users/david/source/EQBuddy/scripts/release.ps1 -Tag vX.Y.Z
```

`scripts/check.ps1` and `scripts/status.ps1` are **not** in the allowlist at all, so they go
through the auto-mode classifier and are refused intermittently. When that happens, fall
back to running the test projects directly — `dotnet test tests/EQBuddy.Tests/...` — rather
than assuming the tooling is broken. **Do not try to edit `.claude/settings.local.json`
yourself; that is correctly blocked.** David has been asked to either add the documented
form to the allowlist or change `CLAUDE.md` to document the granted form. Ask which happened.

Also: chaining commands with `&&` sometimes trips the classifier where the same commands
run fine individually. Split them.

---

## State: v1.88.4 shipped, board mostly clear

Tag, GitHub release, OneDrive artifacts and the local install were all verified. `main` is
clean and pushed; 1,572 unit + 110 Avalonia + 9 E2E green.

Shipped in 1.88.4: **#184** (Plane of Sky tab regrouped by reward, drop location and the
auto-tick `*` restored, undo/Ctrl+Z), **#186** (monitor-aware height cap, Ctrl+wheel resizes
the window), **#193** (the serious one — an empty class filter meant *every* class, so one
looted rune ticked several classes' checklists), **#185** (named-mob auto-discovery),
**#135** (charm's fifth cause), **#183** (one mez break costing two chips). Plus liminalwarmth's
**#201** merged after.

---

## Gates 1 and 2 are DONE — the next task is Gate 3 (Spawns + timers)

**Gate 2 shipped to `main` as branch `gate2-quests` (2026-08-17), unreleased.** Quests
rebuilt on the design system, both UIs in one change; `docs/DesignSystem.md` §10 is the
as-built record. `Directory.Build.props` is bumped to **1.89.0** and `WhatsNew.json` has
its entry, so a release is one command away **when David says go**.

The prerequisite is done too: **`pwsh -NoProfile -File scripts/shoot.ps1`** seeds a session,
renders opaque and captures over a plain backdrop. Use it — it found the Gate 2 clipping
bug (now trap 14) that no test could. The three stale README screenshots are refreshed.

**Gate 3 is Spawns + timers.** `EqTimer` and `EqProgress` are the two §3 primitives still
unbuilt, and Spawns is where they belong. Per §8c the duration field stays **free text** —
`SpawnDurationText` parses `5m`, `90s`, `3d 12h`, and a numeric spinner regresses week-long
raid targets. Add `EQBuddy/SpawnsWindow.xaml{,.cs}` and `EQBuddy.Avalonia/SpawnsWindow.cs`
to `DesignRatchetTests.Migrated` in the same PR.

The section below is the Gate 2 brief as it was written. Kept because §8a/§8b/§8c still
govern the gates after it.

## The original Gate 2 brief (done)

David commissioned a full UI/UX modernization ("Modern Norrath Companion" — restrained dark,
warm gold, Steam/Discord polish, **not** faux-medieval, **not** enterprise dashboard). His full
brief is in the 2026-08-17 conversation; the distilled version is:

**Gate 1 is DONE and APPROVED** — `docs/DesignSystem.md`. Read it first. It contains the
audit, the token/component proposal, the icon strategy, the parity strategy, the migration
order, the risks, and (§8) the decisions on his ChatGPT mockups.

**Gate 2 is Quests**, and before it, one prerequisite:

### Prerequisite — the screenshot fixture

His brief makes screenshot review an *acceptance criterion*, and right now a capture is
unusable. Two problems, both hit on 2026-08-17:

1. An isolated `EQBUDDY_APPDATA` profile has no session, so every card renders `0 dps /
   0 kills / 0 items`. Needs a **seeded session** — the shifted-log recipe (see the
   `eqbuddy-screenshot-fixture` memory and `tests/EQBuddy.E2E/FixtureLog.cs`).
2. The windows are translucent, so whatever is behind them bleeds into the PNG. Needs an
   **opaque capture path** (a capture theme, or a guaranteed-plain backdrop).

A working capture script already exists at
`<scratchpad>/shot.ps1` — captures a window by title to PNG via DWM frame bounds. It works;
it is the *content* that needs fixing. Copy it somewhere durable.

This also unblocks refreshing the README screenshots, which David asked for and which are
genuinely stale: `quest-tracker.png`, `widget-expanded.png` and `widget-cards.png` are all
Aug 11–12 and predate the 2026-08-16 card consolidation entirely. All 24 referenced files
exist, so nothing is broken — they are just out of date.

### Gate 2 — Quests

Build the tokens and the first components, then rebuild the Quests window on them, **both
UIs in the same PR**, functionality unchanged. Do not restyle anything else.

Three things from `docs/DesignSystem.md` §8 that will otherwise be discovered expensively:

- **Reward/quest icons cannot be built as mocked.** `ItemCatalog` has no icon field and
  nothing in the codebase maps item→icon (spike, 2026-08-15). Use **slot silhouettes** from
  `Slots`/`Skill`, and a **state-coloured left rule** instead of a quest-type icon.
- **Mini mode**: take the value-over-label hierarchy, but give every metric cell a reserved
  width — the widget is `SizeToContent`, so pill width is window geometry (trap 12, #173).
- **Spawns**: adopt that mockup nearly wholesale, but keep the duration as **free text**.
  `SpawnDurationText` parses `90s` and `3d 12h`; a numeric spinner regresses raid targets.

Gate order (David approved moving Spawns ahead of the widget): **Quests → Spawns/timers →
widget → mini mode + chips → map → remaining windows.**

---

## Owed to people, not yet sent

**Nothing has been posted to any discussion this whole session.** This is the biggest
outstanding debt and several of these people did real diagnostic work.

- **Drafts written, awaiting David's review, not posted**: #192, #189, #197, #190. The file
  was delivered to him in chat on 2026-08-17 (`replies-draft.md`). Ask him for it or rewrite.
- **No reply at all yet**: #184 (bjstrange), #185 (elderbit), #186 (Kemble-Kemble),
  #193 (wizen), #135 (bjstrange — *sixth* log), #183 (TheLethean), #191 (TheMegaSage).
  All six of the first are FIXED in 1.88.4 and deserve to be told.
- Discussion replies need the GraphQL `addDiscussionComment` mutation.
- **Ask reporters to correct the wiki** where relevant, and point at the page's edit link.

---

## Open PRs needing David's call

- **#194** (quasarj) — fixes the CrossOver overlay script's source download. Small, green.
  I'd take it.
- **#198** (liminalwarmth) — loot provenance, +606/−85 over 13 files. Green, but a *feature*.
- **#199, #200** (liminalwarmth) — mini-bar double-click to open a breakout; a "(Disabled)"
  alert-sound option. Both small and green, both features.
- **#195 / #196** — dependabot xunit.v3 → 4.0.0. **#196 fails CI.** It's a major-version
  migration, not a merge; recommend closing both and doing it deliberately.

---

## Findings worth not re-learning

- **#192 (foraged item) is half-diagnosed.** Quest data is correct (`Kejekan Palm Fruit ×1`
  is in `Yuio's Illness`) and forage *is* parsed. The regex is
  `^You have scrounged up an? (?<item>.+?)\.$` — if Legends writes **"some"** it silently
  doesn't parse. The draft asks wizen for his exact line. That is the likely one-line fix.
- **#189 "settings lost between installs"** — the installer never touches `settings.json`
  (checked). Most likely two copies coexisting during an update, each saving the whole file
  from its own startup snapshot (trap 13). `AppSettings.Save` now logs this; ask for
  `error.log` after an update.
- **#193's damage cannot be repaired.** Wildcard ticks went through the normal path and are
  indistinguishable from honest ones. WhatsNew says so plainly. Don't promise a cleanup.
- **The article heuristic has a real exception.** Sol A's trash clockworks are
  "CWG Model XA" — no article, reads as named. `SpawnCatalog.SharesNameFamily` guards it.
  #181's regression test catches violations immediately.

---

## Hard lines (see `CLAUDE.md` for the full set)

- Never measure other players. Values line, not technical.
- Releases wait for David's explicit go. Ask "want me to cut it?" — don't hand him a command
  block; that once caused two people to release two minutes apart (1.88.0/1.88.1).
- Curated catalogs are never auto-written; **learned** data is.
- eqlwiki is the tie-breaker; other sources where it's silent, marked as such.
- A `UI.Shared`/Core fix must reach **both** UIs in the same change — that is what carried
  #122 and #152 to Linux.
- Derived artifacts get **regenerated**, never hand-merged.
- Replay the reporter's **actual log file**. A hand-condensed version of charm5.txt passed
  while the real file failed; the same held for charm6 and the #183 mez log.
