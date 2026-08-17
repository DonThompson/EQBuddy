# Gate 1 — UI/UX audit and design-system proposal

**Status: proposal, 2026-08-17. No UI code has been changed.**
Deliverables per the brief: current-state audit · token proposal · component proposal ·
icon strategy · WPF/Avalonia parity strategy · migration order · known risks.

---

## 1. Current-state audit

Measured across the 231 `.cs`/`.xaml` files under `src/` (excluding `obj`/`bin`), plus a
read of the shipped screenshots.

### 1.1 What is already good — build on this, don't rebuild it

**Colour is already a token system, and a shared one.** `UI.Shared/ThemePalettes.cs`
holds 21 named keys × 8 themes (ParchmentBrass, BlueGrey, Grey, HighContrast, Redish,
Solarized, SolarizedDark, Turquoise) as data. WPF composes a `ResourceDictionary` from it
at runtime; Avalonia mutates brush singletons from the same table. `ThemePaletteTests`
fails the build if a palette is partial or a value doesn't parse.

This matters more than it sounds: **it is a working proof of the parity pattern this whole
effort needs** — tokens as framework-free *data* in `UI.Shared`, composed per framework,
with a test forbidding drift. The design system should extend that pattern to typography,
spacing and shape rather than invent a second mechanism.

`UI.Shared` already holds 40+ presentation modules (`Countdown`, `WidgetMetrics`,
`SpawnsViewModel`, `HistoryPresentation`, `GearChecklistPresentation`, `AlertColors`,
`MapColors`…). The separation the brief asks for — state/ViewModels untouched, presentation
improved — is largely already the architecture. **This is a presentation-token and
component gap, not an architectural one.**

### 1.2 Typography — no scale

| | |
|---|---|
| Distinct `FontSize` values | **13** — 7, 9, 9.5, 10, 10.5, 11, 11.5, 12, 12.5, 13, 14, 16, 17 |
| Total assignments | **612** |
| Named roles | **0** |

Half-point steps (9.5, 10.5, 11.5, 12.5) are the tell: sizes were nudged per control to
make a specific row fit, not chosen from a scale. 612 literal decisions is 612 chances to
disagree, and nothing can detect a disagreement.

### 1.3 Spacing — no scale

**174 distinct `Thickness` tuples.** The long tail is the problem, not the head: `(0,2,0,0)`,
`(0,3,0,0)`, `(0,4,0,0)`, `(0,6,0,0)`, `(0,8,0,0)`, `(0,10,0,0)`, `(0,12,0,0)` all exist as
"a bit of space above", chosen independently. `(20,2,0,0)` appears 14 times as an ad-hoc
indent.

### 1.4 Shape — 7 radii

`CornerRadius` takes **3, 4, 5, 6, 8, 10, 12** across 177 uses. Cards, chips, popups,
buttons and badges each ended up with their own geometry. Nothing distinguishes "this is a
card" from "this is a chip" except a number someone picked once.

### 1.5 Icons — 84 glyphs, and duplicates for one meaning

**84 distinct non-ASCII glyphs, 857 uses**, mixing four unrelated families: emoji
(🗺 📌 🎯 🐾 🐌 🕒 🔔 📍 🎒 🔍 📱 💀 💤 🔒), geometric shapes (▸ ▾ ▶ ▲ ▼ ▮), dingbats
(✓ ✔ ✕ ★ ✦ ⚑) and technical symbols (⧉ ⧗ ⟳ ↻ ⤴ ⤡ ⇣).

The same concept has more than one glyph:

| Meaning | Glyphs in use |
|---|---|
| done / confirm | `✓` ×62 and `✔` ×15 |
| favourite | `★` ×15 and `⭐` ×5 |
| refresh | `⟳` ×22 and `↻` ×4 |
| increase / decrease | `▲▼`, `⬆⬇`, `⇣` |
| expand | `▸`, `▾`, `▶` |

