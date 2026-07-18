# EQBuddy release: publish exe, compile installer, refresh zip, push to OneDrive for family.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$oneDrive = 'C:\Users\david\OneDrive\EQBuddyDownload'

Get-Process EQBuddy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

dotnet publish "$repo\src\EQBuddy\EQBuddy.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$repo\dist\publish"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) not found' }
& $iscc "$repo\installer\EQBuddy.iss"
if ($LASTEXITCODE -ne 0) { throw 'installer compile failed' }

Compress-Archive -Path "$repo\dist\publish\EQBuddy.exe", "$repo\README.md" `
    -DestinationPath "$repo\dist\EQBuddy-portable.zip" -Force

New-Item -ItemType Directory -Force $oneDrive | Out-Null
Copy-Item "$repo\dist\EQBuddySetup.exe", "$repo\dist\EQBuddy-portable.zip" $oneDrive -Force
Write-Host "Released to $oneDrive"
