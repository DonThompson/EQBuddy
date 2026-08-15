# EQBuddy architecture

Orientation for anyone — human or agent — changing this codebase. Companion to
[../CLAUDE.md](../CLAUDE.md) (loaded every session, deliberately short) and
[TestPlan.md](TestPlan.md) (what the behaviour is supposed to be).

Measured 2026-08-14 at v1.82.0.

---

## 1. The shape of it

```
                    eqlog_<char>_<server>.txt   (the game writes it; we only read)
                                 |
                    LogWatcher   |  500 ms polls, byte-offset, truncation-safe
                                 v
                    LogParser    |  one regex per line type -> GameEvent records
                                 v
                    SessionStats |  aggregation, encounters, DPS, journal
                                 v
                    StatsSnapshot|  ONE immutable snapshot per UI tick
                                 |
            +--------------------+--------------------+
            v                    v                    v
      WPF MainWindow      Avalonia MainWindow    CompanionHost
      (the widget)        (Linux/macOS)          (EQBuddy Mobile, LAN)
```

| Project | Files | Lines | Role |
|---|---:|---:|---|
| `EQBuddy.Core` | 65 | 14,507 | Parsing, aggregation, settings, catalogs, wiki. No UI. |
| `EQBuddy.UI.Shared` | 35 | 3,612 | View-model/formatting shared by both UIs. **Framework-free — enforced by `ArchitectureTests`.** |
| `EQBuddy.Companion` | 14 | 2,921 | LAN HTTP+WebSocket server and the mobile page. **UI-toolkit-free on purpose**, so Avalonia can host it too. |
| `EQBuddy` | 37 | 14,432 | The WPF widget and its windows. |
| `EQBuddy.Avalonia` | 22 | 6,423 | Cross-platform build, trails by a few releases. |

## 2. Load-bearing invariants

Break one of these and something quietly goes wrong rather than failing loudly.

1. **One snapshot per tick.** `SessionStats.Snapshot()` is taken once per second and
   handed to every consumer. Windows must not build their own — the perf pass exists
   because they used to.
2. **The log is the only input.** No memory reads, no packet sniffing, no telemetry.
   Every network destination is documented in `SECURITY.md` and verified from code.
3. **`UI.Shared` and `Core` reference no UI framework.** Pinned by `ArchitectureTests`.
4. **`EQBUDDY_APPDATA` redirects the whole profile.** Tests set it via a module
   initializer; the isolated-profile flow depends on nothing else leaking out.
5. **Companion surfaces are gated twice.** The desktop decides what may be *sent*
   (`CompanionHiddenSurfaces`); the device decides what it *shows* and in what order
   (its own localStorage). An ungated surface is never even projected.
6. **Per-section fingerprints** decide who gets woken by a push. They must exclude
   anything that drifts every tick.
7. **Curated catalogs are human-written.** Automation may flag, never write.
8. **The ledger's `Revision` counter is how maps notice edits.** Both map windows watch
   it; that is the entire mechanism by which curating on a tablet updates the PC.

## 3. Where the risk is concentrated

**`src/EQBuddy` — 14,432 lines, and no test project references it.** Two routes now
reach into it anyway, and neither is a unit test:

- **Pure arithmetic extracted to `UI.Shared`** (`WidgetMetrics`, `ChipStackAnchor`) —
  ordinary unit tests, because sums do not need a window.
- **The `EQBUDDY_EXPAND` dump read by E2E** — the real app, launched, reporting facts
  about itself. This is the only thing that sees whether the arithmetic is *wired* to
  the controls. To cover a piece of window behaviour: dump the fact, assert it from E2E.

What remains genuinely uncovered is rendering and input: how it *looks*, and what a
mouse does to it.

This is not academic. Both bugs players reported on 2026-08-14 — the clipped card (#144)
and the drifting chips (#152) — live in this layer, and on the morning they were reported
nothing here could have caught either. Both are now held: their arithmetic by
`WidgetMetricsTests` and `ChipStackAnchorTests`, and #144's wiring by two E2E scenarios.
That is the shape of progress to aim for — each escape converts a manual row into an
automated one. See [TestPlan.md](TestPlan.md) §5.

### Hotspot ratchet

`ArchitectureTests` fails the build if these grow more than 10% past their baseline.
A path may be a glob, and then its matches are **summed** — so splitting a hotspot into
another partial cannot buy headroom. Current state:

| File | Baseline | Now | Fails at | Headroom |
|---|---:|---:|---:|---:|
| `EQBuddy/MainWindow*.xaml.cs` | 4,274 | 4,274 | 4,701 | 427 |
| `EQBuddy.Core/SessionStats.cs` | 2,324 | 2,372 | 2,556 | 184 |
| `EQBuddy/OptionsWindow.xaml.cs` | 1,547 | 1,597 | 1,701 | 104 |
| `EQBuddy.Core/LogParser.cs` | 853 | 853 | 938 | 85 |

`MainWindow` sat at 97% of its allowance until 2026-08-15, which is not a place to work
from. The 992-line Epic/Sky checklist surface came out into `QuestChecklistView` — it
only ever touched settings, its own state and eleven named controls, so it was a
component that had never been separated rather than logic that was truly entangled. The
baseline came down with it, banking the room instead of leaving it to refill.

That is the pattern to repeat, and there is more of it: the render family (`RefreshUi`
at 551 lines, `RenderTracked` at 181) is the next candidate. Two rules make it safe.
**Pin the behaviour in E2E first** — add the facts to the `EQBUDDY_EXPAND` dump and
assert them, as `TheQuestChecklistRendersATabPerClassAndTheSelectedClassesRows` does;
with no unit tests in the WPF layer that assertion is the only thing standing between a
move and a silent regression. And **prefer a class over a partial**, because a class is
a component with a boundary you can read, while a partial is the same window in two
files — which is why the ratchet now sums them.

## 4. Concepts worth knowing before you change them

**Encounter vs pull.** A fight opens on damage and closes on the kill line or 20 s of
silence (`EncounterTimeout`). Fights then group into *pulls* when there is no 10 s lull
between them (`EncounterGrouping.PullGap`). The card and the History review share this
grouping so live and archived agree. In a zone that never goes quiet — Plane of Sky —
a pull runs long and its DPS stops meaning much; this is understood and deliberate
(discussion #151). Per-mob figures live on the Kills card and are unaffected.

**Combat window.** Separate from encounters: it is the DPS denominator. Damage taken
opens and extends it; self-inflicted damage deliberately does not ("a swim across a
lake is not a fight").

**Zone naming.** Three names for one place: the log's (`The Lair of the Splitpaw`), the
catalog's (`Splitpaw Lair`), and the map file's (`paw.txt`). `ZoneMapFiles` and
`SpawnCatalog.logZoneName` bridge them. A curation edit must quote the *catalog* zone.

**Mobile projection.** `CompanionMapSource` caches zone geometry and hands it out by
reference; `CompanionSnapshot.ForClient` withholds it from a device already holding the
stamp. A device parked in one zone receives the picture exactly once.

## 5. Known limits, stated honestly

- Position updates only when a `/loc` reaches the log — there is no live feed. The
  breadcrumb trail is the last minute of movement and needs two crumbs 25+ units apart.
- Browsers refuse a wake lock over plain HTTP, so the mobile page cannot hold a screen
  awake; it says so rather than pretending.
- Windows Firewall prompts on first listen and a dismissed prompt fails silently from
  the device's side.
- Avalonia trails WPF by a few releases and does not host EQBuddy Mobile yet, though
  the seam is deliberately there.
