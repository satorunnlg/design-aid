# Run-Tests.ps1
# Test runner script for Design Aid

param(
    [string]$Filter = "",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Project root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "=== Design Aid Test Runner ===" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"
Write-Host ""

# Build
Write-Host "[1/3] Building..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    $buildOutput = dotnet build --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed:" -ForegroundColor Red
        Write-Host $buildOutput
        exit 1
    }
    Write-Host "Build succeeded" -ForegroundColor Green
}
finally {
    Pop-Location
}

# Test data setup
Write-Host ""
Write-Host "[2/3] Setting up test data..." -ForegroundColor Yellow
$SetupScript = Join-Path $ScriptDir "Setup-TestData.ps1"
if (Test-Path $SetupScript) {
    & $SetupScript
}

# Run tests
Write-Host ""
Write-Host "[3/3] Running tests..." -ForegroundColor Yellow

$testArgs = @("test", "--nologo")

if ($Filter) {
    if ($Filter -eq "Integration") {
        $testArgs += "--filter"
        $testArgs += "Category=Integration"
    }
    elseif ($Filter -eq "Unit") {
        $testArgs += "--filter"
        $testArgs += "Category!=Integration&Category!=E2E"
    }
    else {
        $testArgs += "--filter"
        $testArgs += $Filter
    }
}

if ($Verbose) {
    $testArgs += "--logger"
    $testArgs += "console;verbosity=detailed"
}

Write-Host "Executing: dotnet $($testArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

Push-Location $ProjectRoot
try {
    & dotnet @testArgs
    $testExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

Write-Host ""
if ($testExitCode -eq 0) {
    Write-Host "=== All tests passed ===" -ForegroundColor Green
}
else {
    Write-Host "=== Some tests failed ===" -ForegroundColor Red
}

exit $testExitCode
