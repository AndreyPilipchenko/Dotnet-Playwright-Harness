<#
.SYNOPSIS
Refreshes the harness in a project from a newer harness repository.

.DESCRIPTION
Copies the current tool source, templates and Claude Code payload into an installed project, rebuilds
the tool and re-merges the hooks. The framework, name and target framework come from the project's
.harness/project.json.

Files you changed locally are kept. When the harness also changed one of them, its new version is
written next to it as <file>.harness-new and listed as a conflict. Your own settings.json entries are
never removed; only the harness hooks are replaced.

Works on Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Target
The installed project. Default: the folder above .harness when run from a project, otherwise the
current directory.

.PARAMETER Source
The harness repository to update from. Default: this script's repository when run from it.

.EXAMPLE
scripts/update.ps1 -Target C:\src\shop-tests

.EXAMPLE
.harness/update.ps1 -Source C:\src\dotnet-playwright-harness
#>
[CmdletBinding()]
param(
    [string]$Target,
    [string]$Source
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptFolder = $PSScriptRoot
$fromProject = (Split-Path -Leaf $scriptFolder) -eq '.harness'

if (-not $Target) {
    if ($fromProject) { $Target = Split-Path -Parent $scriptFolder } else { $Target = (Get-Location).Path }
}
$projectRoot = (Resolve-Path -LiteralPath $Target).Path

if (-not $Source) {
    if ($fromProject) {
        throw 'Pass -Source <harness repository> to say where the new harness version is.'
    }
    $Source = Split-Path -Parent $scriptFolder
}
$harnessRoot = (Resolve-Path -LiteralPath $Source).Path

$setup = Join-Path $harnessRoot 'scripts\setup.ps1'
if (-not (Test-Path -LiteralPath $setup) -or -not (Test-Path -LiteralPath (Join-Path $harnessRoot 'templates'))) {
    throw "$harnessRoot is not a harness repository (scripts/setup.ps1 and templates/ were not found)."
}

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.harness\project.json'))) {
    throw "$projectRoot has no .harness/project.json, so the harness is not installed there. Run scripts/setup.ps1 first."
}

& $setup -Target $projectRoot
