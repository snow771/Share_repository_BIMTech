# このスクリプトの目的:
#   BUIがデータを読めない原因を、Revitを立ち上げずに切り分けます。
#   原因はほぼ「フォルダの指定違い」「文字コード」「列名」のどれかです。
#
# 修正時の影響:
#   このファイル自体もUTF-8 BOM付きで保存してください。
#   BOM無しはWindows PowerShell 5.1がShift_JISとして読み、日本語が壊れます。

[CmdletBinding()]
param(
    [string]$DataFolder = (Join-Path $env:USERPROFILE "BUI_PoCData")
)

$ErrorActionPreference = "Stop"

Write-Host "データフォルダ: $DataFolder"
Write-Host ""

if (-not (Test-Path -LiteralPath $DataFolder)) {
    Write-Host "[NG] フォルダがありません。設定タブの「データフォルダ」を確認してください。"
    Write-Host "     未変換の場合は ConvertGoldStandard_データ変換.ps1 を先に実行してください。"
    exit 1
}

# BUIが使う表。Productsだけが必須で、他は無くても動きます（警告になります）。
$required = @("Products")
$optional = @("Manufacturers", "Material_Categories", "Spec_Master", "Display_Groups",
              "Product_Specs", "Sources", "Reliability_Ranks")

# 表ごとに、これが無いと機能しない列。
$requiredColumns = @{
    "Products"           = @("product_id", "product_name", "manufacturer_id", "material_category_id")
    "Manufacturers"      = @("manufacturer_id", "manufacturer_name")
    "Material_Categories" = @("material_category_id", "category_name_ja")
    "Spec_Master"        = @("spec_key", "display_name_ja", "display_group_id", "display_order", "standard_unit")
    "Display_Groups"     = @("display_group_id", "display_group_name_ja", "display_order")
    "Product_Specs"      = @("product_id", "spec_key", "value_number", "value_string")
    "Sources"            = @("source_id", "source_name", "publication_date")
    "Reliability_Ranks"  = @("reliability_rank_id", "rank_code")
}

$failures = 0

function Test-Table([string]$table, [bool]$isRequired) {
    $path = Join-Path $DataFolder ($table + ".csv")
    if (-not (Test-Path -LiteralPath $path)) {
        if ($isRequired) {
            Write-Host ("[NG] {0,-20} ファイルがありません" -f ($table + ".csv"))
            return 1
        }
        Write-Host ("[--] {0,-20} ファイルがありません（空として扱われます）" -f ($table + ".csv"))
        return 0
    }

    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)

    try {
        $strict = New-Object System.Text.UTF8Encoding($false, $true)
        $offset = if ($hasBom) { 3 } else { 0 }
        $text = $strict.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    catch {
        Write-Host ("[NG] {0,-20} UTF-8として読めません。Excelで「CSV UTF-8」を選んで保存し直してください" -f ($table + ".csv"))
        return 1
    }

    $lines = $text -split "`r?`n" | Where-Object { $_ -ne "" }
    if ($lines.Count -eq 0) {
        Write-Host ("[NG] {0,-20} 中身が空です" -f ($table + ".csv"))
        return 1
    }

    $headers = $lines[0] -split ","
    $headers = $headers | ForEach-Object { $_.Trim('"').Trim() }
    $missing = @()
    foreach ($column in $requiredColumns[$table]) {
        if ($headers -notcontains $column) { $missing += $column }
    }

    if ($missing.Count -gt 0) {
        Write-Host ("[NG] {0,-20} 列が足りません: {1}" -f ($table + ".csv"), ($missing -join ", "))
        Write-Host ("     実際の列名: {0}" -f ($headers -join ", "))
        return 1
    }

    $bomLabel = if ($hasBom) { "BOM付き" } else { "BOM無し" }
    Write-Host ("[OK] {0,-20} {1,5} 行  UTF-8 {2}" -f ($table + ".csv"), ($lines.Count - 1), $bomLabel)
    return 0
}

foreach ($table in $required) { $failures += Test-Table $table $true }
foreach ($table in $optional) { $failures += Test-Table $table $false }

Write-Host ""
if ($failures -eq 0) {
    Write-Host "すべて [OK] です。この状態でRevitを起動してください。"
    exit 0
}

Write-Host "[NG] が $failures 件あります。上の指示に従って直してから、もう一度実行してください。"
exit 1
