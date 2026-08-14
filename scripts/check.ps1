<#
.SYNOPSIS
    Every gate that must pass before a commit, in one command.

.DESCRIPTION
    Build, unit tests, Avalonia tests. Prints one summary line per stage and returns a
    non-zero exit code if any of them fail, so it is equally usable by a human and by
    an agent that only reads the tail of the output.

    E2E is deliberately NOT included: it launches the real app and needs a desktop
    session. Run tests/EQBuddy.E2E by hand when touching ingest or the widget's wiring.

.EXAMPLE
    pwsh -NoProfile -File scripts/check.ps1
    pwsh -NoProfile -File scripts/check.ps1 -Quick   # skip the Avalonia suite
#>
[CmdletBinding()]
param([switch] $Quick)

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
$failed = @()

function Step([string] $name, [scriptblock] $body) {
    Write-Host "-- $name " -NoNewline
    $output = & $body 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED" -ForegroundColor Red
        # Only the lines that say why — a full MSBuild log buries the one that matters.
        $output | Select-String -Pattern 'error |Failed!|\[FAIL\]|Assert\.' |
            Select-Object -First 15 | ForEach-Object { Write-Host "   $_" }
        $script:failed += $name
    }
    else {
        $summary = $output | Select-String -Pattern 'Passed!|Build succeeded' |
            Select-Object -Last 1
        Write-Host "ok" -ForegroundColor Green -NoNewline
        if ($summary) { Write-Host "  $($summary -replace '\s+', ' ')" } else { Write-Host '' }
    }
}

Step 'build      ' { dotnet build "$repo\EQBuddy.slnx" -c Release --nologo -v q }
Step 'unit tests  ' { dotnet test "$repo\tests\EQBuddy.Tests\EQBuddy.Tests.csproj" -c Release --nologo }
if (-not $Quick) {
    Step 'avalonia    ' { dotnet test "$repo\tests\EQBuddy.Avalonia.Tests\EQBuddy.Avalonia.Tests.csproj" -c Release --nologo }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host 'All gates green.' -ForegroundColor Green
