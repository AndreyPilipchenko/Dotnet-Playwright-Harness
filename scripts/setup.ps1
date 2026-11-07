<#
.SYNOPSIS
Installs the Playwright .NET harness into a project, or rebuilds it after a clone.

.DESCRIPTION
Run from the harness repository (scripts/setup.ps1) to install or refresh the harness in a project:
it copies the tool source, templates and Claude Code payload into the project, builds the tool,
records .harness/project.json and merges the harness hooks into .claude/settings.json.

Run from a project (.harness/setup.ps1) after cloning it: the payload is already committed, so it
only builds the tool and re-merges the hooks. Until this runs, the hooks fail open and guard nothing.

Safe to re-run. A payload file you changed locally is never overwritten: the new version is written
next to it as <file>.harness-new and reported as a conflict.

Works on Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Target
The project root. Default: the current directory, or the folder above .harness when run from a project.

.PARAMETER Framework
nunit, xunit or mstest. Needed on the first install; later runs use .harness/project.json.

.PARAMETER Name
The project name used by scaffold, such as Acme.Shop. Default: from project.json or the folder name.

.PARAMETER TargetFramework
The target framework for generated projects. Default: from project.json or net10.0.

.EXAMPLE
scripts/setup.ps1 -Target C:\src\shop-tests -Framework nunit -Name Acme.Shop

