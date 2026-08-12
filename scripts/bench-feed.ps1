# Feeds a scripted "session" into EQBuddy as a fake character (Testbuddy), for
# field-testing features the tester's real characters can't trigger — slows,
# raid kills, specific buffs. Run with the game OFF and EQBuddy running; EQBuddy
# follows the growing log within ~5s. Cleanup: delete the Testbuddy log (and
# raid-kills.json in EQBuddy's app data to erase the fake Nagafen kill).
#
#   pwsh scripts\bench-feed.ps1              (auto-detects the log folder)
#   pwsh scripts\bench-feed.ps1 -Logs "D:\EQ\Logs"
param([string]$Logs = '')

$ErrorActionPreference = 'Stop'
if (-not $Logs) {
    $Logs = @(
        'C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\Logs',
        'C:\Program Files (x86)\EverQuest Legends\Logs'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Logs) { throw 'Log folder not found — pass it with -Logs (shown in EQBuddy Options).' }
}
$f = Join-Path $Logs 'eqlog_Testbuddy_legends.txt'
Write-Host "Feeding $f"

function L([string]$m) {
    $ts = Get-Date -Format 'ddd MMM d HH:mm:ss yyyy'
    Add-Content $f "[$ts] $m"
    Write-Host "  $m"
    Start-Sleep -Milliseconds 1500
}

L 'You begin casting Armor of Faith.'
L 'You feel the favor of the gods upon you.'          # buff timer: 63:00 est
L 'You feel drowsy.'                                  # slow chip: range + voice
Start-Sleep -Seconds 8
L 'You feel less drowsy.'                             # slow clears on fade
L "Cleric1 tells the raid, 'CH inc'"                  # raid signal (raid-only toggle)
L 'You feel lethargic.'                               # slow chip: 40% + cure tooltip
L 'You slash a gnoll pup for 42 points of damage.'
L 'You have slain Lord Nagafen!'                      # raids card: 1/21
Write-Host 'Bench feed complete — check the Buffs card, slow chips, and Raids card.'
