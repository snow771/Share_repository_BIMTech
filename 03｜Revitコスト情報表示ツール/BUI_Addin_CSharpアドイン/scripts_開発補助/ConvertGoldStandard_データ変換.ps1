# このスクリプトの目的:
#   GoldStandardのxlsxを、BUIが読むCSV一式（1シート＝1CSV）へ書き出します。
#
# なぜExcelを使わないか:
#   COM経由はExcelの有無と設定に左右され、会社PCで動かない可能性があります。
#   xlsxはZIPなので、中のXMLを直接読めば環境に依存しません。
#
# 修正時の影響:
#   出力はUTF-8 BOM付きです。BOMを外すとBUIがCSVを読めません。
#   このファイル自体もUTF-8 BOM付きで保存してください。BOM無しは日本語が壊れます。

param(
    # 変換元のxlsx。未指定時は下の本体で解決します（既定はリポジトリのルートにあるもの）。
    [string]$SourceXlsx = $null,
    # 出力先。PoC用の複製フォルダを既定にします。元データのフォルダを指定しないでください。
    [string]$OutputFolder = (Join-Path $env:USERPROFILE "BUI_PoCData")
)

$ErrorActionPreference = "Stop"

# $SourceXlsx が未指定の場合は、ここで解決します。
# 注意: [CmdletBinding()] を付けたまま param のデフォルト値の中で $PSScriptRoot を参照すると、
#       日本語などの非ASCIIパス配下で「powershell -File」実行時に $PSScriptRoot が空文字列に
#       なり、Split-Path が「Cannot bind argument to parameter 'Path' because it is an empty
#       string.」で失敗することがある（Windows PowerShell 5.1 の既知の挙動）。
#       そのため [CmdletBinding()] は付けず、既定値の解決は本体側（$PSScriptRoot が確実に
#       設定されているタイミング）で行う。
if ([string]::IsNullOrEmpty($SourceXlsx)) {
    $SourceXlsx = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "GoldStandard_SQLテーブル再構築_v2.xlsx"
}

if (-not (Test-Path -LiteralPath $SourceXlsx)) {
    Write-Host "[NG] 変換元が見つかりません: $SourceXlsx"
    exit 1
}

Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

# 日付として書き出す列。Excelのシリアル値を yyyy-MM-dd へ直します。
$dateColumns = @("publication_date", "reviewed_date")
$serialEpoch = Get-Date -Year 1899 -Month 12 -Day 30 -Hour 0 -Minute 0 -Second 0

function Convert-ColumnLetterToIndex([string]$reference) {
    $letters = ($reference -replace '[0-9]', '')
    $index = 0
    foreach ($character in $letters.ToCharArray()) {
        $index = $index * 26 + ([int][char]::ToUpper($character) - 64)
    }
    return $index - 1
}

function Convert-ToCsvField([string]$value) {
    if ($null -eq $value) { return '' }
    if ($value -match '[",\r\n]') {
        return '"' + ($value -replace '"', '""') + '"'
    }
    return $value
}

