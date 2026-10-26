@echo off
setlocal
set "FIGFORGE_PACK_SCRIPT=%~f0"
powershell.exe -NoLogo -NoProfile -Command "$s = [IO.File]::ReadAllText($env:FIGFORGE_PACK_SCRIPT); & ([scriptblock]::Create(($s -split '(?m)^# POWERSHELL_PAYLOAD\r?$', 2)[1]))"
set "PACK_EXIT=%ERRORLEVEL%"
if not "%~1"=="--no-pause" pause
exit /b %PACK_EXIT%
# POWERSHELL_PAYLOAD
$ErrorActionPreference = 'Stop'
$temporaryFiles = @()
try {
    $root = Split-Path -Parent $env:FIGFORGE_PACK_SCRIPT
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    function Require-File([string] $path) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required file missing: $path"
        }
        if ((Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Required file is empty: $path"
        }
    }

    function Read-Version([string] $path) {
        Require-File $path
        $version = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).version
        if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
            throw "Invalid package version in $path"
        }
        return $version
    }

    function Write-Archive([string] $path, [string] $source, [string] $prefix, $files) {
        $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $file.FullName, "$prefix/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $root 'LICENSE'), "$prefix/LICENSE", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        } finally { $zip.Dispose() }
    }

    $plugin = Join-Path $root 'plugin'
    $unity = Join-Path $root 'unity'
    $pluginVersion = Read-Version (Join-Path $plugin 'package.json')
    $unityVersion = Read-Version (Join-Path $unity 'package.json')
    Require-File (Join-Path $plugin 'package-lock.json')
    $node = (Get-Command node.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $npm = (Get-Command npm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $nodeVersion = & $node --version
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read Node.js version.' }
    if ([version]($nodeVersion.Trim() -replace '^v', '') -lt [version]'22.6.0') {
        throw "Node.js >= 22.6.0 is required; found $nodeVersion"
    }
    Write-Host "Using Node.js $nodeVersion ($node)"
    Push-Location -LiteralPath $plugin
    try {
        Write-Host 'Installing locked Figma plugin dependencies...'
        & $npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit $LASTEXITCODE). Packaging stopped." }

        # Only remove this project's generated output; reject linked directories.
        $buildOutput = [IO.Path]::GetFullPath((Join-Path $plugin 'dist'))
        if ([IO.Path]::GetDirectoryName($buildOutput) -ne [IO.Path]::GetFullPath($plugin)) {
            throw "Unsafe build output path: $buildOutput"
        }
        foreach ($path in @($root, $plugin, $buildOutput)) {
            if ((Test-Path -LiteralPath $path) -and
                ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Refusing to clean output through a linked path: $path"
            }
        }
        if (Test-Path -LiteralPath $buildOutput) {
            $links = @(Get-ChildItem -LiteralPath $buildOutput -Force -Recurse |
                Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
            if ($links.Count) { throw "Linked entries found inside $buildOutput; cleanup stopped." }
            Remove-Item -LiteralPath $buildOutput -Recurse -Force
        }
        Write-Host 'Building the latest Figma plugin...'
        & $npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit $LASTEXITCODE). Packaging stopped." }
    } finally { Pop-Location }
    Require-File (Join-Path $root 'LICENSE')
    Require-File (Join-Path $plugin 'manifest.json')
    $manifest = Get-Content -LiteralPath (Join-Path $plugin 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in @($manifest.main, $manifest.ui)) {
        if ($entry -isnot [string] -or $entry -notmatch '^dist/[^:]+$' -or $entry -match '(^|[/\\])\.\.([/\\]|$)') {
            throw 'Expected manifest main and ui to reference files inside dist/.'
        }
        Require-File (Join-Path $plugin $entry)
    }
    $pluginFiles = @((Get-Item -LiteralPath (Join-Path $plugin 'manifest.json')))
    $pluginFiles += @(Get-ChildItem -LiteralPath (Join-Path $plugin 'dist') -File -Recurse)

    # Match the release package: omit development Tests and preserve asset metadata.
    $unityFiles = @()
    foreach ($directory in @('Editor', 'Runtime')) {
        $path = Join-Path $unity $directory
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Missing package directory: $path" }
        $unityFiles += @(Get-ChildItem -LiteralPath $path -File -Recurse)
        $meta = Join-Path $unity "$directory.meta"
        Require-File $meta
        $unityFiles += Get-Item -LiteralPath $meta
    }
    foreach ($name in @('package.json', 'package.json.meta', 'README.md', 'README.md.meta')) {
        $path = Join-Path $unity $name
        Require-File $path
        $unityFiles += Get-Item -LiteralPath $path
    }

    # Validate both inputs before producing either archive.
    $output = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $pluginZip = Join-Path $output "figforge-plugin-$pluginVersion.zip"
    $unityZip = Join-Path $output "figforge-unity-importer-$unityVersion.zip"
    $temporaryFiles = @((Join-Path $output "$([guid]::NewGuid()).tmp"), (Join-Path $output "$([guid]::NewGuid()).tmp"))
    Write-Archive $temporaryFiles[0] $plugin 'figforge-plugin' $pluginFiles
    Write-Archive $temporaryFiles[1] $unity 'figforge-unity-importer' $unityFiles
    Move-Item -LiteralPath $temporaryFiles[0] -Destination $pluginZip -Force
    Move-Item -LiteralPath $temporaryFiles[1] -Destination $unityZip -Force
    Write-Host "Created: $pluginZip"
    Write-Host "Created: $unityZip"
    Write-Host 'Figma: unzip, then import figforge-plugin/manifest.json as a development plugin.'
    Write-Host 'Unity: unzip, then Add package from disk using figforge-unity-importer/package.json.'
    exit 0
} catch {
    Write-Host "Packaging failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    foreach ($path in $temporaryFiles) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
}
