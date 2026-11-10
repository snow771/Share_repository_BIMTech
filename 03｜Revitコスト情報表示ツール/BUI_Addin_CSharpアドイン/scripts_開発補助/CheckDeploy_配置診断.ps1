# 【重要】このファイルはUTF-8 BOM付きで保存してください。
# Windows PowerShell 5.1 はBOMの無い.ps1をShift_JISとして読むため、
# BOMを外すと日本語の文字列が壊れて「トークンを使用できません」の構文エラーになります。

# このスクリプトの目的:
#   「ビルドしたのに画面が変わらない」ときに、Revitが実際にどのDLLを
#   読み込んでいるかを確定させます。推測をやめ、事実だけを表示します。
#
# なぜ必要か:
#   BUIは次の3つが揃って初めて反映されます。どれか1つでも欠けると、
#   ビルドは成功しているのに画面は古いまま、という状態になります。
#     1. ビルドで bin\Debug に新しい Bui.dll が作られる
#     2. その Bui.dll が %APPDATA%\...\Addins\2025\BUI\ へコピーされる
#     3. BUI.addin がそのコピーを指している
#   このスクリプトは3つすべてを個別に確認します。
#
# 使い方:
#   .\scripts_開発補助\CheckDeploy_配置診断.ps1
#
# 修正時の影響:
#   新機能の有無を判定するために、DLLへ埋め込まれる文字列を探しています。
#   $markers を変える場合は、その文字列が本当にDLLへ入る名前か確認してください。

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# DLLに含まれていれば「その機能が入ったビルド」と判断できる目印です。
# 型名やプロパティ名はDLLのメタデータに残るため、確実に検出できます。
$markers = @(
    @{ Name = '設定タブの状態表示';         Text = 'SettingsStatusMessage' },
    @{ Name = 'データフォルダ参照ボタン';   Text = 'BrowseDataFolderCommand' },
    @{ Name = 'ファイル選択画面';           Text = 'WpfFilePickerService' },
    @{ Name = 'CSVパスの引用符除去';        Text = 'CsvPathNormalizer' },
    @{ Name = '不正行の読み飛ばし';         Text = 'CsvRowSkipReport' },
    @{ Name = 'GoldStandard製品リポジトリ'; Text = 'GoldStandardProductRepository' },
    @{ Name = 'GoldStandard製品詳細取得';   Text = 'GoldStandardDetailSource' },
    @{ Name = '製品カードのタブ組立';       Text = 'ProductCardTabBuilder' },
    @{ Name = '製品画像の有無判定';         Text = 'HasProductImage' }
)

function Test-BuiMarker {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    # .NETのメタデータ文字列はUTF-8、XAML由来はUTF-16で入るため両方を探します。
    $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
    $utf16 = [Text.Encoding]::Unicode.GetString($bytes)

    foreach ($marker in $markers) {
        $found = $utf8.Contains($marker.Text) -or $utf16.Contains($marker.Text)
        if ($found) {
            Write-Host "     [OK] $($marker.Name)" -ForegroundColor Green
        }
        else {
            Write-Host "     [NG] $($marker.Name) が入っていません" -ForegroundColor Red
        }
    }
}

function Show-BuiDll {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Path)

    Write-Host ''
    Write-Host "===== $Label =====" -ForegroundColor Cyan
    Write-Host "  場所: $Path"

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Host '  [NG] ファイルがありません' -ForegroundColor Red
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    Write-Host "  更新日時: $($item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "  サイズ: $($item.Length) バイト"
    Write-Host '  入っている機能:'
    Test-BuiMarker -Path $Path
    return $item
}

Write-Host ''
Write-Host 'BUI 配置診断' -ForegroundColor Yellow
Write-Host "現在時刻: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"

# --- 1. ビルド出力 ---
$builtDll = Join-Path $RepositoryRoot "src_ソース\Bui.Addin\bin\$Configuration\net10.0-windows\Bui.dll"
$built = Show-BuiDll -Label '1. ビルド出力（Visual Studioが作るDLL）' -Path $builtDll

# --- 2. 配置先 ---
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025'
$deployedDll = Join-Path $addinsRoot 'BUI\Bui.dll'
$deployed = Show-BuiDll -Label '2. 配置先（Revitが読み込むDLL）' -Path $deployedDll

# --- 3. マニフェスト ---
$manifest = Join-Path $addinsRoot 'BUI.addin'
Write-Host ''
Write-Host '===== 3. BUI.addin（Revitへの案内状） =====' -ForegroundColor Cyan
Write-Host "  場所: $manifest"

