# Screenshot fixture: a real EQBuddy.exe, a seeded session, an OPAQUE render.
#
# Two things made a capture unusable before this existed (2026-08-17):
#
#   1. An isolated EQBUDDY_APPDATA profile has no session, so every card renders
#      "0 dps / 0 kills / 0 items". Fixed by seeding the profile's log folder with the
#      time-shifted fixture (scripts/make-test-session.ps1) — the same recipe
#      tests/EQBuddy.E2E/FixtureLog.cs uses — so the app replays a rich session at
#      startup and the shot shows real numbers that are not a real person's.
#   2. Every window is translucent by design (it sits over a running game), so whatever
#      was behind it bled into the PNG. Fixed on two fronts: EQBUDDY_OPAQUE=1 makes the
#      window GROUND opaque (UI.Shared/CaptureTheme.cs), and a plain full-screen backdrop
#      sits behind everything so the rounded corners land on one flat colour instead of
#      the desktop.
#
# Nothing here touches the real profile: EQBUDDY_APPDATA points at a temp tree that is
# deleted afterwards unless -KeepProfile.
#
#   pwsh -NoProfile -File scripts/shoot.ps1                        # every shot
#   pwsh -NoProfile -File scripts/shoot.ps1 -Shot quest-tracker    # just one
#   pwsh -NoProfile -File scripts/shoot.ps1 -List                  # what it can shoot
#
# PREREQUISITE: dotnet build EQBuddy.slnx -c Release. This launches the BUILD output,
# not dist/publish, exactly like the E2E suite.
[CmdletBinding()]
param(
    # Which shots to take; omit for all of them. Names are the keys in $Shots below.
    [string[]]$Shot = @(),
    [string]$Out = '',
    # Behind every window, so a transparent corner lands on one flat colour. Neutral and
    # deliberately not a palette colour, so "outside the window" reads as outside.
    [string]$Backdrop = '#202225',
    [string]$Theme = 'ParchmentBrass',
    # Seconds to let the startup replay land after the window appears. There is no
    # readiness signal without EQBUDDY_EXPAND (which changes what the widget looks like,
    # so it cannot be forced on every shot) — this is a settle, not a handshake.
    [int]$Settle = 8,
    [switch]$KeepProfile,
    [switch]$List
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
if ($Out -eq '') { $Out = Join-Path $repo 'docs/screenshots' }

# --- what we can shoot -------------------------------------------------------------
# Title  = the window to capture, matched as a substring (scripts/shot.ps1).
# Env    = the EQBUDDY_* hook that opens it (the same family MainWindow already reads).
# Set    = extra settings.json overrides for this shot.
$Shots = [ordered]@{
    'widget-cards'    = @{ Title = 'EQBuddy'; Env = @{}; Set = @{} }
    'widget-expanded' = @{ Title = 'EQBuddy'; Env = @{ EQBUDDY_EXPAND = '1' }; Set = @{} }
    # One card, opened by name: a card's expanded state is not persisted, so a body can
    # only be photographed through this hook. EQBUDDY_EXPAND takes a comma-separated list
    # of the same keys SectionMap uses.
    'loot-card'       = @{ Title = 'EQBuddy'; Env = @{ EQBUDDY_EXPAND = 'loot' }; Set = @{} }
    'kills-card'      = @{ Title = 'EQBuddy'; Env = @{ EQBUDDY_EXPAND = 'kills' }; Set = @{} }
    # Gate 5b batch one, on one screen: motes and money take ICardContext (their rows are
    # items), faction takes none.
    'value-cards'     = @{ Title = 'EQBuddy'; Env = @{ EQBUDDY_EXPAND = 'motes,money,faction' }; Set = @{} }
    # The breakout needs no hook of its own: it shows whenever the widget is minimized and
    # its stat is starred, and both are plain settings. Session scope is the one with the
    # filter strips on it (Target is a different axis and hides them).
    # #182 (Ladylag): the damage-by-ability rows, in the narrow window she had. This is
    # the shot whose rows read ".", ".." and nothing at all.
    'damage-breakout' = @{ Title = 'Damage breakout'
                           Env = @{}
                           Set = @{ Minimized = $true; MiniStats = @('dps'); BreakoutDamageScope = 'session' } }
    'loot-breakout'   = @{ Title = 'Loot breakout'
                           Env = @{}
                           Set = @{ Minimized = $true; MiniStats = @('loot'); BreakoutLootScope = 'session' } }
    'quest-tracker'   = @{ Title = 'Quest Tracker'; Env = @{ EQBUDDY_QUESTS = '1' }; Set = @{} }
    'quest-tracker-all' = @{ Title = 'Quest Tracker'; Env = @{ EQBUDDY_QUESTS = 'all' }; Set = @{} }
    # The Plane of Sky checklist, staged so all three reward states are on one screen:
    # one turned in (offers Reopen), one with every piece held (offers Mark turned in),
    # one part-collected (offers neither). Ticks survive the catalog merge because
    # ApplyDefaultSkyQuestChecklist matches on Id and never touches Acquired.
    'sky-checklist'   = @{ Title = 'Quest Tracker'
                           Env = @{ EQBUDDY_QUESTS = 'sky' }
                           Set = @{
                               # Warrior, because that is what the fixture log infers.
                               SkyQuestCompleted = @('Warrior|Azure Ruby Ring')
                               SkyQuestChecklist = @(
                                   @{ Id = 'sky-194'; Acquired = $true }   # turned in
                                   @{ Id = 'sky-195'; Acquired = $true }
                                   @{ Id = 'sky-200'; Acquired = $true }   # every piece held
                                   @{ Id = 'sky-201'; Acquired = $true }
                                   @{ Id = 'sky-202'; Acquired = $true }
                                   @{ Id = 'sky-203'; Acquired = $true }   # part collected
                               )
                           } }
    'spawns-window'   = @{ Title = 'Spawn'; Env = @{ EQBUDDY_SPAWNS = 'Runnyeye Citadel' }; Set = @{ TrackSpawns = $true } }
    'options-window'  = @{ Title = 'Options'; Env = @{ EQBUDDY_OPTIONS = '1' }; Set = @{} }
    'zone-map'        = @{ Title = 'Zone Map'; Env = @{ EQBUDDY_MAP = '1' }; Set = @{} }
    'drops-window'    = @{ Title = 'Drops'; Env = @{ EQBUDDY_DROPS = '1' }; Set = @{} }
}

if ($List) {
    $Shots.Keys | ForEach-Object { "{0,-20} {1}" -f $_, $Shots[$_].Title }
    return
}

$wanted = if ($Shot.Count -gt 0) { $Shot } else { @($Shots.Keys) }
foreach ($name in $wanted) {
    if (-not $Shots.Contains($name)) { throw "Unknown shot '$name'. Try -List." }
}

$exe = Join-Path $repo 'src/EQBuddy/bin/Release/net10.0-windows/EQBuddy.exe'
if (-not (Test-Path $exe)) {
    throw "EQBuddy.exe not built at $exe. Run: dotnet build EQBuddy.slnx -c Release"
}

# The What's-new popup fires whenever LastSeenVersion trails the build, and it would sit
# over every shot. Read the shipping version rather than hardcoding one.
$version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

# --- the isolated profile ----------------------------------------------------------
$root = Join-Path ([IO.Path]::GetTempPath()) "eqbuddy-shoot-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
$profileDir = New-Item -ItemType Directory -Force (Join-Path $root 'profile')
$logsDir = New-Item -ItemType Directory -Force (Join-Path $root 'game/Logs')
# Existing but empty: UpdateChecker reads "configured folder, no EQBuddySetup.exe" as
# "no update", so no OneDrive scan and no GitHub call during a shoot.
$updateDir = New-Item -ItemType Directory -Force (Join-Path $root 'updates')

Write-Host "Profile: $profileDir"
& (Join-Path $PSScriptRoot 'make-test-session.ps1') -Out $logsDir.FullName | Write-Host

function Write-Settings([hashtable]$extra) {
    $s = @{
        LogFolder    = $logsDir.FullName
        UpdateFolder = $updateDir.FullName
        Theme        = $Theme
        WindowLeft   = 120
        WindowTop    = 120
        QuestsLeft   = 120
        QuestsTop    = 120
        Minimized    = $false
        # Every popup that would cover a shot, pre-answered.
        ShowTutorial = $false
        LastSeenVersion = $version
        WatchPinsMigrated = $true
        # No chip windows floating over the capture, and no log rewriting under it.
        TrackSpawns  = $false
        TruncateLogs = $false
        ArchiveLogs  = $false
        # Already current, so Load() doesn't add the built-in CC-broke rule and the
        # Tracked card shows only what the fixture actually earned.
        DefaultRulesVersion = 1
    }
    foreach ($k in $extra.Keys) { $s[$k] = $extra[$k] }
    $s | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $profileDir 'settings.json') -Encoding UTF8
}

