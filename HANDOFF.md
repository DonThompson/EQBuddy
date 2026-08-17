# EQBuddy — handoff

Paste this as your first message next session.

Don't re-derive the codebase. `CLAUDE.md` loads automatically and carries the commands, the
non-negotiable rules, the where-things-live index, the trap list (11), and the
surface-allocation rule. `docs/Architecture.md` and `docs/TestPlan.md` sit behind it;
`DocumentationTests` fails the build if any go stale. **Start with
`pwsh -NoProfile -File scripts/status.ps1`.**

Gates: `pwsh -NoProfile -File scripts/check.ps1` (never pipe it into `tail` and chain a commit
off it — the pipe swallows the exit code). E2E is separate and needs a desktop session:
`dotnet build EQBuddy.slnx -c Release` then
`dotnet test tests/EQBuddy.E2E/EQBuddy.E2E.csproj -c Release`.

---

## State: everything is shipped and the board is clear

**v1.88.3 is live** (tag, GitHub release, OneDrive, installed locally). Working tree clean,
everything pushed, **1,517 unit + 110 Avalonia + 9 E2E green**.

**Zero open PRs. Zero issues awaiting a first response. Every discussion has our reply last
except #182, which was answered minutes before this was written.**

**Releases are no longer blocked.** One early attempt hit the permission classifier; after
that `scripts/release.ps1` ran fine and cut 1.88.1, 1.88.2 and 1.88.3. **David's standing
rule still holds — releases wait for his explicit go.** Ask "want me to cut it?" rather than
handing him a command block; doing the latter once caused two people to run the release two
minutes apart and produced the 1.88.0/1.88.1 split.

## THE NEXT TASK — David set the direction explicitly

> "we lock scope for a while and focus on the re-implementation we started with quests"

**Scope is locked. Do not take on new feature areas.** He has not yet said which reading of
"the re-implementation we started with quests" he means, and **this is the first thing to
ask him**, because the two readings lead to different work:

1. **Continue the quest surfaces** — the #174 features that hang off quest data (below), or
2. **Apply the quest PATTERN elsewhere** — one definition in Core, three tabs, every surface
   (WPF, Avalonia, mobile) reading it and unable to disagree. That pattern is what made the
   quest work hold together, and it is the reusable part.

**Approved and waiting: #174 (n3cr0nk1tt3n), all three QoL asks, David approved all of them.**
Every one is phone/tablet by the CLAUDE.md surface filter:

1. **Mob lookup** by name → zone, camp loc, drops, **faction hits**. We already parse faction
   lines and only count them; the mob pages are already harvested for the map's camps and
   Drops by Creature.
2. **"What is this for?" from a Loot row** → quests, recipes, what drops it, cross-linked.
   Closest to shipping: loot rows already know quest turn-ins, and the tracker already
   searches by item. The gap is the reverse direction.
3. **Upgrade preview at +N** with mote cost and breakpoints — "most-requested in game" per
   the reporter. The arithmetic is now firm from #154: exp-per-mote and the
   cost-to-reach-a-tier curve (1, 2, 4, 8 … 512) are both verified against the wiki.

## Open, with the ball in someone else's court — do not chase

- **#182 (Ladylag)** — combat-breakout ability names truncated, "no way to widen the window".
  **Diagnosed:** the window IS resizable and the width IS persisted per breakout kind, but
  it is `WindowStyle="None" AllowsTransparency="True"`, so the resize border is invisible and
  a couple of pixels wide. **The capability is fine; the affordance is missing.** She has the
  two workarounds (drag the very edge, Ctrl+wheel to shrink text). Asked which breakout, how
  long the names are, and whether dragging works once told. **A real fix is making the edge
  findable** — plus a tooltip showing the full name on a truncated row, which is worth doing
  regardless. This is the strongest small task if you want one.
- **#177 (chrstahl)** — fixed in 1.88.3; asked whether charm pets now bind without the
  `/pet who leader` prompt, and whether any wrong pet survives.
- **#173 (KoboldCoterie)** — keyboard fix confirmed by him with `xprop`; title-bar regression
  fixed in 1.88.2. **The always-on-top-over-fullscreen half is still open and untouched on
  purpose** — X11 stacking belongs to the window manager. Asked whether a KWin "Block
  compositing: No" rule on EQ changes it.