Emoji also render at a size and weight the app does not control, and vary by platform —
which is not hypothetical here: **PRs #148 and #166 exist because icon glyphs failed to
render at all in Wine prefixes.** Any icon strategy that depends on system fonts re-opens
that bug on Linux/macOS.

### 1.6 Information hierarchy — the Spawns window as the worked example

From `docs/screenshots/spawns-window.png`, which the brief singles out:

- The countdown (`4:21`) and the editable duration (`5m`) carry **near-identical visual
  weight**. The countdown is the glanceable value and the duration is configuration; they
  read as two equal columns.
- Every row shows **two empty input boxes** as visible rectangles. A status surface is
  wearing an editing surface's chrome, permanently.
- Three actions (`▶ 🔔 ✕`) sit at identical size with no grouping or hierarchy.
- Rows without a timer leave the countdown column blank — "unknown" is rendered as
  "nothing", so uncertainty and absence look the same.
- There is **no progress-toward-respawn** anywhere, so "due in 4:21" and "due in 18:31"
  look equally urgent.

The same pattern — box-inside-box, label and value at equal weight — recurs on the widget
cards and the older quest surfaces.

---

## 2. Token proposal

Tokens live in `UI.Shared` **as data**, exactly like `ThemePalettes`, and each UI composes
them into its own native resources. Names are conceptual; they map onto existing brush keys
so nothing has to be renamed on day one.

### 2.1 Colour — mostly a re-mapping, not a repaint

Existing keys already cover most of the brief's list. Proposed conceptual mapping:

| Concept | Today |
|---|---|
| WindowBackground | `BgBrush` |
| Surface | `PanelBrush` |
| SurfaceHover | `PanelHoverBrush` |
| SurfaceSelected | *(new — derived, see below)* |
| Divider | `BorderBrush` at reduced alpha (`ThemeTones` already derives hairlines) |
| TextPrimary / TextSecondary / TextMuted | `TextBrush` / `DimBrush` / *(new step)* |
| Accent | `AccentBrush` |
| Success / Warning / Danger / Info | `GoodBrush` / `WarnBrush` / `BadBrush` / `IncomingBrush` |

Gaps to add: **SurfaceRaised**, **SurfaceSelected**, and a third text step. All three should
be *derived* in `ThemeTones` from existing keys rather than added as 8 new hand-picked hex
values per theme — that is how hairlines and bar tracks are already done, and it keeps
`HighContrast` honest automatically.

**The accent discipline the brief asks for is a real change.** Gold is currently used for
section headings, values, borders, chips and icons alike. Proposed rule: accent means
*selected, primary action, or the single most important number on the surface*. Everything
else steps down to TextPrimary/TextSecondary.

### 2.2 Typography — 7 roles, replacing 13 sizes

| Role | Size / weight | Used for |
|---|---|---|
| `TitleWindow` | 14 SemiBold | Window title row |
| `TitleSection` | 12.5 SemiBold | Card and group headings |
| `Metric` | 16–17 Bold | The one number a surface exists to show |
| `Body` | 12 Regular | Rows, list content |
| `BodySecondary` | 11.5 Regular, TextSecondary | Detail lines (NPC · drop location) |
| `Caption` | 11 Regular, TextMuted | Filters, chips, counts |
| `Metadata` | 10 Regular, TextMuted | Footnotes, provenance, the accuracy contract |

That covers all 612 sites. The half-point sizes disappear; 7, 9 and 9.5 are absorbed into
`Metadata`/`Caption` (7pt is below the readable floor and should not survive the migration).

### 2.3 Spacing — a 6-step scale

`XXS 2 · XS 4 · S 6 · M 8 · L 12 · XL 16`

Chosen to absorb the existing head of the distribution (1, 4, 6, 8, 10, 12 dominate) with
minimum visual disturbance. `10` maps to `L 12` or `M 8` case by case; the 14 uses of
`(20,2,0,0)` become an explicit `Indent` token.

### 2.4 Shape — 4 radii, 3 heights

