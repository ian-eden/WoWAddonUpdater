# Builds both versions of the app into .\dist
#
#   WoWAddonUpdater.exe        Standalone. Runs on any 64-bit Windows 10/11 PC, nothing else to install.
#   WoWAddonUpdater-lite.exe   Small. Needs the .NET 10 Desktop Runtime installed.
#
# Usage:  .\build.ps1

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Checked {
    # Native tools (git, dotnet) write progress and warnings to stderr; only the exit code means failure.
    $ErrorActionPreference = 'Continue'
    & $args[0] $args[1..($args.Count - 1)] 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "'$($args -join ' ')' failed with exit code $LASTEXITCODE." }
}

$dist = Join-Path $PSScriptRoot 'dist'
$tmp = Join-Path $PSScriptRoot 'obj\dist-tmp'
foreach ($d in $dist, $tmp) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory $dist | Out-Null

Write-Host "Building standalone version..." -ForegroundColor Cyan
Invoke-Checked dotnet publish -c Release -o "$tmp\standalone" --self-contained true `
    -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

Write-Host "Building lite version..." -ForegroundColor Cyan
Invoke-Checked dotnet publish -c Release -o "$tmp\lite" --self-contained false

Copy-Item "$tmp\standalone\WoWAddonUpdater.exe" "$dist\WoWAddonUpdater.exe"
Copy-Item "$tmp\lite\WoWAddonUpdater.exe" "$dist\WoWAddonUpdater-lite.exe"
Remove-Item $tmp -Recurse -Force

Write-Host ""
Get-ChildItem $dist | ForEach-Object { "{0,-28} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
