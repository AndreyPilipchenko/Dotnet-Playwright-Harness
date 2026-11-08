<#
.SYNOPSIS
End-to-end checks for setup.ps1 and update.ps1. Run on both Windows PowerShell 5.1 and PowerShell 7:

  powershell -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1
  pwsh -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1

Works in a temporary folder and never touches this repository. Exits 1 on the first failed check.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('harness-setup-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$shell = (Get-Process -Id $PID).Path
$failures = 0

function Check([string]$What, [bool]$Condition) {
    if ($Condition) { Write-Host "  ok    $What" }
    else { Write-Host "  FAIL  $What" -ForegroundColor Red; $script:failures++ }
}

function Invoke-Script([string]$Path, [string[]]$Arguments) {
    # Runs a script in a child process of the same PowerShell edition; returns exit code and output.
    $ErrorActionPreference = 'Continue'
    $output = & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Path @Arguments 2>&1 | Out-String
    return @{ Code = $LASTEXITCODE; Output = $output }
}

function Read-Json([string]$Path) { return [IO.File]::ReadAllText($Path) | ConvertFrom-Json }

function Get-HarnessHandlerCount([string]$SettingsPath) {
    $text = [IO.File]::ReadAllText($SettingsPath)
    return ([regex]::Matches($text, 'Harness\.Tools\.dll')).Count
}

try {
    Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition)); work folder $work"

    # A copy of the harness, so update.ps1 can be tested against a "newer" harness.
    $harness = Join-Path $work 'harness'
    New-Item -ItemType Directory -Path $harness | Out-Null
    foreach ($item in @('scripts', 'templates', '.claude', 'LICENSE', 'NOTICE.md')) {
        Copy-Item -Recurse -LiteralPath (Join-Path $repo $item) -Destination $harness
    }
    New-Item -ItemType Directory -Path (Join-Path $harness 'tools') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'tools\Directory.Build.props'), (Join-Path $repo 'tools\Directory.Packages.props') -Destination (Join-Path $harness 'tools')
    $toolCopy = Join-Path $harness 'tools\Harness.Tools'
    New-Item -ItemType Directory -Path $toolCopy | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'tools\Harness.Tools') | Where-Object { $_.Name -notin @('bin', 'obj') } |
        Copy-Item -Recurse -Destination $toolCopy

    $project = Join-Path $work 'shop-tests'
    $setup = Join-Path $harness 'scripts\setup.ps1'

    Write-Host 'First install without -Framework, non-interactive'
    $r = Invoke-Script $setup @('-Target', $project)
    Check 'fails' ($r.Code -ne 0)
    Check 'says to pass -Framework' ($r.Output -match '-Framework')

    Write-Host 'Refuses to install into the harness itself'
    $r = Invoke-Script $setup @('-Target', $harness, '-Framework', 'nunit')
    Check 'fails' ($r.Code -ne 0)

    Write-Host 'First install'
    $r = Invoke-Script $setup @('-Target', $project, '-Framework', 'nunit', '-Name', 'Acme.Shop')
    Check "exits 0 $(if ($r.Code -ne 0) { $r.Output })" ($r.Code -eq 0)
    Check 'builds the tool' (Test-Path (Join-Path $project '.harness\tools\bin\Harness.Tools.dll'))
    Check 'copies setup and update' ((Test-Path (Join-Path $project '.harness\setup.ps1')) -and (Test-Path (Join-Path $project '.harness\update.ps1')))
    Check 'copies the templates' (Test-Path (Join-Path $project '.harness\templates\settings.harness.json'))
    Check 'copies the agents' (Test-Path (Join-Path $project '.claude\agents\suite-runner.md'))
    $projectJson = Read-Json (Join-Path $project '.harness\project.json')
    Check 'records the framework' ($projectJson.framework -eq 'nunit' -and $projectJson.name -eq 'Acme.Shop')
    $settings = Join-Path $project '.claude\settings.json'
    Check 'merges 8 harness hooks' ((Get-HarnessHandlerCount $settings) -eq 8)
    $manifest = [IO.File]::ReadAllText((Join-Path $project '.harness\manifest.json'))
    Check 'writes the manifest, without build output' ($manifest -match 'Harness.Tools.csproj' -and $manifest -notmatch '/(bin|obj)/')
    $firstSettings = [IO.File]::ReadAllText($settings)

    Write-Host 'Re-run is a no-op'
    $r = Invoke-Script $setup @('-Target', $project)
    Check 'exits 0' ($r.Code -eq 0)
    Check 'copies nothing' ($r.Output -match ' 0 copied or updated, 0 conflicts')
    Check 'settings unchanged' ([IO.File]::ReadAllText($settings) -eq $firstSettings)

    Write-Host 'Changing the framework is refused'
    $r = Invoke-Script $setup @('-Target', $project, '-Framework', 'xunit')
    Check 'fails' ($r.Code -ne 0)
    Check 'names the recorded framework' ($r.Output -match 'nunit')

    Write-Host 'Update keeps local changes and reports conflicts'
    $localFile = Join-Path $project '.harness\templates\build\dot.editorconfig'
    Add-Content -LiteralPath $localFile -Value '# local change'
    Add-Content -LiteralPath (Join-Path $harness 'templates\build\dot.editorconfig') -Value '# harness change'
    Add-Content -LiteralPath (Join-Path $harness 'templates\build\global.json') -Value ' '
    $userSettings = Read-Json $settings
    $userSettings | Add-Member -NotePropertyName 'model' -NotePropertyValue 'opus'
    [IO.File]::WriteAllText($settings, ($userSettings | ConvertTo-Json -Depth 20))
    $r = Invoke-Script (Join-Path $harness 'scripts\update.ps1') @('-Target', $project)
    Check "exits 0 $(if ($r.Code -ne 0) { $r.Output })" ($r.Code -eq 0)
    Check 'keeps the local version' ([IO.File]::ReadAllText($localFile) -match 'local change')
    Check 'writes .harness-new' ([IO.File]::ReadAllText($localFile + '.harness-new') -match 'harness change')
    Check 'reports the conflict' ($r.Output -match 'dot\.editorconfig')
    $globalJson = [IO.File]::ReadAllText((Join-Path $project '.harness\templates\build\global.json')).Replace("`r", '')
    Check 'takes unchanged files from the harness' ($globalJson -eq [IO.File]::ReadAllText((Join-Path $harness 'templates\build\global.json')).Replace("`r", ''))
    Check 'keeps user settings' ((Read-Json $settings).model -eq 'opus')
    Check 'still 8 harness hooks' ((Get-HarnessHandlerCount $settings) -eq 8)

    Write-Host 'A clone rebuilds from the committed source'
    Remove-Item -Recurse -Force (Join-Path $project '.harness\tools\bin')
    $r = Invoke-Script (Join-Path $project '.harness\setup.ps1') @()
    Check "exits 0 $(if ($r.Code -ne 0) { $r.Output })" ($r.Code -eq 0)
    Check 'rebuilds the tool' (Test-Path (Join-Path $project '.harness\tools\bin\Harness.Tools.dll'))
    Check 'still 8 harness hooks' ((Get-HarnessHandlerCount $settings) -eq 8)

    Write-Host 'update.ps1 from a project needs -Source'
    $r = Invoke-Script (Join-Path $project '.harness\update.ps1') @()
    Check 'fails' ($r.Code -ne 0)
    $r = Invoke-Script (Join-Path $project '.harness\update.ps1') @('-Source', $harness)
    Check "with -Source exits 0 $(if ($r.Code -ne 0) { $r.Output })" ($r.Code -eq 0)
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    Write-Host "$failures checks failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.'
