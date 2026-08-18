# EQBuddy — handoff

**Don't re-derive the codebase.** `CLAUDE.md` loads automatically and carries the commands,
the non-negotiable rules, the where-things-live index, the trap list (14) and the
surface-allocation rule. `docs/Architecture.md` and `docs/TestPlan.md` sit behind it, and
`DocumentationTests` fails the build if any go stale. Start with
`pwsh -NoProfile -File scripts/status.ps1`.

---

## Run things in the form the allowlist grants

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File C:/Users/david/source/EQBuddy/scripts/release.ps1 -Tag vX.Y.Z
```

`check.ps1`, `status.ps1`, `shoot.ps1` and `shot.ps1` take the same shape and work through
Bash. `shoot.ps1 -Shot a,b,c` needs `pwsh -Command "& '…/shoot.ps1' -Shot a,b,c"` — the
`-File` form passes the list as one string. Chaining with `&&` sometimes trips the
classifier where the same commands run fine apart; split them.

---

## State: v1.90.0 shipped, board clear

Tag, GitHub release, OneDrive and the local install all verified. `main` is clean and
pushed. **1,738 unit + 205 Avalonia + 10 E2E green. Zero open PRs. Every discussion has a
reply.**

Two releases went out on 2026-08-17:

- **1.89.0** — Gate 2 (Quests rebuilt as list + detail pane), plus liminalwarmth's #198
  loot provenance, #199 mini-bar double-click and #200 (Disabled) alert sound.
- **1.90.0** — Gate 3 (Spawns rebuilt with progress bars and a state-aware countdown),
  plus quasarj's #194 CrossOver overlay fix.

---

## THE NEXT TASK — Gate 4 of the UI/UX rework: Loot

`docs/DesignSystem.md` is the whole plan and the gate log. **Read §11 first** — it is the
amended order and it explains why Loot moved up. Then §10 (Gate 2 as built) and §11.6
(Gate 3 as built) for the worked examples.

**Gate 4 is the Loot card + the Loot breakout window**, both UIs in one change. It moved
ahead of the widget because #198 concentrated the debt there: it added a `show: all /
looted / other` filter strip and a `sort: count / name / recent` strip to both surfaces,
built the old way — bare `TextBlock`s with a `Tag`, a click handler and literal sizes.

**Spend the primitives; do not mint new ones.**

- `EqChip` / `EqSegmentedStrip` (each UI's `DesignSystem.cs`, spec in
  `UI.Shared/ChipStyle.cs`) — those two strips are exactly this, and converting them is
  most of the gate.
- `DesignSystem.Text(role)` / `.Icon(name)` / `.IconButton(...)`.
- `UI.Shared/IconPaths.cs` for glyphs. Add new ones there; `IconGeometryTests` checks they
  parse and fill the 24×24 grid.

**Then add all four Loot files to `DesignRatchetTests.Migrated` in the same PR.** That test
is the mechanism the whole effort rests on: it fails the build if a migrated surface grows
a literal font size, radius or spacing value, or draws with a glyph. **The list only ever
grows.**

**There are ~14 more hand-built segmented strips** in `MainWindow.xaml` and
`BreakoutWindow.xaml` after Loot's. They are Gate 5's problem, not Gate 4's.

### Shoot it before you call it done

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File C:/Users/david/source/EQBuddy/scripts/shoot.ps1 -Shot widget-cards
```

`scripts/shoot.ps1` runs the real app against a throwaway profile seeded with the shifted
fixture (so cards show real numbers), renders opaque (`EQBUDDY_OPAQUE`), and captures with
`PrintWindow` so a running EQBuddy can't photograph itself over the shot. `-List` names the
shots, `-Theme Solarized` is the only light theme and the one where a hardcoded colour
shows up.

**This is an acceptance criterion, not a nicety.** It has now found three bugs no test
could see: the Gate 2 text clipping (trap 14), and in Gate 3 both the too-narrow progress
bar and the column headers sitting 115px off. Look at the picture.

---

## Debts and open threads

**Owed publicly, from replies posted 2026-08-17.** These are commitments, not ideas:

| # | Reporter | What | Where it belongs |
|---|---|---|---|
| #135 | bjstrange | **charm7.txt / Puppet Strings.** Item clickies aren't in the charm catalog (harvested from wiki *spell* pages), so the per-spell arm window has nothing to look up. I promised to replay his actual log rather than theorise — do that. | Not a gate |
| #182 | Ladylag | Ability names rendering as literal `.` — a parser failure drawn as data. Also: breakouts only resize from the bottom edge, and truncated names should show in full on hover. | `.` bug now; the rest Gate 8 |
| #189 | wizen | The Quest Tracker doesn't hide with the widget. Also asked him for `error.log` after an update re: settings (trap 13). | Not a gate |
| #197 | wizen | The sound picker filters `*.wav;*.mp3` but playback is the OS's, so `.ogg` works. Widen the filter. One string, two places. | Gate 8 or now |
| #192 | wizen | Waiting on his exact forage line — if Legends writes "some", the regex misses it and that's a one-line fix. | Waiting on him |
| #202 | bjstrange | Mobile loot/watches card refresh loop. I checked the loot fingerprint and it has no clock in it, so my first hypothesis is dead. Four questions asked; waiting. | Waiting on him |
| #190 | wizen | **Approved:** tracked-quest chips — double-click opens the tracker with that quest selected, right-click dismisses. | Gate 6 |
| #191 | TheMegaSage | **Approved:** the mini bar's contents become configurable and removable. §8b's reserved widths are non-negotiable (#173). | Gate 6 |

**Still worth doing on Gate 3:** the fixture has no running timer in a catalogued zone, so
the progress bar is unit-tested but has never been *seen*. Seeding one named kill into
`tests/fixtures/eqlog_Testchar_fixture.txt` would close that.

---

## Findings worth not re-learning

- **A component nobody can reach gets rebuilt by hand.** Gate 2 built the chip primitive
  and left it private inside `QuestsWindow`; six hours later #198 hand-built two more. That
  is why gate 2b exists and why anything shared goes somewhere reachable immediately.
- **`Auto` columns lie in a header row.** A header has no buttons, so an `Auto` action
  column measures zero there and ~115 in a row — every label lands left of the column it
  names. Fixed lanes also stop rows reflowing when a button appears mid-edit.
- **A progress bar in one column is a sliver.** David, 2026-08-17: *"we have room between
  the columns."* Span it across the row.
- **#193's damage cannot be repaired.** Wildcard ticks went through the normal path and are
  indistinguishable from honest ones. The reply says so plainly; don't promise a cleanup.
- **Replay the reporter's actual log file.** A hand-condensed charm5.txt passed while the
  real one failed; same for charm6 and the #183 mez log.

---

## Hard lines (see `CLAUDE.md` for the full set)

- Never measure other players. Values line, not technical.
- Releases wait for David's explicit go. Ask "want me to cut it?" — don't hand him a
  command block; that once had two people release two minutes apart.
- Curated catalogs are never auto-written; **learned** data is.
- eqlwiki is the tie-breaker; other sources where it's silent, marked as such.
- A `UI.Shared`/Core fix must reach **both** UIs in the same change — that is what carried
  #122 and #152 to Linux.
- Every player-noticeable change earns a `WhatsNew.json` entry in the release that ships
  it, crediting the reporter by name and discussion number.