if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    Write-Host '  [NG] ファイルがありません。DeployDev_開発用配置.ps1 を実行してください。' -ForegroundColor Red
}
else {
    $assembly = ([xml](Get-Content -LiteralPath $manifest -Raw)).RevitAddIns.AddIn.Assembly
    Write-Host "  指している先: $assembly"
    if ([string]::Equals($assembly, $deployedDll, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host '  [OK] 配置先のDLLを指しています' -ForegroundColor Green
    }
    else {
        Write-Host '  [NG] 配置先と一致しません' -ForegroundColor Red
    }
}

function Test-BuiSqlClientFacade {
    # 出力直下の Microsoft.Data.SqlClient.dll には2種類あります。
    #   facade（約900KB）… 読み込むと「is not supported on this platform.」で失敗する
    #   実体  （約2.1MB）… Windows用。runtimes\win\lib\net8.0 に入っている
    # 「ファイルがあるか」だけを見て[OK]と言うと、facadeが置かれていても
    # 診断は通り、Revitで接続を試みた瞬間に初めて失敗します（実際にそうなりました）。
    # 中身の文字列で見分けます。
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    return $text.Contains('Microsoft.Data.SqlClient is not supported on this platform')
}

# --- 4. リボンのアイコン ---
Write-Host ''
Write-Host '===== 4. リボンのアイコン =====' -ForegroundColor Cyan

$iconNames = @(
    'リボン_01_パネル表示.ico',
    'リボン_02_パラメータ作成.ico',
    'リボン_03_AI.ico'
)
$missingIcons = @()
foreach ($iconName in $iconNames) {
    $iconPath = Join-Path $addinsRoot (Join-Path 'BUI\Resources_画像と定義' $iconName)
    if (Test-Path -LiteralPath $iconPath) {
        $iconSize = (Get-Item -LiteralPath $iconPath).Length
        Write-Host ("  あり: {0} ({1} バイト)" -f $iconName, $iconSize) -ForegroundColor Green
    }
    else {
        Write-Host ("  なし: {0}" -f $iconName) -ForegroundColor Yellow
        $missingIcons += $iconName
    }
}

if ($missingIcons.Count -gt 0) {
    Write-Host ''
    Write-Host '  [情報] リボンのアイコンが配置されていません。' -ForegroundColor Yellow
    Write-Host '         リボンのボタンはアイコン無しで表示されます（機能は使えます）。'
    Write-Host '         DeployDev_開発用配置.ps1 を実行し直してください。'
}
else {
    Write-Host '  [OK] リボンのアイコンは配置されています。' -ForegroundColor Green
}

# --- 5. SQL Server用DLL ---
# CSVだけで使う場合は無くても動くため、[NG]ではなく[情報]として扱い、診断全体は失敗させません。
#
# ビルド出力と配置先の両方を見ます。
# 以前はビルド出力を見ずに「DeployDev_開発用配置.ps1 を実行し直してください」とだけ出しており、
# ビルド出力にDLLが無い環境では、配置スクリプトを何度実行しても同じ表示が返るだけで、
# 原因に辿り着けませんでした。「まだ作られていない」と「作られたが配られていない」を必ず区別します。
#
# 管理DLLだけを見て[OK]としないでください。ネイティブのSNI DLLが無いと接続時に初めて失敗します。
Write-Host ''
Write-Host '===== 5. SQL Server接続用DLL =====' -ForegroundColor Cyan

$buildRoot = Split-Path -Parent $builtDll
$builtSqlClient = Join-Path $buildRoot 'Microsoft.Data.SqlClient.dll'
$builtSni = Join-Path $buildRoot 'runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll'
$deployedSqlClient = Join-Path $addinsRoot 'BUI\Microsoft.Data.SqlClient.dll'
$deployedSni = Join-Path $addinsRoot 'BUI\Microsoft.Data.SqlClient.SNI.dll'

$hasBuiltSqlClient = Test-Path -LiteralPath $builtSqlClient -PathType Leaf
$hasBuiltSni = Test-Path -LiteralPath $builtSni -PathType Leaf
$hasDeployedSqlClient = Test-Path -LiteralPath $deployedSqlClient -PathType Leaf
$hasDeployedSni = Test-Path -LiteralPath $deployedSni -PathType Leaf

function Show-BuiPresence {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Path, [bool]$Exists)
    $mark = if ($Exists) { 'あり' } else { 'なし' }
    $color = if ($Exists) { 'Green' } else { 'Yellow' }
    Write-Host ("  {0}: {1}" -f $Label, $mark) -ForegroundColor $color
    Write-Host ("     {0}" -f $Path)
}

