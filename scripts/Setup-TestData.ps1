# Setup-TestData.ps1
# Test data setup script for Design Aid

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Find project root from script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "=== Design Aid Test Data Setup ===" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"

# data directories
$DataDir = Join-Path $ProjectRoot "data"
$ComponentsDir = Join-Path $DataDir "components"
$ProjectsDir = Join-Path $DataDir "projects"
$SampleProject = Join-Path $ProjectsDir "sample-project"

# Check if sample project exists
if (Test-Path $SampleProject) {
    Write-Host "`nSample project exists. Updating hashes..." -ForegroundColor Yellow
} else {
    Write-Host "`nCreating sample project..." -ForegroundColor Green

    # Create directory structure (new architecture: components are shared)
    $Dirs = @(
        "$SampleProject\assets\lifting-unit",
        "$ComponentsDir\SP-TEST-001"
    )
    foreach ($Dir in $Dirs) {
        New-Item -ItemType Directory -Path $Dir -Force | Out-Null
    }

    # .da-project
    $DaProjectContent = @"
{
  "project_id": "$([guid]::NewGuid().ToString())",
  "name": "sample-project",
  "registered_at": "$((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))"
}
"@
    [System.IO.File]::WriteAllText("$SampleProject\.da-project", $DaProjectContent, [System.Text.UTF8Encoding]::new($false))

    # asset.json
    $AssetContent = @"
{
  "id": "$([guid]::NewGuid().ToString())",
  "name": "lifting-unit",
  "display_name": "Lifting Unit",
  "description": "Main lifting mechanism",
  "created_at": "$((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))"
}
"@
    $AssetPath = "$SampleProject\assets\lifting-unit"
    [System.IO.File]::WriteAllText("$AssetPath\asset.json", $AssetContent, [System.Text.UTF8Encoding]::new($false))

    # drawing.txt (sample drawing) - now in shared components directory
    $DrawingContent = @"
Sample Drawing Content
This is a placeholder for a DXF/DWG file.
Part: SP-TEST-001
Material: SS400
Surface: Plating
"@
    $PartDir = "$ComponentsDir\SP-TEST-001"
    [System.IO.File]::WriteAllText("$PartDir\drawing.txt", $DrawingContent, [System.Text.UTF8Encoding]::new($false))
}

# Compute hash for component (now in shared location)
$DrawingFile = "$ComponentsDir\SP-TEST-001\drawing.txt"
if (Test-Path $DrawingFile) {
    $Hash = Get-FileHash -Path $DrawingFile -Algorithm SHA256
    $HashValue = "sha256:" + $Hash.Hash.ToLower()
    Write-Host "drawing.txt hash: $HashValue" -ForegroundColor Gray

    # Update part.json (no asset_id - relationship is managed via DB junction table)
    $PartJsonContent = @"
{
  "id": "770e8400-e29b-41d4-a716-446655440002",
  "part_number": "SP-TEST-001",
  "name": "Test Plate",
  "type": "Fabricated",
  "version": "1.0.0",
  "artifacts": [
    {
      "path": "drawing.txt",
      "hash": "$HashValue"
    }
  ],
  "standards": ["STD-MATERIAL-01"],
  "metadata": {
    "material": "SS400",
    "surface_treatment": "Plating"
  },
  "memo": "Sample test part"
}
"@

    $PartJsonPath = "$ComponentsDir\SP-TEST-001\part.json"
    [System.IO.File]::WriteAllText($PartJsonPath, $PartJsonContent, [System.Text.UTF8Encoding]::new($false))
    Write-Host "part.json updated" -ForegroundColor Green
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host "Sample project: $SampleProject"
Write-Host "Shared components: $ComponentsDir"
Write-Host ""
Write-Host "Test commands:" -ForegroundColor Yellow
Write-Host "  dotnet test                                    # All tests"
Write-Host "  dotnet test --filter Category=Integration      # Integration tests only"
Write-Host ""