| Token | Value | Applies to |
|---|---|---|
| `RadiusPanel` | 10 | Windows, popups |
| `RadiusCard` | 6 | Cards, list rows, detail panels |
| `RadiusControl` | 6 | Buttons, inputs, combos |
| `RadiusPill` | 11 | Chips, badges, filter pills |
| `RowHeight` / `ControlHeight` / `IconButton` | 24 / 26 / 24 | list rows / inputs / icon actions |

---

## 3. Component proposal

Twelve primitives cover essentially every surface in the app. Each is proposed as
**a shared *spec* in `UI.Shared` + a thin native implementation per framework** (see §5).

| Component | Replaces (examples) |
|---|---|
| `EqCard` | The 14 widget card `Border`s, each currently hand-built |
| `EqSectionHeader` | Quest group headings, options group labels, breakout titles |
| `EqMetric` | dps/kills/loot/xp tiles, card summary values |
| `EqListRow` | Quest checklist rows, spawn rows, loot rows, gear rows |
| `EqChip` | Class chips, mode strip, filter pills |
| `EqStatusBadge` | `ready` / `in progress` / `done`, DUE, instance |
| `EqTimer` | Spawn countdowns, mez/charm chips, buff fades |
| `EqProgress` | Spawn progress-to-respawn, xp bar, quest completion |
| `EqIconButton` | Close, pin, expand, alert-bell, manual-start |
| `EqSearchBox` | Quest search, item lookup, history filter |
| `EqEmptyState` | "Nothing yet — loot a quest item…", empty checklists |
| `EqDetailPanel` | The proposed quest detail pane; later gear and drops |

**`EqTimer` and `EqProgress` are the highest-value pair** — they serve spawns, mez, charm,
buffs and quest readiness, which is most of what the brief calls "EQBuddy's strongest
real-time visual elements", and they are exactly what the Spawns window lacks today.

---

## 4. Icon strategy

**Recommendation: a small vector-path icon set, defined as path data in `UI.Shared`,
rendered by each framework's native `Path`. No icon font, no image assets, no dependency.**

Rationale, in priority order:

