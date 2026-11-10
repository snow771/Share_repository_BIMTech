// このファイルはアドイン情報を不変の値として表し、設定JSONと画面表示で同じ項目を使うために存在します。
// 項目を追加・削除・改名すると、設定JSON、読込処理、後続の画面やテストの互換性に影響します。
namespace Bui.Metadata;

/// <summary>
/// アドインの表示情報と作成者情報を保持します。
/// </summary>
// このレコードの並びや型を変える場合は、JSONの項目対応とこの型を利用する後続コードも合わせて更新します。
public sealed record AddinMetadata(
    string DisplayName,
    string EnglishName,
    string DescriptionJa,
    string Author,
    string Department,
    string Company,
    string RevitVersion);