- **#169 (joma65)** — the second-copy fix shipped; asked for `pgrep -a -i eqbuddy` and whether
  `error.log` gains the new "changed underneath this EQBuddy" line.
- **#153 (adndmike)** — fix shipped but **unverified by ear**; the manual pass (rename a chosen
  `.wav`, expect a banner naming it) needs a desktop session with sound.
- **#123** — donation link, parked on David's identity review. He will post when it clears.
- **liminalwarmth offered three more fixes** he already has running locally and will PR: a due
  timer pruned before its alert fires across a >60 s tick gap (laptop sleep) — a silently
  missed alert and the real one — the Avalonia chip gauge only repainting at rebuild, and
  `Countdown.Format` printing "60s" at the minute crossing. **Say yes.**

## Landed this session, so you don't re-do it

1.88.0–1.88.3 carry: the **mobile quest surface** (three tabs, ~1,200-quest index shipped once
per device by stamp, tap-to-track); the widget's **Quests card** replacing Sky Quest and Epics
and opening the tracker; **Avalonia's three quest tabs**; hand-ticking restored in both
desktop trackers; **#153** sound volume; **#175** Beastlord (missing from all six class
pickers); **#176** Golden Hilt isle; **#154** mote potency + two ladder errors of ours;
**#120** class detection; **#169** Linux/macOS running a SECOND COPY every launch; **#173**
the readout resizing the widget; **#177** per-spell charm windows + `Allure of Death` inventing
pets; and PRs **#166**, **#171**, **#178** (minus its prebuilt binary), **#179**, **#181**.

## Standing decisions and hard lines

- **eqlwiki is the tie-breaker** (David, 2026-08-16, from #163): other sources where the wiki
  is silent; the wiki wins where they disagree; anything sourced elsewhere is marked as such.
- **EQ Legends Companion (`jmoyers/everquest-companion`) is FSL-1.1-MIT.** EQBuddy is a
  competing product. **Ideas yes, code never** — do not read, fetch or port it. The #177
  per-spell charm window was adopted this way, from a written description only.
- **No prebuilt binaries in the repo** (David, 2026-08-16, on #178): the CrossOver script
  builds `winemac.so` on the user's machine from CodeWeavers' source plus the shipped patch.
  A log-only widget cannot ask players to trust an unreadable blob that replaces a library
  inside their CrossOver install.
- Curated catalogs are never auto-written — but **LEARNED data is**, and #181 was exactly that
  hole: a placeholder's death taught a named's respawn. Watch that class of bug.

## Notes that cost time this session

- **Derived artifacts get REGENERATED, never hand-merged.** Three contributor PRs conflicted
  almost entirely on generated files. `buffs-harvest.py`, `fades-harvest.py` and the new
  `charms-harvest.py` all run OFFLINE from `spells.json`, so the resolution is: take the
  contributor's script change, re-run, compare. Twice the regenerated result was a strict
  superset of the PR's by exactly one entry — the union proving itself.
- **WebFetch refuses verbatim quotes** (copyright guard) and returns paraphrase, which is why
  two reads of one wiki page contradicted each other on mote tiers. For anything that must be
  quoted exactly, drive a browser and read `get_page_text`, or fetch raw wikitext via
  `action=edit`.
- **The wiki contradicts itself on mote tier numbering** — 0-based on Mote Guide (whose column
  is actually "Item Tier Limit"), 1-based on Item Upgrade System, and both inside one paragraph
  of Constructed Potential. `Motes.cs` deliberately stores NO tier index. Read the comment
  before "fixing" it.
- **Download the screenshot and look at it.** #173's second report was unreadable as prose
  ("the title bar looks like this") and obvious as an image — the character name standing
  vertically, one letter per line. `curl` it and open it with the Read tool.
- **A fix in `UI.Shared` must reach BOTH UIs.** The #173 perf readout was fixed in Avalonia
  and left hand-copied in WPF; that is the exact pattern that carried #122 and #152 to Linux.
- Discussion replies need the GraphQL `addDiscussionComment` mutation (`gh` has no native
  command); issues take `gh issue comment`. Set `PYTHONIOENCODING=utf-8` before piping any
  GitHub text through Python on this machine, or emoji crash the pipe.
