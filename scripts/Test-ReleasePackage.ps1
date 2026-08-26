[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\release\win-x64'))
$resolvedPackageRoot = [System.IO.Path]::GetFullPath($PackageRoot)

if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals(
    $resolvedPackageRoot.TrimEnd('\'),
    $allowedRoot.TrimEnd('\'))) {
    throw 'The release package root must be artifacts\release\win-x64.'
}

if (-not (Test-Path -LiteralPath $resolvedPackageRoot -PathType Container)) {
    throw 'The release package directory does not exist.'
}

$requiredFiles = @(
    'DevForge.Desktop.exe'
    'DevForge.Desktop.dll'
    'DevForge.Desktop.deps.json'
    'DevForge.Desktop.runtimeconfig.json'
    'coreclr.dll'
    'hostfxr.dll'
    'docs\README.md'
    'docs\CHANGELOG.md'
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackageRoot $requiredFile) -PathType Leaf)) {
        throw "Required release file is missing: $requiredFile"
    }
}

$expectedBlueprints = @(
    'desktop.csharp-wpf-tool'
    'tool.python-cli'
    'web.react-vite-ts'
)
$blueprintRoot = Join-Path $resolvedPackageRoot 'blueprints\built-in'
$actualBlueprints = @(
    Get-ChildItem -LiteralPath $blueprintRoot -Directory |
        Sort-Object -Property Name |
        ForEach-Object { $_.Name }
)
if ([System.StringComparer]::Ordinal.Compare(
    ($expectedBlueprints -join "`n"),
    ($actualBlueprints -join "`n")) -ne 0) {
    throw 'The release package must contain exactly the three reviewed blueprint roots.'
}

foreach ($blueprint in $expectedBlueprints) {
    foreach ($file in @('manifest.yaml', 'checksums.json', 'README.md')) {
        $path = Join-Path $blueprintRoot (Join-Path $blueprint $file)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required blueprint file is missing: $blueprint\$file"
        }
    }
}

$forbiddenNames = @(
    '.env'
    'id_rsa'
    'id_ed25519'
    'support-bundles'
)
$forbiddenExtensions = @(
    '.db'
    '.sqlite'
    '.sqlite3'
    '.pem'
    '.key'
    '.pfx'
    '.ps1'
    '.cmd'
    '.bat'
    '.sh'
)
$packagePrefix = $resolvedPackageRoot.TrimEnd('\') + '\'
$files = @(
    Get-ChildItem -LiteralPath $resolvedPackageRoot -File -Recurse |
        Where-Object { $_.FullName -ne (Join-Path $resolvedPackageRoot 'release-audit.json') }
)
foreach ($file in $files) {
    if (-not $file.FullName.StartsWith(
        $packagePrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'A release file resolved outside the package root.'
    }

    if (($forbiddenNames -contains $file.Name.ToLowerInvariant()) -or
        ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant())) {
        throw "Forbidden release payload: $($file.Name)"
    }
}

[ordered]@{
    schemaVersion = 1
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    fileCount = $files.Count
    blueprintRoots = $expectedBlueprints
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (
    Join-Path $resolvedPackageRoot 'release-audit.json') -Encoding utf8NoBOM

Write-Host "Release package audit passed: $($files.Count) files, 3 blueprint roots."
