# 【重要】このファイルはUTF-8 BOM付きで保存してください。
# Windows PowerShell 5.1 はBOMの無い.ps1をShift_JISとして読むため、
# BOMを外すと日本語の文字列が壊れて「トークンを使用できません」の構文エラーになります。
# BUIの開発用DLLを、現在のWindowsユーザーのRevit 2025用Addinsへ配置します。
# 依存先はRevit 2025、.NET SDK、BUIのビルド出力です。配置先の年やファイル追加を変えると安全境界が変わるため、テストも同時に修正してください。

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$RevitPath = 'C:\Program Files\Autodesk\Revit 2025',
    [switch]$Build
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BuiFullPath {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -match '%[^%]+%' -or
        $Path -match '\$\{?env:') {
        throw "$Label に空文字または未解決の環境変数が含まれています。"
    }

    try {
        return [IO.Path]::GetFullPath($Path)
    }
    catch {
        throw "$Label を絶対パスとして解決できません。"
    }
}

function Assert-BuiNoReparsePoint {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$StopAt
    )

    $current = ConvertTo-BuiFullPath -Path $Path -Label '安全確認パス'
    $stop = ConvertTo-BuiFullPath -Path $StopAt -Label '安全確認の開始パス'

    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "配置先の途中にjunction/symlinkがあるため中止しました: $current"
            }
        }

        if ([string]::Equals($current.TrimEnd('\'), $stop.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) {
            throw '配置先の安全範囲を確認できません。'
        }
        $current = $parent
    }
}

