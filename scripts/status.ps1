<#
.SYNOPSIS
    One call that answers "where did we leave off?".

.DESCRIPTION
    Written 2026-08-14 because opening a session meant running the same six or seven
    commands every time: what version, what's uncommitted, what's unpushed, what's open
    on GitHub, who is waiting for a reply, and how much room the hotspots have left.

    Read-only. Nothing here writes, releases, or posts.

    The discussion section is the one worth reading first: a thread whose last comment
    is from someone else is somebody waiting on us, and that is the easiest thing in
    this project to let slide.

.EXAMPLE
    pwsh -NoProfile -File scripts/status.ps1
    pwsh -NoProfile -File scripts/status.ps1 -NoNetwork    # skip GitHub, offline/fast
#>
[CmdletBinding()]
param([switch] $NoNetwork)

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
$owner = 'DranakCorps-bot'; $name = 'EQBuddy'

function Head([string] $t) { Write-Host "`n$t" -ForegroundColor Cyan }

# ---- version and release state ----
$props = Get-Content "$repo\Directory.Build.props" -Raw
$version = if ($props -match '<Version>([\d.]+)</Version>') { $Matches[1] } else { '?' }
$whatsNew = Get-Content "$repo\src\EQBuddy.Core\Data\WhatsNew.json" -Raw | ConvertFrom-Json
$hasEntry = [bool]($whatsNew | Where-Object { $_.version -eq $version })
$tagged = (git -C $repo tag --list "v$version") -ne $null -and (git -C $repo tag --list "v$version").Count -gt 0

Head 'VERSION'
Write-Host "  Directory.Build.props : $version"
Write-Host "  What's-new entry      : $(if ($hasEntry) { 'present' } else { 'MISSING - release.ps1 will refuse' })" `
    -ForegroundColor $(if ($hasEntry) { 'Gray' } else { 'Yellow' })
Write-Host "  Tag v$version          : $(if ($tagged) { 'exists (already released)' } else { 'not tagged (staged, not released)' })"

# ---- working tree ----
Head 'GIT'
$dirty = git -C $repo status --porcelain
if ($dirty) {
    Write-Host "  uncommitted:" -ForegroundColor Yellow
    $dirty | Select-Object -First 12 | ForEach-Object { Write-Host "    $_" }
}
else { Write-Host '  working tree clean' }
$ahead = git -C $repo rev-list --count '@{u}..HEAD' 2>$null
if ($ahead -and [int]$ahead -gt 0) { Write-Host "  UNPUSHED commits: $ahead" -ForegroundColor Yellow }
Write-Host '  recent:'
git -C $repo log --oneline -5 | ForEach-Object { Write-Host "    $_" }

# ---- the ratchet, which is the thing that silently runs out ----
Head 'HOTSPOT HEADROOM'
$arch = Get-Content "$repo\tests\EQBuddy.Tests\ArchitectureTests.cs" -Raw
foreach ($m in [regex]::Matches($arch, '\(@"([^"]+)",\s*(\d+)\)')) {
    $rel = $m.Groups[1].Value -replace '\\', '/'
    $base = [int]$m.Groups[2].Value
    # A hotspot path may be a glob, and then the ratchet sums its matches — a partial
    # must not be able to make this readout look roomier than the gate.
    $files = @(Get-ChildItem (Join-Path $repo "src/$rel") -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { continue }
    $now = ($files | ForEach-Object { (Get-Content $_.FullName).Count } | Measure-Object -Sum).Sum
    $limit = [int]($base * 1.1)
    $left = $limit - $now
    $colour = if ($left -lt 60) { 'Red' } elseif ($left -lt 200) { 'Yellow' } else { 'Gray' }
    Write-Host ("  {0,-34} {1,5} / {2,5}   {3,5} left" -f $rel, $now, $limit, $left) -ForegroundColor $colour
}

if ($NoNetwork) { Write-Host "`n(skipped GitHub)`n"; return }

# ---- what's open on GitHub ----
Head 'OPEN PRs'
gh pr list --repo "$owner/$name" --state open --json number,title,author,mergeable,updatedAt `
    --template '{{range .}}  #{{.number}} [{{.author.login}}] {{.title}}  ({{.mergeable}}){{"\n"}}{{end}}' 2>$null

Head 'OPEN ISSUES'
gh issue list --repo "$owner/$name" --state open --limit 20 `
    --json number,title,author --template '{{range .}}  #{{.number}} [{{.author.login}}] {{.title}}{{"\n"}}{{end}}' 2>$null

# A thread whose last word is somebody else's is a thread waiting on us.
Head 'DISCUSSIONS AWAITING A REPLY'
$q = @'
{ repository(owner:"OWNER", name:"NAME") {
    discussions(first:25, orderBy:{field:UPDATED_AT, direction:DESC}) {
      nodes { number title updatedAt author{login} comments(last:1){ nodes{ author{login} } } } } } }
'@ -replace 'OWNER', $owner -replace 'NAME', $name

$nodes = (gh api graphql -f query=$q 2>$null | ConvertFrom-Json).data.repository.discussions.nodes
$waiting = $nodes | Where-Object {
    $last = if ($_.comments.nodes.Count) { $_.comments.nodes[-1].author.login } else { $_.author.login }
    $last -ne $owner
}
if ($waiting) {
    $waiting | ForEach-Object {
        $last = if ($_.comments.nodes.Count) { $_.comments.nodes[-1].author.login } else { $_.author.login }
        Write-Host ("  #{0} [{1}] {2}" -f $_.number, $last, $_.title) -ForegroundColor Yellow
    }
}
else { Write-Host '  none - every thread has our reply last' -ForegroundColor Green }
Write-Host ''
