# Security

EQBuddy runs next to your game and reads your log file. That's a position of
trust, so this page says exactly what the app does with it — every network
connection, every file it writes, and how updates are verified. If anything
here stops matching the code, that's a bug; report it like one.

## Supported versions

The latest release, only. EQBuddy checks for updates at startup and every
6 hours and offers the new version in a banner, so staying current is one
click — and on the family/OneDrive channel the installer syncs down within a
few hours of release. There are no maintained older branches; a security fix
ships as the next release.

## Every network destination, and why

EQBuddy's rule is **log-only, zero telemetry**: it never sends your data
anywhere on its own. The complete list of hosts the app itself contacts:

| Host | When | What |
|---|---|---|
| `eqlwiki.com` | You hover/click an item, open item info, or use the drops/quest views | Read-only MediaWiki API lookups of item and mob pages. Responses are cached locally for a week and labelled LIVE / CACHED / STALE. Your search term is the only thing in the request. |
| `api.github.com` | Startup, every 6 h, and right-click → "Check for updates" | A read-only request for the latest release's version number and asset list. Nothing about you or your session is attached. |
| `github.com` | Only when you click the update banner | Downloads `EQBuddySetup.exe` and its published `.sha256` from the release you were just shown. |

That's the whole list. The family/guild update channel is not a network
request at all: EQBuddy reads `EQBuddySetup.exe` from a locally synced
OneDrive folder (`EQBuddyDownload`) — OneDrive does the transport, EQBuddy
just checks the file that's already on disk.

Some buttons open your **browser** at a site — the app never fetches these
itself, it hands the URL to your default browser and steps away:

- eqlwiki.com pages (item info "open wiki page", wiki search, the ✦ wiki
  contribution edit links)
- GitHub: the repository, the releases page, and pre-filled Discussion drafts
  (Send feedback, Submit-to-EQBuddy zone shares, the quest ⚑ report). These
  open as **drafts in your browser for you to review and post under your own
  account** — the app posts nothing.
- eqmaps.info (the map window's "Get maps…" button, linking Brewall's map packs)
- eqlegendstools.com (the char-sheet link in Options)

## Zero telemetry

There is no analytics endpoint, no crash reporter, no usage ping, no
"anonymous statistics". Errors go to a local file
(`%AppData%\EQBuddy\error.log`), full stop. When knowledge moves between
players it moves because a player chose to move it: share strings you paste
to a friend, the ✦ Copy-for-wiki button that fills your clipboard, feedback
drafts you post yourself. If you ever catch EQBuddy sending something this
page doesn't list, that is a vulnerability — report it as one.

## What it writes locally

Everything lives under `%AppData%\EQBuddy\` (or wherever `EQBUDDY_APPDATA`
points): `settings.json`, `history.db` (your session history, SQLite),
`error.log`, and the per-character ledgers and archives (AA ledger, quest
ledger, spawn-point archives).

The log janitor is the one thing that touches files outside that folder,
and it asks first — the tutorial's opening page asks whether EQBuddy may
auto-empty finished-session logs, and nothing is touched until you answer:

- It sets `Log=1` in the game's `eqclient.ini`, only while the game is closed.
- With auto-empty **on**, a character log quiet for 60+ minutes (a finished
  session) is emptied — never deleted. With *"Keep a timestamped copy before
  emptying"* on, the content is first archived to `Logs\archive\` as its own
  file.
- With auto-empty **off**, EQBuddy never touches your log files.
- The janitor stands down completely while the game, GINA, or GamParse is
  running, so no other tool's read position is ever yanked out from under it.

## Update trust model

- Every release publishes `EQBuddySetup.exe.sha256` beside the installer. The
  auto-updater stages the installer to a temp file and verifies it against
  that hash before running it; a mismatch deletes the staged file and aborts.
- **Fail closed:** if a GitHub release has no published hash, the updater
  refuses to download at all and points you at the release page instead. The
  same verification runs on installers taken from the OneDrive/family folder.
- Installers are currently signed with a **self-signed certificate**, which
  is why Windows SmartScreen/Defender sometimes grumbles (see the README's
  security note). A publicly trusted certificate (Azure Trusted Signing) is
  in identity validation now; once it lands, releases ship fully signed, and
  the updater is slated to additionally validate the publisher identity on
  staged installers — hash verification proves the file is the published one,
  signature validation will prove who published it.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: **Security → Report a
vulnerability** on this repository (it's enabled). You'll reach the
maintainer directly and privately, and you'll get credit in the fix's release
notes unless you'd rather not. For anything that isn't sensitive — a
suspicious warning, a hardening idea — a public
[Discussion](https://github.com/DranakCorps-bot/EQBuddy/discussions) is fine
too.