function Convert-SerialToDate([string]$value) {
    $number = 0
    if ([int]::TryParse($value, [ref]$number) -and $number -ge 20000 -and $number -le 60000) {
        return $serialEpoch.AddDays($number).ToString("yyyy-MM-dd")
    }
    return $value
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($SourceXlsx)
try {
    function Read-Entry([string]$name) {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $name }
        if ($null -eq $entry) { return $null }
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    # 共有文字列表。セルの t="s" はこの表への添字です。
    $sharedStrings = @()
    $sharedXml = Read-Entry "xl/sharedStrings.xml"
    if ($null -ne $sharedXml) {
        foreach ($si in $sharedXml.sst.si) {
            $text = ""
            foreach ($t in $si.SelectNodes(".//*[local-name()='t']")) { $text += $t.InnerText }
            $sharedStrings += $text
        }
    }

    $workbook = Read-Entry "xl/workbook.xml"
    $relations = Read-Entry "xl/_rels/workbook.xml.rels"
    $relationMap = @{}
    foreach ($relation in $relations.Relationships.Relationship) { $relationMap[$relation.Id] = $relation.Target }

    if (-not (Test-Path -LiteralPath $OutputFolder)) {
        New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
    }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    $written = 0

    foreach ($sheet in $workbook.workbook.sheets.sheet) {
        $sheetName = $sheet.name
        $relationId = $sheet.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
        $target = $relationMap[$relationId] -replace '^/xl/', '' -replace '^/', ''
        if ($target -notlike "xl/*") { $target = "xl/$target" }

        $sheetXml = Read-Entry $target
        if ($null -eq $sheetXml) {
            Write-Host "[NG] シートを読めません: $sheetName"
            continue
        }

        $rows = @()
        foreach ($row in $sheetXml.SelectNodes("//*[local-name()='row']")) {
            $values = @{}
            $maximum = -1
            foreach ($cell in $row.SelectNodes("*[local-name()='c']")) {
                $index = Convert-ColumnLetterToIndex $cell.GetAttribute("r")
                $type = $cell.GetAttribute("t")
                $node = $cell.SelectSingleNode("*[local-name()='v']")
                $text = ""
                if ($type -eq "inlineStr") {
                    $inline = $cell.SelectSingleNode(".//*[local-name()='t']")
                    if ($null -ne $inline) { $text = $inline.InnerText }
                } elseif ($null -ne $node) {
                    if ($type -eq "s") { $text = $sharedStrings[[int]$node.InnerText] }
                    else { $text = $node.InnerText }
                }
                $values[$index] = $text
                if ($index -gt $maximum) { $maximum = $index }
            }
            $rows += ,@{ Values = $values; Max = $maximum }
        }

        if ($rows.Count -eq 0) {
            # 行が1つも無い（見出しすら無い）本当に空のシートのみ、ここでスキップします。
            # 見出し行だけ存在してデータ行が無いシート（例: Product_Variants, Product_Images）は
            # ここには来ません。$rows.Count は見出し行の分だけ1以上になるので、
            # 下の通常処理に進み、見出し行だけのCSVがきちんと書き出されます。
            Write-Host "[--] 空のシートです: $sheetName"
            continue
        }

        # 列数は見出し行だけでなく、全データ行の最大列も含めて決めます。
        # 見出し行より広いデータ行があるシート（例: Migration_Audit）で、
        # はみ出した列を無言で切り捨てないためです。
        $headerMaxColumn = $rows[0].Max
        $dataMaxColumn = -1
        for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
            if ($rows[$rowIndex].Max -gt $dataMaxColumn) { $dataMaxColumn = $rows[$rowIndex].Max }
        }
        $headerCount = [Math]::Max($headerMaxColumn, $dataMaxColumn) + 1

        $headers = @()
        for ($column = 0; $column -lt $headerCount; $column++) {
            $headers += [string]$rows[0].Values[$column]
        }

        # 見出し行より広い列があった分は、一意で空でないプレースホルダー名を補います。
        # BUI側のCSV読み込みは見出し行必須・重複名禁止・見出しより広い行を禁止するため、
        # ここで補っておかないと、はみ出した列がデータごと無言で消えてしまいます。
        $extraColumnCount = $headerCount - ($headerMaxColumn + 1)
        if ($extraColumnCount -gt 0) {
            $addedNames = @()
            for ($column = $headerMaxColumn + 1; $column -lt $headerCount; $column++) {
                $placeholder = "Column{0}" -f ($column + 1)
                while ($headers -contains $placeholder) { $placeholder = $placeholder + "_" }
                $headers[$column] = $placeholder
                $addedNames += $placeholder
            }
            Write-Host ("[WARN] {0}: 見出し行より広いデータ行があったため、{1} 列分の見出しを補いました（{2}）" -f $sheetName, $extraColumnCount, ($addedNames -join ', '))
        }

        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add((($headers | ForEach-Object { Convert-ToCsvField $_ }) -join ','))

        for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
            $fields = @()
            for ($column = 0; $column -lt $headerCount; $column++) {
                $value = [string]$rows[$rowIndex].Values[$column]
                if ($dateColumns -contains $headers[$column]) { $value = Convert-SerialToDate $value }
                $fields += Convert-ToCsvField $value
            }
            # 全部空の行は書き出しません。空行はBUI側で読み飛ばしの警告になります。
            if (($fields -join '') -ne '') { $lines.Add(($fields -join ',')) }
        }

        $path = Join-Path $OutputFolder ($sheetName + ".csv")
        if (Test-Path -LiteralPath $path) {
            Copy-Item -LiteralPath $path -Destination "$path.$stamp.bak" -Force
        }
        [System.IO.File]::WriteAllLines($path, $lines, $utf8Bom)
        Write-Host ("[OK] {0,-22} {1,5} 行" -f ($sheetName + ".csv"), ($lines.Count - 1))
        $written++
    }

    Write-Host ""
    Write-Host "出力先: $OutputFolder"
    Write-Host "書き出したCSV: $written 枚"
    Write-Host "BUIの設定タブ「データフォルダ」にこのフォルダを指定してください。"
}
finally {
    $archive.Dispose()
}
