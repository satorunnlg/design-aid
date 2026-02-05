# Setup-TestData.ps1
# ユニットテスト用サンプルデータセットアップスクリプト
#
# 現在のアーキテクチャ（Project 概念なし）に対応。
# assets/ と components/ に直接データを配置する。

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# スクリプトの場所からプロジェクトルートを取得
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "=== Design Aid Test Data Setup ===" -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"

# data ディレクトリ
$DataDir = Join-Path $ProjectRoot "data"
$AssetsDir = Join-Path $DataDir "assets"
$ComponentsDir = Join-Path $DataDir "components"

# サンプルデータのパス
$SampleAsset = Join-Path $AssetsDir "sample-asset"
$SamplePart = Join-Path $ComponentsDir "SP-TEST-001"

# data ディレクトリが存在しない場合は作成
if (-not (Test-Path $DataDir)) {
    Write-Host "`nCreating data directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ComponentsDir -Force | Out-Null
}

# サンプル装置が存在するかチェック
if (Test-Path $SampleAsset) {
    Write-Host "`nSample asset exists. Updating..." -ForegroundColor Yellow
} else {
    Write-Host "`nCreating sample asset..." -ForegroundColor Green
    New-Item -ItemType Directory -Path $SampleAsset -Force | Out-Null
}

# asset.json
$AssetId = [guid]::NewGuid().ToString()
$AssetContent = @"
{
  "id": "$AssetId",
  "name": "sample-asset",
  "display_name": "Sample Asset",
  "description": "Test data for unit tests",
  "created_at": "$((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))"
}
"@
$AssetJsonPath = Join-Path $SampleAsset "asset.json"
[System.IO.File]::WriteAllText($AssetJsonPath, $AssetContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "  asset.json created" -ForegroundColor Gray

# サンプルパーツが存在するかチェック
if (Test-Path $SamplePart) {
    Write-Host "`nSample part exists. Updating hashes..." -ForegroundColor Yellow
} else {
    Write-Host "`nCreating sample part..." -ForegroundColor Green
    New-Item -ItemType Directory -Path $SamplePart -Force | Out-Null
}

# drawing.txt（サンプル成果物）
$DrawingContent = @"
Sample Drawing Content
This is a placeholder for a DXF/DWG file.
Part: SP-TEST-001
Material: SS400
Surface: Plating
"@
$DrawingPath = Join-Path $SamplePart "drawing.txt"
[System.IO.File]::WriteAllText($DrawingPath, $DrawingContent, [System.Text.UTF8Encoding]::new($false))

# ハッシュ計算
$Hash = Get-FileHash -Path $DrawingPath -Algorithm SHA256
$HashValue = "sha256:" + $Hash.Hash.ToLower()
Write-Host "  drawing.txt hash: $HashValue" -ForegroundColor Gray

# part.json
$PartId = [guid]::NewGuid().ToString()
$PartJsonContent = @"
{
  "id": "$PartId",
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
$PartJsonPath = Join-Path $SamplePart "part.json"
[System.IO.File]::WriteAllText($PartJsonPath, $PartJsonContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "  part.json created" -ForegroundColor Green

# asset_links.json（パーツとの紐付け）
$AssetLinksContent = @"
{
  "parts": [
    {
      "part_number": "SP-TEST-001",
      "quantity": 1,
      "notes": "Test linkage"
    }
  ],
  "sub_assets": []
}
"@
$AssetLinksPath = Join-Path $SampleAsset "asset_links.json"
[System.IO.File]::WriteAllText($AssetLinksPath, $AssetLinksContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "  asset_links.json created" -ForegroundColor Green

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host "Sample data:"
Write-Host "  Asset: $SampleAsset"
Write-Host "  Part:  $SamplePart"
Write-Host ""
Write-Host "Test commands:" -ForegroundColor Yellow
Write-Host "  dotnet test                                    # All tests"
Write-Host "  dotnet test --filter Category=Integration      # Integration tests only"
Write-Host ""
