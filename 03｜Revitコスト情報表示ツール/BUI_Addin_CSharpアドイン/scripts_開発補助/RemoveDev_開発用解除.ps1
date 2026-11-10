# 【重要】このファイルはUTF-8 BOM付きで保存してください。
# Windows PowerShell 5.1 はBOMの無い.ps1をShift_JISとして読むため、
# BOMを外すと日本語の文字列が壊れて「トークンを使用できません」の構文エラーになります。
# BUI開発用配置の `BUI.addin` と `BUI` フォルダだけを解除します。
# Addins\2025全体や他社アドインの削除に広がらないよう、解決済み絶対パスと末尾名を削除直前に再確認します。

[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BuiRemovalFullPath {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -match '%[^%]+%' -or
        $Path -match '\$\{?env:') {
        throw "$Label が空、または未解決です。"
    }
    try { return [IO.Path]::GetFullPath($Path) }
    catch { throw "$Label を絶対パスとして解決できません。" }
}

function Assert-BuiRemovalNoReparsePoint {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$StopAt
    )

    $current = ConvertTo-BuiRemovalFullPath -Path $Path -Label '安全確認パス'
    $stop = ConvertTo-BuiRemovalFullPath -Path $StopAt -Label '安全確認の開始パス'

    # 削除対象自体だけでなく、APPDATAまでの親も調べてリンク経由の範囲逸脱を防ぎます。
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "解除対象の途中にjunction/symlinkがあるため中止しました: $current"
            }
        }

        if ([string]::Equals($current.TrimEnd('\'), $stop.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) {
            throw '解除対象の安全範囲を確認できません。'
        }
        $current = $parent
    }
}

function Assert-BuiOwnedRemovalTarget {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedRoot,
        [Parameter(Mandatory)][ValidateSet('BUI','BUI.addin')][string]$ExpectedLeaf
    )

    $root = ConvertTo-BuiRemovalFullPath -Path $ExpectedRoot -Label 'Addins 2025フォルダ'
    $candidate = ConvertTo-BuiRemovalFullPath -Path $Path -Label '削除対象'
    $expected = [IO.Path]::GetFullPath((Join-Path $root $ExpectedLeaf))

    # 親フォルダとleaf名の両方が一致するBUI所有対象だけを許可します。
    if (-not [string]::Equals($candidate, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals((Split-Path -Leaf $candidate), $ExpectedLeaf, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals((Split-Path -Parent $candidate).TrimEnd('\'), $root.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "BUI所有の $ExpectedLeaf 以外は削除できません。"
    }

    if (Test-Path -LiteralPath $candidate) {
        $item = Get-Item -LiteralPath $candidate -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "junction/symlinkの削除は安全に確認できないため中止しました: $candidate"
        }
    }

    return $candidate
}

function Resolve-BuiRemovalTargets {
    [CmdletBinding()]
    param(
        [string]$AppDataPath = $env:APPDATA,
        [string]$RevitYear = '2025'
    )

    if ($RevitYear -ne '2025') {
        throw 'BUI PoCはRevit 2025専用です。他の年は解除対象にできません。'
    }
    $appData = ConvertTo-BuiRemovalFullPath -Path $AppDataPath -Label 'APPDATA'
    $root = [IO.Path]::GetFullPath((Join-Path $appData 'Autodesk\Revit\Addins\2025'))
    Assert-BuiRemovalNoReparsePoint -Path $root -StopAt $appData
    $manifest = Assert-BuiOwnedRemovalTarget -Path (Join-Path $root 'BUI.addin') -ExpectedRoot $root -ExpectedLeaf 'BUI.addin'
    $directory = Assert-BuiOwnedRemovalTarget -Path (Join-Path $root 'BUI') -ExpectedRoot $root -ExpectedLeaf 'BUI'
    Assert-BuiRemovalNoReparsePoint -Path $manifest -StopAt $appData
    Assert-BuiRemovalNoReparsePoint -Path $directory -StopAt $appData

    return [pscustomobject]@{
        AddinsRoot = $root
        ManifestPath = $manifest
        AddinDirectory = $directory
    }
}

function Invoke-BuiDevRemove {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$AppDataPath = $env:APPDATA,
        [string]$RevitYear = '2025'
    )

    $targets = Resolve-BuiRemovalTargets -AppDataPath $AppDataPath -RevitYear $RevitYear
    foreach ($entry in @(
        @{ Path = $targets.ManifestPath; Leaf = 'BUI.addin'; Recurse = $false },
        @{ Path = $targets.AddinDirectory; Leaf = 'BUI'; Recurse = $true })) {
        $safePath = Assert-BuiOwnedRemovalTarget -Path $entry.Path -ExpectedRoot $targets.AddinsRoot -ExpectedLeaf $entry.Leaf
        # 実行直前にも再検査し、対象解決後のリンク差し替えで別フォルダを削除しないようにします。
        Assert-BuiRemovalNoReparsePoint -Path $safePath -StopAt (ConvertTo-BuiRemovalFullPath -Path $AppDataPath -Label 'APPDATA')
        if ((Test-Path -LiteralPath $safePath) -and $PSCmdlet.ShouldProcess($safePath, 'BUI開発用配置を解除')) {
            if ($entry.Recurse) {
                Remove-Item -LiteralPath $safePath -Recurse -Force
            }
            else {
                Remove-Item -LiteralPath $safePath -Force
            }
        }
    }

    Write-Host 'BUI開発用配置の解除処理が終了しました。'
    Write-Host 'Revit 2025が閉じている状態で実施し、次回起動時にBUIが読み込まれないことを確認してください。'
}

# Pesterのdot-sourceでは削除を実行せず、関数だけを読み込みます。
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-BuiDevRemove -WhatIf:$WhatIfPreference
}