function Assert-BuiOwnedTemporaryDirectory {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)

    $candidate = ConvertTo-BuiFullPath -Path $Path -Label '一時配置フォルダ'
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $candidateParent = (Split-Path -Parent $candidate).TrimEnd('\')
    $candidateLeaf = Split-Path -Leaf $candidate

    # 再帰削除は、OS一時フォルダ直下のBUIが作成したGUID付きフォルダだけに限定します。
    if (-not [string]::Equals($candidateParent, $temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
        $candidateLeaf -notmatch '^BUI-deploy-[0-9a-fA-F]{32}$') {
        throw '一時配置フォルダの所有範囲を確認できません。'
    }

    if (Test-Path -LiteralPath $candidate) {
        $item = Get-Item -LiteralPath $candidate -Force
        if (-not $item.PSIsContainer -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw '一時配置フォルダが通常のフォルダではないため中止しました。'
        }
    }

    return $candidate
}

function Assert-BuiOwnedDeployPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$OwnedRoot,
        [Parameter(Mandatory)][string]$AddinsRoot,
        [Parameter(Mandatory)][string]$AppDataPath
    )

    $candidate = ConvertTo-BuiFullPath -Path $Path -Label '配置対象'
    $owned = ConvertTo-BuiFullPath -Path $OwnedRoot -Label 'BUI所有フォルダ'
    $addins = ConvertTo-BuiFullPath -Path $AddinsRoot -Label 'Addins 2025フォルダ'
    $appData = ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA'
    $expectedAddins = ConvertTo-BuiFullPath `
        -Path (Join-Path $appData 'Autodesk\Revit\Addins\2025') `
        -Label 'Revit Addins 2025フォルダ'

    # 完全パスを文字列の前方一致だけで判定せず、区切り文字付きで直下の所有範囲を限定します。
    if (-not [string]::Equals($addins.TrimEnd('\'), $expectedAddins.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or
        (-not [string]::Equals($owned.TrimEnd('\'), $addins.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -and
         -not $owned.StartsWith($addins.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) -or
        -not $candidate.StartsWith($owned.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw '配置対象がBUI所有のRevit Addins 2025範囲外です。'
    }

    # 配置先leaf自体まで確認するため、ファイルsymlink/reparseもCopy-Itemより前に拒否します。
    Assert-BuiNoReparsePoint -Path $addins -StopAt $appData
    Assert-BuiNoReparsePoint -Path $owned -StopAt $addins
    Assert-BuiNoReparsePoint -Path $candidate -StopAt $owned
    return $candidate
}

function Resolve-BuiDeployTarget {
    [CmdletBinding()]
    param(
        [string]$Destination,
        [string]$AppDataPath = $env:APPDATA,
        [string]$RevitYear = '2025'
    )

    if ($RevitYear -ne '2025') {
        throw 'BUI PoCはRevit 2025専用です。他の年へは配置できません。'
    }

    $appDataRoot = ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA'
    $expectedRoot = ConvertTo-BuiFullPath `
        -Path (Join-Path $appDataRoot 'Autodesk\Revit\Addins\2025') `
        -Label 'Revit Addins 2025フォルダ'
    $requestedRoot = if ([string]::IsNullOrWhiteSpace($Destination)) {
        $expectedRoot
    }
    else {
        ConvertTo-BuiFullPath -Path $Destination -Label '配置先'
    }

    # 「Addins配下ならどこでも良い」とせず、2025直下だけを許可します。
    if (-not [string]::Equals($requestedRoot.TrimEnd('\'), $expectedRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw '配置先は現在のユーザーの Autodesk\Revit\Addins\2025 直下だけを指定できます。'
    }

    $addinDirectory = Join-Path $expectedRoot 'BUI'
    $manifestPath = Join-Path $expectedRoot 'BUI.addin'

    # Addinsルートだけでなく、実際に書き込む2対象までのjunction/symlinkを調べます。
    # ここを省くと、`BUI` フォルダ自体が別フォルダを指す場合に配置範囲を逸脱します。
    Assert-BuiNoReparsePoint -Path $expectedRoot -StopAt $appDataRoot
    Assert-BuiNoReparsePoint -Path $addinDirectory -StopAt $appDataRoot
    Assert-BuiNoReparsePoint -Path $manifestPath -StopAt $appDataRoot

    return [pscustomobject]@{
        AddinsRoot = $expectedRoot
        AddinDirectory = $addinDirectory
        ManifestPath = $manifestPath
    }
}

function Get-BuiDeployAllowlist {
    # ローカル設定、認証情報、CSV、RVT、ログ、バックアップ、一時ファイルはこの一覧に入れません。
    return @(
        'Bui.dll',
        'Bui.pdb',
        'Bui.deps.json',
        'Config\AddinMetadata_アドイン情報.json',
        'Config\Settings.Default_共通設定.json',
        'Resources_画像と定義\SharedParameters_共有パラメータ.txt',
        # リボンのアイコンです。この一覧に無いと配置先へ届かず、
        # 症状は「アイコンが出ない」だけになり、原因に辿り着けません。
        'Resources_画像と定義\リボン_01_パネル表示.ico',
        'Resources_画像と定義\リボン_02_パラメータ作成.ico',
        'Resources_画像と定義\リボン_03_AI.ico'
    )
}

function Get-BuiSqlDeployRelativePaths {
    param([Parameter(Mandatory)][string]$OutputPath)

    # SQL接続用のDLLです。Microsoft.Data.SqlClientは依存DLLを連れてくるため、
    # 個別に列挙せずパターンで拾います。
    # これらが配置先に無いと、SQL Serverへの切り替えが必ず失敗します。
    # CSVだけで使う場合は無くても動くため、見つからなくてもエラーにしません。
    #
    # 2026-09-15までは「Windows認証専用だからAzure.*/Microsoft.Identity*/
    # Microsoft.IdentityModel*は不要」という判断でこれらを配布対象から
    # 意図的に外していました。しかし実機で
    # 「Could not load file or assembly 'Microsoft.Identity.Client, …'」が
    # 発生し、この判断が誤りだったと判明しました。
    # Microsoft.Data.SqlClientのSqlAuthenticationProviderManagerは、
    # Windows認証だけを使う場合でも、静的初期化の時点で
    # ActiveDirectory系の認証プロバイダーをまとめて登録しようとして
    # Microsoft.Identity.Client型に触れます。実際にその経路を使うかどうかに
    # 関わらず、アセンブリの存在自体が必要です。
    #
    # 個別列挙による漏れを繰り返さないよう、Bui.dll以外の*.dllは
    # すべて配布対象にします（このフォルダにはBui.Addinが参照する
    # パッケージの依存DLLしか無いため、全量配布して安全です）。
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $OutputPath -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
        if ($file.Name -eq 'Bui.dll') { continue }
        $found.Add($file.Name)
    }

    # ネイティブのSNI DLLはフォルダ構造ごと必要です。
    $nativeRelative = 'runtimes\win-x64\native'
    $nativeRoot = Join-Path $OutputPath $nativeRelative
    if (Test-Path -LiteralPath $nativeRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $nativeRoot -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
            $found.Add((Join-Path $nativeRelative $file.Name))
            # Revitアドインは Revit.exe の deps context で読み込まれ、そちらにはアドイン自身の
            # .deps.json が含まれないため、runtimes\win-x64\native 配下へのネイティブプローブが
            # 必ず届くとは限りません。Revitアドインの定番の回避策として、管理DLLと同じフォルダへも
            # 平置きでコピーします（Finding 5）。
            $found.Add($file.Name)
        }
    }

    return $found.ToArray()
}

function Resolve-BuiSqlDeploySourcePath {
    param(
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $direct = Join-Path $OutputPath $RelativePath

    # フォルダを含む相対パス（runtimes 配下をそのまま配る分）は、そのまま使います。
    if ($RelativePath.Contains('\')) { return $direct }

    # ここから下は「BUIフォルダ直下へ平置きする分」の解決です。探す順番に意味があります。
    #
    # 1. runtimes\win\lib\net8.0 …… Windows用の実体
    #    出力直下の Microsoft.Data.SqlClient.dll は約900KBの facade で、実装は約2.1MBの
    #    こちらにあります。通常は .deps.json の runtimeTargets がどちらを読むかを振り分けますが、
    #    Revitアドインは Revit.exe の deps context で読み込まれ、そこにアドインの .deps.json は
    #    含まれません。facade をそのまま配ると、接続を試みたときに初めて失敗します。
    # 2. runtimes\win-x64\native …… ネイティブのSNI DLL。出力直下には実体がありません。
    # 3. 出力直下 …… 上の2つに無いもの（RID別を持たないDLL）。
    $preferred = @(
        (Join-Path $OutputPath (Join-Path 'runtimes\win\lib\net8.0' $RelativePath)),
        (Join-Path $OutputPath (Join-Path 'runtimes\win-x64\native' $RelativePath))
    )
    foreach ($candidate in $preferred) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    return $direct
}

function Assert-BuiDeployInputs {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$RevitPath,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $required = @(
        (Join-Path $RevitPath 'RevitAPI.dll'),
        (Join-Path $RevitPath 'RevitAPIUI.dll'),
        (Join-Path $RepositoryRoot 'Bui.sln'),
        (Join-Path $RepositoryRoot 'src_ソース\Bui.Addin\Bui.Addin.csproj'),
        (Join-Path $RepositoryRoot 'src_ソース\Bui.Addin\Resources_画像と定義\BUI.addin'),
        (Join-Path $OutputPath 'Bui.dll'),
        (Join-Path $OutputPath 'Config\AddinMetadata_アドイン情報.json'),
        (Join-Path $OutputPath 'Config\Settings.Default_共通設定.json')
    )

    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "配置前の必須ファイルがありません: $path"
        }
    }
}

function New-BuiRenderedManifest {
    param(
        [Parameter(Mandatory)][string]$TemplatePath,
        [Parameter(Mandatory)][string]$DllPath,
        [Parameter(Mandatory)][string]$TemporaryDirectory
    )

    $absoluteDll = ConvertTo-BuiFullPath -Path $DllPath -Label 'Bui.dll'
    if (-not [IO.Path]::IsPathRooted($absoluteDll)) {
        throw 'マニフェストのDLLパスは絶対パスである必要があります。'
    }

    $template = Get-Content -LiteralPath $TemplatePath -Raw -Encoding UTF8
    if ($template -notmatch '\{\{BUI_ASSEMBLY_PATH\}\}') {
        throw 'マニフェストテンプレートにDLLパスの置換記号がありません。'
    }

    $escapedDll = [Security.SecurityElement]::Escape($absoluteDll)
    $rendered = $template.Replace('{{BUI_ASSEMBLY_PATH}}', $escapedDll)
    if ($rendered -match '\{\{BUI_ASSEMBLY_PATH\}\}') {
        throw 'マニフェストのDLLパス置換に失敗しました。'
    }

    try {
        [xml]$xml = $rendered
    }
    catch {
        throw '置換後のBUI.addinが正しいXMLではありません。'
    }
    $manifestAssembly = [string]$xml.RevitAddIns.AddIn.Assembly
    if (-not [IO.Path]::IsPathRooted($manifestAssembly) -or
        -not [string]::Equals([IO.Path]::GetFullPath($manifestAssembly), $absoluteDll, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'マニフェスト内のDLLパスが、配置するBui.dllの絶対パスと一致しません。'
    }

    # WhatIf中でも、実配置ではない一時領域でmanifestの内容検査まで完了させます。
    New-Item -ItemType Directory -Force -Path $TemporaryDirectory -WhatIf:$false | Out-Null
    $temporaryManifest = Join-Path $TemporaryDirectory ("BUI.{0}.addin.tmp" -f [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($temporaryManifest, $rendered, [Text.UTF8Encoding]::new($false))
    return $temporaryManifest
}

function Invoke-BuiDevDeploy {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
        [string]$RevitPath = 'C:\Program Files\Autodesk\Revit 2025',
        [switch]$Build,
        [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
        [string]$AppDataPath = $env:APPDATA,
        [string]$OutputPath
    )

    $repo = ConvertTo-BuiFullPath -Path $RepositoryRoot -Label 'リポジトリ'
    $revit = ConvertTo-BuiFullPath -Path $RevitPath -Label 'Revit 2025'
    $project = Join-Path $repo 'src_ソース\Bui.Addin\Bui.Addin.csproj'
    $solution = Join-Path $repo 'Bui.sln'
    $manifestTemplate = Join-Path $repo 'src_ソース\Bui.Addin\Resources_画像と定義\BUI.addin'

    # Build指定時だけビルドし、失敗したら配置処理に進みません。
    if ($Build) {
        foreach ($preBuildPath in @(
            (Join-Path $revit 'RevitAPI.dll'),
            (Join-Path $revit 'RevitAPIUI.dll'),
            $solution,
            $project,
            $manifestTemplate)) {
            if (-not (Test-Path -LiteralPath $preBuildPath -PathType Leaf)) {
                throw "ビルド前の必須ファイルがありません: $preBuildPath"
            }
        }

        & dotnet build $solution -c $Configuration "-p:Revit2025Path=$revit"
        if ($LASTEXITCODE -ne 0) {
            throw 'ビルドに失敗したため、BUIは配置しませんでした。'
        }
    }

    $output = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Join-Path $repo "src_ソース\Bui.Addin\bin\$Configuration\net10.0-windows"
    }
    else {
        ConvertTo-BuiFullPath -Path $OutputPath -Label 'ビルド出力'
    }

    Assert-BuiDeployInputs -RepositoryRoot $repo -RevitPath $revit -OutputPath $output
    $target = Resolve-BuiDeployTarget -AppDataPath $AppDataPath -RevitYear '2025'
    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("BUI-deploy-{0}" -f [Guid]::NewGuid().ToString('N'))
    $stagedAddin = Join-Path $tempRoot 'BUI'

    try {
        # Forceを使わず、この実行が新規作成した一時フォルダだけを後で片付けます。
        New-Item -ItemType Directory -Path $tempRoot -WhatIf:$false | Out-Null
        Assert-BuiOwnedTemporaryDirectory -Path $tempRoot | Out-Null
        New-Item -ItemType Directory -Force -Path $stagedAddin -WhatIf:$false | Out-Null

        # 固定の許可一覧に、ビルド出力にあるSQL用DLLを足した一覧を1回だけ作ります。
        # 2つのループが同じ一覧を使わないと、ステージには入ったのに配置先へ届かない、
        # という分かりにくい状態になります。
        $sqlDeployFiles = @(Get-BuiSqlDeployRelativePaths -OutputPath $output)
        if ($sqlDeployFiles.Count -eq 0) {
            # 黙って何もせず終わると、利用者は配置スクリプトを何度も実行し続けます。
            # 実際にそうなりました。配置の問題ではないことを、ここではっきり言います。
            Write-Warning 'ビルド出力にSQL接続用のDLLがありません。SQL Serverへは接続できません。'
            Write-Warning '  これは配置ではなくビルドの問題です。何度実行しても変わりません。'
            Write-Warning '  Bui.Addin.csproj の CopyLocalLockFileAssemblies が true か確認し、'
            Write-Warning '  Visual Studioで「ソリューションのリビルド」をやり直してください。'
            Write-Warning '  CSVだけを使う場合は、このままでも動きます。'
        }
        $deployFiles = @(Get-BuiDeployAllowlist) + $sqlDeployFiles

        foreach ($relativePath in $deployFiles) {
            $source = Resolve-BuiSqlDeploySourcePath -OutputPath $output -RelativePath $relativePath
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                # Resources_画像と定義\* はもともとSharedParameters_共有パラメータ.txt（ID初期設定に必須）のための
                # 必須指定でした。同じフォルダにリボンの.icoを足したことで、見た目だけのアイコンが
                # 欠けただけでも配置全体が止まる（Bui.dllすら届かない）状態になっていました。
                # RibbonServiceはアイコンが読めなくてもボタンを必ず作る設計（「アイコンが読めなくても
                # ボタンは必ず作る」）なので、.icoはこの必須判定から除外し、警告だけで続行します。
                if ($relativePath -like 'Resources_画像と定義\*' -and $relativePath -like '*.ico') {
                    Write-Warning "アイコンファイルがビルド出力にありません（見た目だけの問題です）: $source"
                    continue
                }
                if ($relativePath -eq 'Bui.dll' -or $relativePath -like 'Config\*' -or $relativePath -like 'Resources_画像と定義\*') {
                    throw "BUIの必須配置ファイルがありません: $source"
                }
                continue
            }

            $stagePath = Join-Path $stagedAddin $relativePath
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $stagePath) -WhatIf:$false | Out-Null
            Copy-Item -LiteralPath $source -Destination $stagePath -Force -WhatIf:$false
        }

        $deployedDll = Join-Path $target.AddinDirectory 'Bui.dll'
        $stagedManifest = New-BuiRenderedManifest -TemplatePath $manifestTemplate `
            -DllPath $deployedDll -TemporaryDirectory $tempRoot

        if ($PSCmdlet.ShouldProcess($target.AddinsRoot, 'BUIアドインをRevit 2025用に配置')) {
            New-Item -ItemType Directory -Force -Path $target.AddinsRoot | Out-Null
            Assert-BuiNoReparsePoint -Path $target.AddinsRoot -StopAt (ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA')
            Assert-BuiNoReparsePoint -Path $target.AddinDirectory -StopAt (ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA')
            Assert-BuiNoReparsePoint -Path $target.ManifestPath -StopAt (ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA')
            New-Item -ItemType Directory -Force -Path $target.AddinDirectory | Out-Null
            Assert-BuiNoReparsePoint -Path $target.AddinDirectory -StopAt (ConvertTo-BuiFullPath -Path $AppDataPath -Label 'APPDATA')

            # 許可リストのファイルだけをステージから複製します。フォルダ全体のコピーはしません。
            foreach ($relativePath in $deployFiles) {
                $source = Join-Path $stagedAddin $relativePath
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
                $destination = Join-Path $target.AddinDirectory $relativePath
                $safeDestination = Assert-BuiOwnedDeployPath -Path $destination `
                    -OwnedRoot $target.AddinDirectory -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                $destinationParent = Split-Path -Parent $safeDestination

                # 親作成の前後とcopy直前にleafまで再検査し、ネストしたjunctionや既存ファイルsymlinkを追跡しません。
                Assert-BuiOwnedDeployPath -Path $safeDestination `
                    -OwnedRoot $target.AddinDirectory -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath | Out-Null
                New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
                $safeDestination = Assert-BuiOwnedDeployPath -Path $safeDestination `
                    -OwnedRoot $target.AddinDirectory -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                Copy-Item -LiteralPath $source -Destination $safeDestination -Force
            }

            $manifestTempAtDestination = Join-Path $target.AddinsRoot ("BUI.{0}.addin.tmp" -f [Guid]::NewGuid().ToString('N'))
            try {
                $safeManifestTemp = Assert-BuiOwnedDeployPath -Path $manifestTempAtDestination `
                    -OwnedRoot $target.AddinsRoot -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                $safeManifest = Assert-BuiOwnedDeployPath -Path $target.ManifestPath `
                    -OwnedRoot $target.AddinsRoot -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                Copy-Item -LiteralPath $stagedManifest -Destination $safeManifestTemp -Force

                # tempと既存manifestの両leafをMove直前に再検査し、リンク差し替えを許可しません。
                $safeManifestTemp = Assert-BuiOwnedDeployPath -Path $safeManifestTemp `
                    -OwnedRoot $target.AddinsRoot -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                $safeManifest = Assert-BuiOwnedDeployPath -Path $safeManifest `
                    -OwnedRoot $target.AddinsRoot -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                Move-Item -LiteralPath $safeManifestTemp -Destination $safeManifest -Force
            }
            finally {
                if (Test-Path -LiteralPath $manifestTempAtDestination) {
                    $safeManifestTemp = Assert-BuiOwnedDeployPath -Path $manifestTempAtDestination `
                        -OwnedRoot $target.AddinsRoot -AddinsRoot $target.AddinsRoot -AppDataPath $AppDataPath
                    Remove-Item -LiteralPath $safeManifestTemp -Force
                }
            }

            Write-Host '配置完了: BUIをRevit 2025用に配置しました。'
            Write-Host '1. Revit 2025が閉じていたことを確認し、Revit 2025を起動します。'
            Write-Host '2. BUIリボンとBUIパネルを確認します。'
            Write-Host "3. 問題時は %LOCALAPPDATA%\PoC\BUI\Logs を確認します。"
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            $safeTemporaryDirectory = Assert-BuiOwnedTemporaryDirectory -Path $tempRoot
            Remove-Item -LiteralPath $safeTemporaryDirectory -Recurse -Force -WhatIf:$false
        }
    }

    return $target
}

# dot-source時は関数定義だけを読み込み、Pesterが実際のAddinsへ配置しないようにします。
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-BuiDevDeploy -Configuration $Configuration -RevitPath $RevitPath -Build:$Build -WhatIf:$WhatIfPreference
}
