# Build the current tree and silently install it on THIS machine only — the standing
# loop for testing changes before a release (David's rule: everything gets field-tested
# locally first). Touches neither OneDrive nor GitHub; the family keeps whatever
# release.ps1 last shipped. The updater won't fight it: it only ever offers NEWER
# versions than the one running.
#
#   pwsh scripts\install-local.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$csproj = Get-Content "$repo\src\EQBuddy\EQBuddy.csproj" -Raw
if ($csproj -notmatch '<Version>([\d.]+)</Version>') { throw 'No <Version> in csproj' }
$version = $Matches[1]
Write-Host "Installing EQBuddy $version locally (no release)"

Get-Process EQBuddy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

dotnet publish "$repo\src\EQBuddy\EQBuddy.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$repo\dist\publish"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -like '*EQBuddy*' } | Select-Object -First 1
if ($cert) {
    Set-AuthenticodeSignature -FilePath "$repo\dist\publish\EQBuddy.exe" -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com' | Out-Null
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) not found' }
& $iscc "/DAppVersion=$version" "$repo\installer\EQBuddy.iss" | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw 'installer compile failed' }
if ($cert) {
    Set-AuthenticodeSignature -FilePath "$repo\dist\EQBuddySetup.exe" -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com' | Out-Null
}

Start-Process "$repo\dist\EQBuddySetup.exe" -ArgumentList '/SILENT'
Write-Host "Installer launched (/SILENT); EQBuddy relaunches itself when it finishes."