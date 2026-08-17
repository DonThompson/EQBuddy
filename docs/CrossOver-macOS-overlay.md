# Running EQBuddy over fullscreen EverQuest on macOS (CrossOver / Wine)

For playing EverQuest Legends on a Mac through **CrossOver** (or another Wine
wrapper) with the **Windows** EQBuddy build in the same bottle, so the widget
floats over the fullscreen game the way it does on Windows — with the mouse still
free to roam between monitors, and clicking the widget never popping the Mac menu
bar.

Not on Wine? Ignore this page. Everything here is opt-in and does nothing on a
Windows install or the native macOS (Avalonia) build.

---

## Quick setup

**The easiest path: hand this section to an AI coding agent pointed at your
CrossOver bottle and let it do the whole thing.** It's a few mechanical steps
(patch a driver, flip two settings), exactly the kind of setup an agent handles
well, and the driver build wants a toolchain most people would rather not drive
by hand.

1. **Patch the Mac driver** — from the EQBuddy repo:
   ```sh
   scripts/crossover/setup-overlay.sh
   ```
   It rebuilds only `winemac.so`, backs up your original, and installs it. Needs
   Xcode command-line tools and Homebrew `bison`. Undo any time with `--revert`.

   **It builds on your machine, and there is no prebuilt binary to opt into.**
   This step replaces a library inside your CrossOver installation, and we are not
   going to ask you to trust an unreadable binary from a log-reading widget to do
   that. What it compiles is CodeWeavers' own published source plus
   [`winemac-overlay.patch`](../scripts/crossover/winemac-overlay.patch) — both
   reviewable before you run anything. The cost is a few minutes' compile.
2. **Set the game to Fullscreen** — in EverQuest, Options → Display → Fullscreen
   (or Alt+Enter), at your monitor's native resolution.
3. **Turn on the overlay setting** (default off) — in `settings.json`:
   ```json
   { "WineFloatOverFullscreen": true }
   ```
   Leave `WineKeepGameFullscreen` off unless you want the immersive-only extra
   below — with it on you can't pull other windows over the game.
4. **Restart EQBuddy and EverQuest** once, so both pick up the driver.
5. **Verify** — with both up, `./winlevels EQ` (built from
   `scripts/crossover/winlevels.m`) should show EQBuddy near level 2147483630 and
   `eqgame.exe` at 26.

That's it. The widget floats over the fullscreen game, clicking it doesn't drop
the menu bar, and you can still alt-tab to other apps and pull windows onto the
game's monitor when EverQuest isn't focused.

## The exact setup this reproduces

This is the "fullscreen, free-mouse" configuration — the game looks fullscreen,
the mouse moves freely across both monitors, EQBuddy sits on top, and the Mac
menu bar never appears when you click the widget:

- **EverQuest: Fullscreen mode**, native resolution (e.g. `Fullscreen=1`,
  `Width=1920`/`Height=1080` in `eqclient.ini`). Under Wine this becomes a
  borderless window covering the screen — which is *why* the mouse stays free
  (it's not a display-captured exclusive fullscreen that would lock the cursor).
- **Do NOT enable CrossOver's "capture displays for fullscreen"** (the
  `CaptureDisplaysForFullscreen` Mac Driver key — leave it unset/off, the
  default). Turning it on locks the cursor to the game's display and blacks out
  the other monitor; off is what keeps the mouse free.
- **The patched `winemac.so`** (step 1) plus **`WineFloatOverFullscreen` on**,
  and **`WineKeepGameFullscreen` off** (see the note below on why). EQBuddy writes
  the matching driver knob (`LetTopmostWindowsFloatOverFullscreen` for itself)
  from the setting, so the toggle is the only switch you touch.
- macOS "Displays have separate Spaces" left at its default (on) — standard for
  multi-monitor.

### `WineKeepGameFullscreen` — immersive-only, and why it's off here

This companion setting (driver knob `KeepFullscreenWhenInactive` on `eqgame.exe`)
keeps the game covering the Mac menu bar even when it *loses* focus. It sounds
nice, but there's a hard tradeoff baked into how macOS stacks windows: the menu
bar sits at a window level *above* normal app windows, so anything tall enough to
hide the menu bar also covers every other window. With this on, **you can't pull
another app's window over the game** — alt-tab to Finder or a browser and it
lands *behind* EverQuest. You'd have to turn the game windowed to get at it.