Show-BuiPresence -Label 'ビルド出力 管理DLL      ' -Path $builtSqlClient -Exists $hasBuiltSqlClient
Show-BuiPresence -Label 'ビルド出力 ネイティブDLL' -Path $builtSni -Exists $hasBuiltSni
Show-BuiPresence -Label '配置先 管理DLL          ' -Path $deployedSqlClient -Exists $hasDeployedSqlClient
Show-BuiPresence -Label '配置先 ネイティブDLL    ' -Path $deployedSni -Exists $hasDeployedSni

if (-not $hasBuiltSqlClient -and -not $hasBuiltSni) {
    Write-Host '  [情報] ビルド出力にSQL接続用のDLLがありません。' -ForegroundColor Yellow
    Write-Host '       これは配置ではなくビルドの問題です。配置スクリプトを何度実行しても変わりません。' -ForegroundColor Yellow
    Write-Host '       Bui.Addin.csproj に次の1行があるか確認してください。' -ForegroundColor Yellow
    Write-Host '         <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>' -ForegroundColor Yellow
    Write-Host '       クラスライブラリは、これが無いとNuGetパッケージのDLLを出力へコピーしません。' -ForegroundColor Yellow
    Write-Host '       追加したら Visual Studio で「ソリューションのリビルド」をやり直してください。' -ForegroundColor Yellow
    Write-Host '       CSVだけを使う場合は、このままでも動きます。' -ForegroundColor Yellow
}
elseif (-not $hasBuiltSqlClient -or -not $hasBuiltSni) {
    Write-Host '  [情報] ビルド出力にSQL接続用のDLLが一部しかありません。' -ForegroundColor Yellow
    Write-Host '       上の「あり/なし」で欠けている方を確認し、リビルドをやり直してください。' -ForegroundColor Yellow
}
elseif ($hasDeployedSqlClient -and $hasDeployedSni -and (Test-BuiSqlClientFacade -Path $deployedSqlClient)) {
    Write-Host '  [情報] 配置されている管理DLLが facade です。このままでは接続時に' -ForegroundColor Yellow
    Write-Host '       「Microsoft.Data.SqlClient is not supported on this platform.」で失敗します。' -ForegroundColor Yellow
    Write-Host '       Windows用の実体（約2.1MB）へ置き換える必要があります。' -ForegroundColor Yellow
    Write-Host '       Revitを完全終了してから「ソリューションのリビルド」をやり直し、' -ForegroundColor Yellow
    Write-Host '       そのあと DeployDev_開発用配置.ps1 を実行してください。' -ForegroundColor Yellow
}
elseif ($hasDeployedSqlClient -and $hasDeployedSni) {
    Write-Host '  [OK] 管理DLLとネイティブDLLの両方が配置されています。SQL Serverへ接続できます。' -ForegroundColor Green
    Write-Host ("     管理DLL {0:N0} バイト（facadeではありません）" -f (Get-Item -LiteralPath $deployedSqlClient).Length) -ForegroundColor Green
}
else {
    Write-Host '  [情報] ビルド出力にはありますが、配置先へ届いていません。' -ForegroundColor Yellow
    Write-Host '       DeployDev_開発用配置.ps1 を実行してください。' -ForegroundColor Yellow
}

# --- 判定 ---
Write-Host ''
Write-Host '===== 判定 =====' -ForegroundColor Yellow

if ($null -eq $built) {
    Write-Host '  ビルド出力がありません。まずビルドしてください。' -ForegroundColor Red
}
elseif ($null -eq $deployed) {
    Write-Host '  配置先にDLLがありません。DeployDev_開発用配置.ps1 を実行してください。' -ForegroundColor Red
}
elseif ($deployed.LastWriteTime -lt $built.LastWriteTime) {
    $diff = [int]($built.LastWriteTime - $deployed.LastWriteTime).TotalMinutes
    Write-Host "  [NG] 配置先のDLLが $diff 分古いままです。" -ForegroundColor Red
    Write-Host '       Revitを完全に終了してから、もう一度ビルドしてください。'
    Write-Host '       それでも古いままなら、次を実行して手動で配置してください。'
    Write-Host '         .\scripts_開発補助\DeployDev_開発用配置.ps1 -Configuration Debug'
}
else {
    Write-Host '  [OK] ビルド出力と配置先が同じ内容です。' -ForegroundColor Green
    Write-Host '       上の「入っている機能」がすべて[OK]なら、Revitは最新のBUIを読み込みます。'
    Write-Host '       それでも画面が古い場合はRevitを再起動してください。'
}

Write-Host ''
Write-Host 'この表示内容をそのまま共有してください。保存場所以外の秘密情報は含みません。'
Write-Host ''
