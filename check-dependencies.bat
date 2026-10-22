@echo off
setlocal
set "FIGFORGE_DEPENDENCY_SCRIPT=%~f0"
powershell.exe -NoLogo -NoProfile -Command "$s = [IO.File]::ReadAllText($env:FIGFORGE_DEPENDENCY_SCRIPT); & ([scriptblock]::Create(($s -split '(?m)^# POWERSHELL_PAYLOAD\r?$', 2)[1]))"
set "CHECK_EXIT=%ERRORLEVEL%"
if not "%~1"=="--no-pause" pause
exit /b %CHECK_EXIT%
# POWERSHELL_PAYLOAD
$ErrorActionPreference = 'Stop'

function Get-Tool([string] $name) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
}

function Test-Node([string] $path) {
    if (-not $path) { return $false }
    $versionText = & $path --version
    if ($LASTEXITCODE -ne 0) { return $false }
    try { return [version]($versionText.Trim() -replace '^v', '') -ge [version]'22.6.0' }
    catch { return $false }
}

try {
    $root = Split-Path -Parent $env:FIGFORGE_DEPENDENCY_SCRIPT
    $plugin = Join-Path $root 'plugin'
    foreach ($name in @('package.json', 'package-lock.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $plugin $name) -PathType Leaf)) {
            throw "Missing plugin/$name"
        }
    }

    $node = Get-Tool 'node.exe'
    $npm = Get-Tool 'npm.cmd'
    if (-not (Test-Node $node) -or -not $npm) {
        $winget = Get-Tool 'winget.exe'
        if (-not $winget) {
            throw 'Node.js >= 22.6.0 and npm are required. Install Windows App Installer (winget), then rerun this script, or install Node.js LTS with npm manually.'
        }
        Write-Host 'Installing/updating Node.js LTS with npm...'
        & $winget install --id OpenJS.NodeJS.LTS --exact --source winget --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) { throw "Node.js installation failed (exit $LASTEXITCODE)." }

        # Refresh this process after the installer updates the persistent PATH.
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
            [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
        $node = Get-Tool 'node.exe'
        $npm = Get-Tool 'npm.cmd'
        if (-not (Test-Node $node) -or -not $npm) {
            throw 'Node.js/npm are still unavailable or outdated. Open a new terminal and rerun this script.'
        }
    }
    Write-Host "Using Node.js: $node"
    & $npm --version
    if ($LASTEXITCODE -ne 0) { throw 'npm is unavailable.' }

    Push-Location -LiteralPath $plugin
    try {
        Write-Host 'Checking plugin dependencies (including development dependencies)...'
        & $npm ls --all --include=dev --include=optional --include=peer
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath 'node_modules' -PathType Container)) {
            Write-Host 'Installing dependencies from package-lock.json...'
            & $npm ci --include=dev --include=optional --include=peer --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit $LASTEXITCODE)." }
            & $npm ls --all --include=dev --include=optional --include=peer
            if ($LASTEXITCODE -ne 0) { throw 'Dependency verification failed after installation.' }
        }
    } finally { Pop-Location }

    Write-Host 'Dependencies are ready. No build was run.'
    Write-Host 'Unity 2022.3+: add unity/package.json through Package Manager; Unity resolves its declared dependencies.'
    exit 0
} catch {
    Write-Host "Dependency check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