.EXAMPLE
.harness/setup.ps1
#>
[CmdletBinding()]
param(
    [string]$Target,
    [ValidateSet('nunit', 'xunit', 'mstest')]
    [string]$Framework,
    [string]$Name,
    [string]$TargetFramework
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$MinimumDotnetMajor = 10

function Get-NormalizedHash([string]$Path) {
    # Line endings are ignored, so a checkout with autocrlf does not look like a local change.
    $text = [IO.File]::ReadAllText($Path).Replace("`r", '')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))
        return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-PayloadFiles([string]$HarnessRoot) {
    # Returns @{ <project-relative destination> = <source path> } for everything setup copies.
    $map = [ordered]@{}
    $skip = '[\\/](bin|obj)[\\/]'

    $sets = @(
        @{ Source = 'tools\Harness.Tools'; Destination = '.harness/tools/Harness.Tools' },
        @{ Source = 'templates'; Destination = '.harness/templates' },
        @{ Source = '.claude'; Destination = '.claude' }
    )
    foreach ($set in $sets) {
        $folder = Join-Path $HarnessRoot $set.Source
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        $files = Get-ChildItem -LiteralPath $folder -Recurse -File | Sort-Object FullName
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($folder.Length).TrimStart('\', '/')
            if (('\' + $relative) -match $skip) { continue }
            if ($set.Source -eq '.claude' -and $relative -match '^settings(\.local)?\.json$') { continue }
            $map[($set.Destination + '/' + $relative.Replace('\', '/'))] = $file.FullName
        }
    }

    $singles = [ordered]@{
        'tools\Directory.Build.props'    = '.harness/tools/Directory.Build.props'
        'tools\Directory.Packages.props' = '.harness/tools/Directory.Packages.props'
        'scripts\setup.ps1'              = '.harness/setup.ps1'
        'scripts\update.ps1'             = '.harness/update.ps1'
        'LICENSE'                        = '.harness/LICENSE'
        'NOTICE.md'                      = '.harness/NOTICE.md'
        '.mcp.json'                      = '.mcp.json'
    }
    foreach ($source in $singles.Keys) {
        $path = Join-Path $HarnessRoot $source
        if (Test-Path -LiteralPath $path -PathType Leaf) { $map[$singles[$source]] = $path }
    }

    return $map
}

function Read-Manifest([string]$Path) {
    $known = @{}
    if (Test-Path -LiteralPath $Path) {
        $json = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
        if ($null -ne $json -and $null -ne $json.PSObject.Properties['files']) {
            foreach ($property in $json.files.PSObject.Properties) { $known[$property.Name] = [string]$property.Value }
        }
    }
    return $known
}

function Write-Manifest([string]$Path, [hashtable]$Files) {
    $ordered = [ordered]@{}
    foreach ($key in ($Files.Keys | Sort-Object)) { $ordered[$key] = $Files[$key] }
    $document = [ordered]@{
        note  = 'Written by setup.ps1: the harness payload as last installed. update.ps1 uses it to tell your changes from harness changes.'
        files = $ordered
    }
    $json = $document | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

function Copy-Payload([string]$HarnessRoot, [string]$ProjectRoot) {
    # Copies the payload. Returns the list of conflicts (files changed locally that the harness also changed).
    $manifestPath = Join-Path $ProjectRoot '.harness\manifest.json'
    $previous = Read-Manifest $manifestPath
    $next = @{}
    $conflicts = New-Object System.Collections.Generic.List[string]
    $copied = 0

    $payload = Get-PayloadFiles $HarnessRoot
    foreach ($relative in $payload.Keys) {
        $source = $payload[$relative]
        $destination = Join-Path $ProjectRoot $relative
        $sourceHash = Get-NormalizedHash $source

        if (-not (Test-Path -LiteralPath $destination)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
            $next[$relative] = $sourceHash
            $copied++
            continue
        }

        $currentHash = Get-NormalizedHash $destination
        if ($currentHash -eq $sourceHash) {
            $next[$relative] = $sourceHash
            continue
        }

        if ($previous.ContainsKey($relative) -and $previous[$relative] -eq $currentHash) {
            # Unchanged since the last install: take the harness version.
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $next[$relative] = $sourceHash
            $copied++
            continue
        }

        # Changed locally (or never installed by the harness): keep it, offer the new version beside it.
        Copy-Item -LiteralPath $source -Destination ($destination + '.harness-new') -Force
        if ($previous.ContainsKey($relative)) { $next[$relative] = $previous[$relative] }
        $conflicts.Add($relative)
    }

    # Files the harness no longer ships: remove them unless they were changed locally.
    foreach ($relative in $previous.Keys) {
        if ($payload.Contains($relative)) { continue }
        $destination = Join-Path $ProjectRoot $relative
        if (-not (Test-Path -LiteralPath $destination)) { continue }
        if ((Get-NormalizedHash $destination) -eq $previous[$relative]) {
            Remove-Item -LiteralPath $destination
        }
        else {
            Write-Warning "$relative is no longer part of the harness but has local changes; it was kept."
        }
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $manifestPath) | Out-Null
    Write-Manifest $manifestPath $next
    Write-Host "Payload: $($payload.Count) files, $copied copied or updated, $($conflicts.Count) conflicts."
    return , $conflicts
}

function Assert-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw "The .NET SDK was not found. Install .NET $MinimumDotnetMajor or later (winget install Microsoft.DotNet.SDK.$MinimumDotnetMajor)."
    }
    # Read all output: stopping the pipeline early leaves $LASTEXITCODE unset on PowerShell 7.
    $output = @(& dotnet --version 2>$null)
    $exit = $LASTEXITCODE
    $version = ''
    if ($output.Count -gt 0) { $version = [string]$output[0] }
    $major = 0
    if ($version -match '^(\d+)\.') { $major = [int]$Matches[1] }
    if ($exit -ne 0 -or $major -lt $MinimumDotnetMajor) {
        throw ".NET SDK $MinimumDotnetMajor or later is required; found '$version'."
    }
}

function Test-Interactive {
    if ([Environment]::GetCommandLineArgs() -contains '-NonInteractive') { return $false }
    try { return -not [Console]::IsInputRedirected } catch { return $false }
}

# --- where are we? ---

$scriptFolder = $PSScriptRoot
$fromProject = (Split-Path -Leaf $scriptFolder) -eq '.harness'

if ($fromProject) {
    $projectRoot = Split-Path -Parent $scriptFolder
    if ($Target -and ((Resolve-Path -LiteralPath $Target).Path.TrimEnd('\') -ne $projectRoot.TrimEnd('\'))) {
        throw "This copy of setup.ps1 belongs to $projectRoot. To install into another project, run scripts/setup.ps1 from the harness repository."
    }
    $harnessRoot = $null
}
else {
    $harnessRoot = Split-Path -Parent $scriptFolder
    if (-not $Target) { $Target = (Get-Location).Path }
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    $projectRoot = (Resolve-Path -LiteralPath $Target).Path
    if ($projectRoot.TrimEnd('\') -eq $harnessRoot.TrimEnd('\')) {
        throw 'Refusing to install the harness into its own repository. Pass -Target <project folder>.'
    }
}

$projectJson = Join-Path $projectRoot '.harness\project.json'
if (-not $Framework -and -not (Test-Path -LiteralPath $projectJson)) {
    if (-not (Test-Interactive)) {
        throw 'This is a first install: pass -Framework nunit, xunit or mstest.'
    }
    $chosen = ''
    while ($chosen -notin @('nunit', 'xunit', 'mstest')) {
        $chosen = ([string](Read-Host 'Test framework (nunit, xunit, mstest)')).Trim().ToLowerInvariant()
    }
    $Framework = $chosen
}

Assert-Dotnet

# --- copy, build, install ---

$conflicts = @()
if ($harnessRoot) {
    Write-Host "Installing the harness from $harnessRoot into $projectRoot"
    $conflicts = Copy-Payload $harnessRoot $projectRoot
}

$toolProject = Join-Path $projectRoot '.harness\tools\Harness.Tools\Harness.Tools.csproj'
$toolOutput = Join-Path $projectRoot '.harness\tools\bin'
if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "The tool source is missing ($toolProject). Run scripts/setup.ps1 from the harness repository."
}

Write-Host 'Building Harness.Tools...'
# Windows PowerShell 5.1 turns redirected native stderr into errors, so relax the preference here.
$ErrorActionPreference = 'Continue'
$buildLog = & dotnet publish $toolProject -c Release -o $toolOutput --nologo -v quiet 2>&1
$buildExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($buildExit -ne 0) {
    $buildLog | Write-Host
    throw 'Building Harness.Tools failed; the hooks will not run until it builds.'
}

$installArgs = @(
    (Join-Path $toolOutput 'Harness.Tools.dll'), 'install',
    '--root', $projectRoot,
    '--settings-template', (Join-Path $projectRoot '.harness\templates\settings.harness.json')
)
if ($Framework) { $installArgs += @('--framework', $Framework) }
if ($Name) { $installArgs += @('--name', $Name) }
if ($TargetFramework) { $installArgs += @('--target-framework', $TargetFramework) }

& dotnet @installArgs
if ($LASTEXITCODE -ne 0) {
    throw 'Harness.Tools install failed; see the message above.'
}

if ($conflicts.Count -gt 0) {
    Write-Warning 'These files have local changes, so the new harness version was written next to them as <file>.harness-new:'
    foreach ($file in $conflicts) { Write-Warning "  $file" }
    Write-Warning 'Merge what you need, then delete the .harness-new files. Re-running keeps your versions.'
}

Write-Host ''
Write-Host 'Harness ready. Restart Claude Code in this project so it loads the hooks.'
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'src')) -and -not (Test-Path -LiteralPath (Join-Path $projectRoot 'tests'))) {
    Write-Host 'To create the walking skeleton, ask Claude to scaffold the framework, or run:'
    Write-Host "  dotnet `"$toolOutput\Harness.Tools.dll`" scaffold --framework <framework> --name <Name> --output `"$projectRoot`""
}
