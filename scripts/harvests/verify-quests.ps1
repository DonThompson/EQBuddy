# Full-catalog accuracy audit (2026-08-11, David: "being wrong once drops faith in
# everything"): fetches every quest's wiki page and confirms each parsed turn-in
# item (and the giver) appears in the page text. Run after big harvest changes.
# 2026-08-11 baseline: 917 quests, ZERO item mismatches; 23 giver-string findings,
# all multi-giver combined strings or loc-suffixed givers (display-only, not errors).
# 2026-08-11 (post section-splitting) baseline: 1167 quests (250 chain/armor-set
# steps), still ZERO split-attributable item mismatches; giver-string findings grow
# to ~87 because steps inherit their chain page's combined giver — same benign class.
# ("Cleric Plane of Sky Tests" may report items until the weekly refresh catches the
# live page edit; the app replaces that aggregate at load via SkyTestSplit.)
# Full-catalog verification: for every quest, fetch its wiki page and confirm each
# parsed turn-in item and the quest giver actually appear in the page text.
# Polite: ~4 concurrent, api.php only. Output: verify-report.txt (mismatches only).
$ErrorActionPreference = 'Continue'
param([string]$OutDir = ".")
$scratch = $OutDir
$j = Get-Content "$PSScriptRoot\..\..\src\EQBuddy.Core\Data\QuestCatalog.json" -Raw | ConvertFrom-Json
$quests = $j.Quests

function Get-Title($q) {
    if ($q.Url -match '/([^/]+?)(?:\?|$)') { return [Uri]::UnescapeDataString($Matches[1]) -replace '_', ' ' }
    return $q.Name
}

$report = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$done = 0
$quests | ForEach-Object -ThrottleLimit 4 -Parallel {
    $q = $_
    $bag = $using:report
    $title = if ($q.Url -match '/([^/]+?)(?:\?|$)') { [Uri]::UnescapeDataString($Matches[1]) -replace '_', ' ' } else { $q.Name }
    try {
        $url = "https://eqlwiki.com/api.php?action=query&prop=revisions&rvprop=content&format=json&redirects=1&titles=" +
            [Uri]::EscapeDataString($title)
        $resp = Invoke-RestMethod -Uri $url -TimeoutSec 20 -Headers @{ "User-Agent" = "EQBuddy-verify/1.0 (catalog audit)" }
        $page = $resp.query.pages.PSObject.Properties.Value | Select-Object -First 1
        if ($page.PSObject.Properties.Name -contains "missing") { $bag.Add("PAGE MISSING: '$($q.Name)' (title '$title')"); return }
        $text = $page.revisions[0].'*'
        if (-not $text) { $bag.Add("EMPTY PAGE: '$($q.Name)'"); return }
        $fold = $text -replace '`', ''
        foreach ($item in $q.Items) {
            $needle = $item.Name -replace '`', ''
            if ($fold.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                $bag.Add("ITEM NOT ON PAGE: '$($q.Name)' -> '$($item.Name)'")
            }
        }
        if ($q.QuestGiver -and $fold.IndexOf(($q.QuestGiver -replace '`', ''), [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $bag.Add("GIVER NOT ON PAGE: '$($q.Name)' -> '$($q.QuestGiver)'")
        }
        Start-Sleep -Milliseconds 150
    }
    catch { $bag.Add("FETCH FAILED: '$($q.Name)' ($($_.Exception.Message))") }
}
$lines = $report | Sort-Object
$lines | Set-Content "$scratch\verify-report.txt"
"verified $($quests.Count) quests; $($lines.Count) findings -> verify-report.txt"