You usually don't need it: because EQBuddy's own windows are non-activating,
clicking the widget never drops the menu bar in the first place. The knob only
matters when the game genuinely loses focus — which is exactly when you're
switching to another app and *want* to see it. So keep it **off** unless you
never alt-tab away mid-session and want pure immersion.

---

## The problem

On Windows, EQBuddy's always-on-top windows sit over the game because "fullscreen"
there is really a composited borderless window. On macOS, Wine's Mac driver
(`winemac.drv`) gives a fullscreen game a window **level** — 26 — that no ordinary
topmost window can beat; EQBuddy's windows are capped at level 21, so the game
paints over them. Two more quirks compound it: clicking a widget activates the
whole Wine process, which deactivates the game and drops the Mac menu bar over
it; and the game itself falls to the bottom level when it loses focus, so the
menu bar reappears whenever you click another app or monitor. None of this is
fixable with stock Wine's registry settings.

## The solution

A ~65-line patch to `winemac.drv` adds two **opt-in, default-off** registry knobs
(`scripts/crossover/winemac-overlay.patch` — the LGPL corresponding source):

| Knob (EQBuddy sets it from a setting) | Effect |
|---|---|
| `LetTopmostWindowsFloatOverFullscreen` (EQBuddy.exe) | A topmost widget computes a level *above* the game's, so it floats on top. |
| `KeepFullscreenWhenInactive` (eqgame.exe) | Optional, off by default: a fullscreen game keeps its high level when inactive, so the menu bar stays hidden when you click away — at the cost of not being able to pull other windows over the game (see the note above). |

The third piece is pure EQBuddy code (`WineOverlay.cs`): under Wine, with the
opt-in on, every EQBuddy window gets `WS_EX_NOACTIVATE`, which winemac maps to a
non-activating panel — a click lands on the widget without pulling the game out
of the foreground. Keyboard still follows whatever you last clicked (so text
fields and search keep working); click the game to move or type in it again.

The knobs are off for every other app and every Windows install. The whole
feature is inert unless you're under Wine and have turned it on.

## Verify it worked

`scripts/crossover/winlevels.m` is a tiny native inspector — build once:
```sh
clang -framework Foundation -framework CoreGraphics -o winlevels scripts/crossover/winlevels.m
```
With the game focused and EQBuddy up, `./winlevels EQ` should show EQBuddy's
windows at `CGShieldingWindowLevel()+2` (~2147483630) and `eqgame.exe` at 26. If
EQBuddy still reads 21, the driver isn't patched or the knob isn't set.

## Maintenance

- **CrossOver updates overwrite `winemac.so`** — re-run `setup-overlay.sh` after
  each update. It always builds against the source matching your installed
  CrossOver version, so an ABI mismatch after a Wine version bump can't arise.
- The registry knobs live in the bottle and persist across updates.

## Known tradeoffs

- The widget floats above **everything**, including the Mac menu bar, whenever
  it's open. It's small and you can hide it with a hotkey.
- Clicking a widget moves keyboard focus to it until you click the game again —
  expected for a non-native overlay, and the price of keeping text fields typeable.

## For maintainers / agents

- App code: `src/EQBuddy/WineOverlay.cs` (Wine-gated, opt-in; inert on Windows),
  wired from `App.OnStartup`; settings `WineFloatOverFullscreen` /
  `WineKeepGameFullscreen` in `src/EQBuddy.Core/AppSettings.cs` (both default
  false). `MainWindow.OnSourceInitialized` also applies the style pre-show.
- Driver patch: `scripts/crossover/winemac-overlay.patch` — three files in
  `dlls/winemac.drv/` (two options in `macdrv_main.c`/`macdrv_cocoa.h`, two level
  overrides in `cocoa_window.m`). Applies with `git apply` / `patch -p1` from a
  Wine source root. The patch is deliberately generic (it never mentions EQBuddy)
  and default-off, so it could be proposed to Wine upstream later; for now it
  ships here as a user-applied driver modification.
