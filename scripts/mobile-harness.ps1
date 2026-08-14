<#
.SYNOPSIS
    Builds a drivable copy of the EQBuddy Mobile page so its map can be worked on
    without a PC, a phone, a pairing code or a live log.

.DESCRIPTION
    Two edits, and only two: the pairing token is hard-coded (so boot() runs), and
    WebSocket is stubbed out (so snapshots can be pushed in by hand). Everything under
    test — the map's SVG drawing, the fade curve, the counter-scaling, the panel
    layout — is the shipped code, because the file is a copy of the shipped file.

    Open the result in any browser and push a snapshot from the console:

        __PUSH({ kind: "snapshot", protocol: 2, identity: {...}, offered: ["map"], map: {...} })

    The shape is CompanionSnapshot with camelCase names; CompanionMapSourceTests is
    the authority on what the server actually sends.

    Output lands in dist/ because that folder is gitignored — the harness is a tool,
    not a deliverable, and it must never be mistaken for the page itself.

.NOTES
    Written 2026-08-15 while restoring the breadcrumb trail. It immediately earned its
    keep: driving the real page at zoom exposed that text.poi's CSS font-size had been
    beating the counter-scaling attribute, so every map label had been ballooning on
    zoom since the map shipped. Unit tests could not have seen that.
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $PSScriptRoot '..\dist\mobile-harness')
)

$ErrorActionPreference = 'Stop'

$src = Join-Path $PSScriptRoot '..\src\EQBuddy.Companion\Web\index.html'
if (-not (Test-Path $src)) { throw "Phone page not found at $src" }
$html = Get-Content $src -Raw

# Both anchors are asserted rather than assumed: a silent no-op here would look like a
# page that mysteriously stopped working, which is the worst way to lose an afternoon.
$tokenLine = 'const token = fragmentToken || remembered || "";'
if (-not $html.Contains($tokenLine)) {
    throw "The page's token line has moved — update scripts/mobile-harness.ps1 to match."
}
$html = $html.Replace($tokenLine, 'const token = "harness";')

$stub = @'
<script>
// Harness only: stand in for the PC so boot() can run. __PUSH(msg) delivers a snapshot
// exactly as the server's WebSocket would.
//
// __PUSH resolves the CURRENT socket when it is CALLED, rather than closing over the one
// that existed when it was defined. The page reconnects on its own schedule, and a
// devtools/automation context can outlive a reload — a captured socket goes quietly
// stale and every push lands on a dead object with no error to show for it.
window.__SOCK = null;
window.__SENT = [];
window.WebSocket = class {
  constructor() {
    this.readyState = 1;
    window.__SOCK = this;
    setTimeout(() => this.onopen && this.onopen(), 0);
  }
  send(s) { try { window.__SENT.push(JSON.parse(s)); } catch { window.__SENT.push(s); } }
  close() {}
};
window.__PUSH = m => {
  const s = window.__SOCK;
  if (!s || !s.onmessage) throw new Error("harness: no socket yet — the page has not booted");
  s.onmessage({ data: JSON.stringify(m) });
};
</script>
<script>
'@

$patched = [regex]::Replace($html, '<script>(\r?\n)"use strict";',
    { param($m) $stub + $m.Groups[1].Value + '"use strict";' }, 1)
if ($patched -eq $html) {
    throw "The page's main <script> opener has moved — update scripts/mobile-harness.ps1 to match."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir 'harness.html'
Set-Content -Path $out -Value $patched -Encoding UTF8
Write-Output (Resolve-Path $out).Path
