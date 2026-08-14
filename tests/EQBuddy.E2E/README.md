# EQBuddy.E2E — the end-to-end suite

The only tests that launch the **real** `EQBuddy.exe`, grow a **real** log file under
it, and assert on the **rendered** result. Everything else in `tests/` exercises code
in-process; this suite exercises the app.

## Running locally

```powershell
dotnet build EQBuddy.slnx -c Release          # prerequisite: the exe under test
dotnet test tests/EQBuddy.E2E -c Release
```

The suite launches the built exe from `src/EQBuddy/bin/Release/net10.0-windows/` — it
never builds the app itself, so a test run can't mutate build outputs mid-flight. No
Release build → every test fails fast with a message pointing here.

**Expect widget windows to appear briefly.** Each test starts its own always-on-top
EQBuddy against an isolated profile; tests run sequentially (one app at a time) and
kill + clean up on teardown. A desktop session is required — this is why the suite is
**not** part of push/PR CI. It runs from the manually-dispatched `e2e-windows` job
(`workflow_dispatch` with `run-e2e`) or, the supported path, on a dev machine.

## How the harness works

`AppHarness` builds, per test:

- a temp **profile** dir passed as `EQBUDDY_APPDATA` — `settings.json` pre-seeded
  (LogFolder, on-screen window position, tutorial/spawn-window/update-check noise off),
  so no UI interaction is ever needed for setup;
- a temp **game install** whose `Logs\` holds `tests/fixtures/eqlog_Testchar_fixture.txt`
  with timestamps shifted to end one minute ago (`FixtureLog` is a C# port of
  `scripts/make-test-session.ps1`), so the replay produces a *live* session;
- the app launched with `EQBUDDY_EXPAND=1`: every card expands, and MainWindow writes a
  `debug.txt` state dump each UI tick — key=value counts and totals
  (`killsTotal`, `lootTotal`, `tracked`, per-list row counts). That dump is the primary
  assertion channel; `history.db` (via `SessionRepository`) is the persistence channel.

Tests append fresh log lines (exact shapes copied from the fixture) and poll the dump
with `Wait.Until` — every assertion is an observable condition with a timeout and a
reason; there are no bare sleeps. Timeouts fold in the dump content and the profile's
`error.log` tail.

## What v1 covers

1. Live session + fresh melee kill updates the kill surface.
2. Kill → loot line lands on the loot surface.
3. A pre-seeded watch rule counts its matching loot.
4. Graceful close persists the session; relaunch adopts (not duplicates) it in history.

## Deliberately NOT covered yet (the ledger against scope creep)

- **UI Automation** — no clicks, no visual-tree reads. `debug.txt` proved sufficient
  for v1, so no FlaUI/UIA dependency was taken. v2 candidate if a scenario needs
  interaction (Options, breakouts, satellite windows).
- **Avalonia app** — it has its own headless render tests; a Linux E2E lane is separate work.
- **Installer / updater** — `UpdateFolder` is pointed at an empty dir on purpose.
- **Spawn timers, mez/slow chips, buff timers, alerts firing** (sound/speech/banners),
  breakout windows, zone map, quest/gear/epic checklists, multi-character follow,
  log truncation janitor, crash recovery (`RecoveredAfterCrash`).