1. **Wine/CrossOver already broke on font-rendered icons twice** (#148, #166). A font-based
   set re-opens a bug the project has already paid for on the platforms that are its only
   uncontested ground.
2. Path geometry is data, so it lives beside `ThemePalettes` and gets the same
   anti-drift test treatment.
3. Vectors take the accent/text tokens as fill, so an icon can't be off-palette.
4. Size is controlled by us, not by a font's metrics — which is what makes 84 mixed glyphs
   look mismatched today.

**Scope: roughly 24 icons**, one per concept in the interaction vocabulary the brief lists
(close, pin, expand, collapse, navigate, guide, alert, search, filter, dismiss, more,
refresh, star, check, warning, timer, map, quest, gear, loot, spawn, charm, mez, buff).
Every one of the 84 current glyphs maps onto one of these or is dropped.

**Emoji are retained in exactly one place**: user-facing *text* where they are content
rather than UI (What's New entries, discussion templates). Not in controls.

---

## 5. WPF / Avalonia parity strategy

**Do not build shared XAML.** The brief's instinct is right and the repo has already proved
the failure mode twice: the Avalonia chip stacks shipped a hand-copied older version of the
WPF anchor and carried #122 *and* #152 to Linux and macOS after Windows had already paid for
both.

The pattern that works here is the one `ThemePalettes` already uses:

```
UI.Shared            (framework-free data + specs, unit-tested)
   ├── DesignTokens.cs        colours, type roles, spacing, radii, sizes
   ├── IconPaths.cs           path geometry per icon name
   └── ComponentSpecs.cs      per-component token composition
        ↓                                    ↓
WPF: ResourceDictionary          Avalonia: Styles + brush singletons
     + ControlTemplates                 + ControlThemes
```

Enforcement, so parity is checked rather than hoped for:

- A test asserting **every token key resolves in both UIs** — the direct analogue of
  `ThemePaletteTests`, which already does this for colour.
- A test asserting **no literal font size, radius or thickness** appears in migrated files
  (an allowlist that shrinks per gate). `ArchitectureTests` already does ratchet-style
  enforcement, so the mechanism exists.
- Avalonia migrates **in the same PR** as its WPF counterpart, never "a release behind".

---

## 6. Migration order

Adopting the brief's gates, with one change I'd argue for.

| Gate | Surface | Note |
|---|---|---|
| 1 | **This document** | — |
| 2 | **Quests** | Reference surface. Best choice: just rebuilt, so its *logic* is fresh and settled, and it exercises tabs, chips, search, filters, list rows, status badges and an empty state — 9 of the 12 primitives. |
| 3 | **Spawns + timers** ⟵ *moved up* | See below |
| 4 | Main widget | Cards, metrics, compact constraint |
| 5 | Mini mode + chips | The HUD vocabulary |
| 6 | Map | Heaviest surface; benefits from timers already being solved |
| 7 | Remaining windows | Gear, Drops, History, Travel, Options, breakouts |

**Why Spawns moves ahead of the widget:** `EqTimer` and `EqProgress` are needed by the
mini-mode chips, the map, and the widget's Watch/Buffs cards. Building them on the Spawns
window — where the current design is weakest and the improvement is most visible — means
the widget and chips gates *consume* finished primitives instead of inventing them under
the tightest space constraints in the app. It also front-loads the surface with a specific
brief ("should become one of EQBuddy's strongest real-time visual elements").

---

## 7. Known risks

**1 — On the widget, typography IS geometry.** This is the big one. Both widgets are
`SizeToContent`, so any change to font size, padding or spacing changes the *window size* of
a transparent, always-on-top window. On X11 that is a geometry change over a fullscreen
game, and it is exactly what cost KoboldCoterie his keyboard in #173. Gate 4 must treat
every metric change as a functional change: `WidgetMetrics` and `PerfReadout` exist because
of this, and reserved-size discipline has to survive the restyle.

**2 — The WPF layer has no unit tests.** Per `docs/TestPlan.md` §5 this is structural. So
every migrated surface needs facts pinned into the `EQBUDDY_EXPAND` dump and asserted from
`tests/EQBuddy.E2E` *before* it moves — the same discipline the brief's screenshot review
implies, made mechanical.

**3 — Eight themes, not one.** Every token change must hold in all eight, including
`HighContrast`. Deriving new tokens in `ThemeTones` rather than hand-picking per theme is
what keeps that tractable.

**4 — Screenshot review needs a fixture.** Reviewing real renders is required by the brief,
but the isolated profile shows zeros on every card and the windows are translucent, so a
naive capture is unusable. A seeded-session fixture (the `EQBUDDY_APPDATA` + shifted-log
recipe) and an opaque capture theme are prerequisites for Gate 2, not afterthoughts.

**5 — Scale and DPI.** Widget content sits under a UI-scale `LayoutTransform`; screen
coordinates and pre-scale units are different spaces, and mixing them breaks silently at
non-100% scale (#144). Any new component doing its own arithmetic must go through
`WidgetMetrics`.

**6 — Density is a feature.** EQBuddy's users read these surfaces mid-pull. Every spacing
increase must be justified against glanceability, not just tidiness. Where the two conflict,
the brief's own rule decides: primary values dominate, secondary context recedes — rather
than everything getting more room.

**7 — Scope creep into logic.** The brief forbids it and the codebase makes it tempting,
because presentation logic and state are genuinely close in places (`SpawnsViewModel` builds
display strings). Rule for this effort: a ViewModel may gain a *token or role name*; it may
not change what it computes.

---

## 8. What Gate 2 would deliver

Quests, rebuilt on the system above: header + tabs, prominent search, status filters, a
compact list with a detail panel, `EqListRow`/`EqStatusBadge`/`EqChip`/`EqEmptyState` in
their first real use, both UIs in one PR, existing tests green, new E2E facts pinned, and
reviewed screenshots — with functionality unchanged from today's tracker.
