<#
.SYNOPSIS
    Renames the Lyrx/.NET 8 WinUI 3 project to Lyrx across the entire repository.

.DESCRIPTION
    Performs:
      1. Clean bin/obj directories (prevents stale artifact conflicts after rename)
      2. In-place text substitution in all source files (cs, xaml, csproj, sln, manifest, ps1)
      3. Rename individual source files whose names contain "Lyrx"
      4. Rename project directories (deepest-first)
      5. Validation pass — confirms zero remaining Lyrx references

    GUARDRAILS:
      - Does NOT touch bin/ or obj/ tree content (cleaned, then ignored)
      - Does NOT add or remove any csproj XML nodes other than those listed
      - Does NOT modify OverlayWindow.xaml structure
      - Preserves all GUIDs in the .sln file

.NOTES
    Copy this script to the repository root, then run:
        .\Rename-ToLyrx.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ─────────────────────────────────────────────────────────────────────────────
# Helper: replace all occurrences in a file (UTF-8, no BOM)
# ─────────────────────────────────────────────────────────────────────────────
function Replace-InFile {
    param(
        [string]$Path,
        [System.Collections.Specialized.OrderedDictionary]$Replacements
    )
    $content = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $original = $content
    foreach ($from in $Replacements.Keys) {
        $content = $content.Replace($from, $Replacements[$from])
    }
    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($Path, $content, [System.Text.Encoding]::UTF8)
        Write-Host "  UPDATED $Path"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 1: Clean bin/ and obj/ directories
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 1: Cleaning bin/ and obj/ directories ===" -ForegroundColor Cyan

$cleanDirs = Get-ChildItem -Path $Root -Recurse -Directory |
    Where-Object {
        ($_.Name -eq 'bin' -or $_.Name -eq 'obj') -and
        $_.FullName -notlike '*\.git\*'
    }

foreach ($d in $cleanDirs) {
    if (Test-Path $d.FullName) {
        Write-Host "  Removing $($d.FullName)"
        Remove-Item -Recurse -Force $d.FullName -ErrorAction SilentlyContinue
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 2: In-place text replacement in source files
#         Order matters: longer/more-specific strings FIRST to prevent partial
#         double-substitution (e.g. "Lyrx.Core.Tests" before "Lyrx").
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 2: In-place text substitution ===" -ForegroundColor Cyan

$replacements = [System.Collections.Specialized.OrderedDictionary]::new()
# Namespace / assembly tokens — longest first
$replacements['Lyrx.Core.Tests'] = 'Lyrx.Core.Tests'
$replacements['Lyrx.ViewModels'] = 'Lyrx.ViewModels'
$replacements['Lyrx.Core']       = 'Lyrx.Core'
$replacements['Lyrx.App']        = 'Lyrx.App'
$replacements['Lyrx']            = 'Lyrx'
# String literals / URL tokens
$replacements['LyrxTests_']      = 'LyrxTests_'
$replacements['lyrx']            = 'lyrx'
$replacements['lyrx']               = 'lyrx'

$extensions = @('*.cs', '*.xaml', '*.csproj', '*.sln', '*.manifest', '*.ps1', '*.md')

$sourceFiles = Get-ChildItem -Path $Root -Recurse -File -Include $extensions |
    Where-Object {
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -notlike '*\bin\*'  -and
        $_.FullName -notlike '*\obj\*'
    }

foreach ($file in $sourceFiles) {
    Replace-InFile -Path $file.FullName -Replacements $replacements
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 3: Rename individual FILES whose names contain "Lyrx"
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 3: Renaming files ===" -ForegroundColor Cyan

$filesToRename = Get-ChildItem -Path $Root -Recurse -File |
    Where-Object {
        $_.Name -like '*Lyrx*' -and
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -notlike '*\bin\*'  -and
        $_.FullName -notlike '*\obj\*'
    }

foreach ($file in $filesToRename) {
    $newName = $file.Name -replace 'Lyrx', 'Lyrx'
    Write-Host "  RENAME  $($file.Name)  ->  $newName  (in $($file.DirectoryName))"
    Rename-Item -Path $file.FullName -NewName $newName
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 4: Rename DIRECTORIES (deepest first to avoid path-collision)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 4: Renaming directories ===" -ForegroundColor Cyan

$dirsToRename = Get-ChildItem -Path $Root -Recurse -Directory |
    Where-Object {
        $_.Name -like '*Lyrx*' -and
        $_.FullName -notlike '*\.git\*'
    } |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($dir in $dirsToRename) {
    $newName = $dir.Name -replace 'Lyrx', 'Lyrx'
    Write-Host "  RENAME  $($dir.Name)  ->  $newName  (in $($dir.Parent.FullName))"
    Rename-Item -Path $dir.FullName -NewName $newName
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 5: Rename solution file (belt-and-suspenders; may already be done)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 5: Rename solution file ===" -ForegroundColor Cyan

$slnOld = Join-Path $Root 'Lyrx.sln'
if (Test-Path $slnOld) {
    Write-Host "  RENAME  Lyrx.sln  ->  Lyrx.sln"
    Rename-Item -Path $slnOld -NewName 'Lyrx.sln'
} else {
    Write-Host "  Lyrx.sln already in place (handled in Step 3)."
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 6: Validation
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== STEP 6: Validation ===" -ForegroundColor Cyan

$checkFiles = Get-ChildItem -Path $Root -Recurse -File -Include $extensions |
    Where-Object {
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -notlike '*\bin\*'  -and
        $_.FullName -notlike '*\obj\*'  -and
        $_.Name     -notlike 'Rename-ToLyrx.ps1'   # skip this script itself
    }

$remaining = $checkFiles | Select-String -Pattern 'Lyrx' -CaseSensitive -SimpleMatch

if ($remaining) {
    Write-Host "`n  [WARN] Remaining 'Lyrx' references:" -ForegroundColor Yellow
    $remaining | ForEach-Object {
        Write-Host "    $($_.Filename):$($_.LineNumber)  $($_.Line.Trim())"
    }
} else {
    Write-Host "  [OK] No remaining 'Lyrx' references." -ForegroundColor Green
}

$remainingLower = $checkFiles | Select-String -Pattern 'lyrx' -CaseSensitive -SimpleMatch
if ($remainingLower) {
    Write-Host "`n  [WARN] Remaining 'lyrx' references:" -ForegroundColor Yellow
    $remainingLower | ForEach-Object {
        Write-Host "    $($_.Filename):$($_.LineNumber)  $($_.Line.Trim())"
    }
} else {
    Write-Host "  [OK] No remaining 'lyrx' references." -ForegroundColor Green
}

Write-Host "`n=== Rename complete! Next steps ===" -ForegroundColor Green
Write-Host "  dotnet restore Lyrx.sln"
Write-Host "  dotnet build   Lyrx.sln"
Write-Host "  dotnet test    tests/Lyrx.Core.Tests/Lyrx.Core.Tests.csproj"