# --- the backdrop ------------------------------------------------------------------
# A plain maximized form, NOT topmost, so the app's own always-on-top windows stay above
# it. This is what stops a rounded corner photographing the desktop.
# One assembly per call: the comma-list form silently loads neither here (pwsh 7).
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$backdropForm = New-Object System.Windows.Forms.Form
$backdropForm.FormBorderStyle = 'None'
$backdropForm.WindowState = 'Maximized'
$backdropForm.BackColor = [System.Drawing.ColorTranslator]::FromHtml($Backdrop)
$backdropForm.ShowInTaskbar = $false
$backdropForm.Show()
$backdropForm.Refresh()

New-Item -ItemType Directory -Force $Out | Out-Null
$taken = @()
try {
    foreach ($name in $wanted) {
        $spec = $Shots[$name]
        Write-Host "`n=== $name → $($spec.Title) ==="
        Write-Settings $spec.Set

        $psi = New-Object Diagnostics.ProcessStartInfo $exe
        $psi.UseShellExecute = $false
        $psi.EnvironmentVariables['EQBUDDY_APPDATA'] = $profileDir.FullName
        $psi.EnvironmentVariables['EQBUDDY_OPAQUE'] = '1'
        foreach ($k in $spec.Env.Keys) { $psi.EnvironmentVariables[$k] = $spec.Env[$k] }
        $proc = [Diagnostics.Process]::Start($psi)
        try {
            # Wait for the window this shot is about, then let the replay settle.
            $deadline = (Get-Date).AddSeconds(90)
            $seen = $false
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 500
                if ($proc.HasExited) { throw "$exe exited early (code $($proc.ExitCode))." }
                $proc.Refresh()
                if (Get-Process -Id $proc.Id | Where-Object { $_.MainWindowTitle -like "*$($spec.Title)*" }) {
                    $seen = $true; break
                }
                # Satellite windows are not MainWindowTitle; shot.ps1 enumerates properly,
                # so once the app has ANY window, hand off to it after the settle.
                if ($proc.MainWindowHandle -ne 0) { $seen = $true; break }
            }
            if (-not $seen) { throw "No window appeared for '$name' within 90s." }
            $backdropForm.Refresh()
            Start-Sleep -Seconds $Settle

            $png = Join-Path $Out "$name.png"
            & (Join-Path $PSScriptRoot 'shot.ps1') -TitleLike $spec.Title -Out $png | Write-Host
            $taken += $png
        }
        finally {
            if (-not $proc.HasExited) { $proc.Kill($true) }
            $proc.WaitForExit(10000) | Out-Null
        }
    }
}
finally {
    $backdropForm.Close()
    $backdropForm.Dispose()
    if ($KeepProfile) { Write-Host "`nProfile kept at $root" }
    else { Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue }
}

Write-Host "`n$($taken.Count) shot(s):"
$taken | ForEach-Object { Write-Host "  $_" }
