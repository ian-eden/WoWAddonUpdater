# Publishes a new version on GitHub. Everyone's app will offer the update the next time it starts.
#
# Usage:  .\release.ps1 -Version 0.2.0 -Notes "Added X, fixed Y"
#
# What it does:
#   1. Sets <Version> in WoWAddonUpdater.csproj
#   2. Builds both exes (build.ps1)
#   3. Commits all changes, tags the commit vX.Y.Z and pushes to GitHub
#   4. Creates the GitHub release with both exes attached

param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Checked {
    & $args[0] $args[1..($args.Count - 1)]
    if ($LASTEXITCODE -ne 0) { throw "'$($args -join ' ')' failed with exit code $LASTEXITCODE." }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
$tag = "v$Version"
if (git tag --list $tag) { throw "Tag $tag already exists. Pick a higher version." }

$proj = Join-Path $PSScriptRoot 'WoWAddonUpdater.csproj'
$text = [IO.File]::ReadAllText($proj)
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($proj, $text)

& (Join-Path $PSScriptRoot 'build.ps1')

Invoke-Checked git add -A
if (git status --porcelain) { Invoke-Checked git commit -m "Release $tag" }
Invoke-Checked git tag $tag
Invoke-Checked git push origin HEAD
Invoke-Checked git push origin $tag

$body = @"
$Notes

### Download
- **WoWAddonUpdater.exe**: use this one. Works on any 64-bit Windows 10/11 PC with nothing else to install.
- **WoWAddonUpdater-lite.exe**: much smaller, but needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Already have it? The app offers this update by itself when you open it.
"@

Invoke-Checked gh release create $tag "dist\WoWAddonUpdater.exe" "dist\WoWAddonUpdater-lite.exe" --title $tag --notes $body
Write-Host "Released $tag" -ForegroundColor Green
