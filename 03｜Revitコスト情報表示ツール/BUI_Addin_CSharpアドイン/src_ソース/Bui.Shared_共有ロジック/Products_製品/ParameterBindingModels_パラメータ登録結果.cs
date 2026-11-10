// このファイルはRevit APIから独立して、共有パラメータを作成・拡張・保持・競合停止する判定と実行報告を表現します。
// System.Guidとカテゴリ報告のみに依存し、判定順序を変えると既存Revitプロジェクトの値保護に影響します。
namespace Bui.Products;

/// <summary>共有パラメータ登録で実行する操作を表します。</summary>
public enum ParameterBindingAction
{
    CreateNew,
    ExtendCategories,
    AlreadyCorrect,
    Conflict
}

/// <summary>RevitのInstanceBindingとTypeBindingをAPI非依存で表します。</summary>
public enum ParameterBindingKind
{
    Instance,
    Type
}

/// <summary>登録操作と、利用者向け説明を保持します。</summary>
public sealed record ParameterBindingDecision(ParameterBindingAction Action, string Message)
{
    /// <summary>期待する定義と既存状態を比較し、データを壊さない操作を選びます。</summary>
    public static ParameterBindingDecision Decide(
        string expectedName,
        Guid expectedGuid,
        string? existingName,
        Guid? existingGuid,
        bool hasExpectedBinding,
        bool hasAllRequiredCategories = true,
        ParameterBindingKind expectedBindingKind = ParameterBindingKind.Instance,
        ParameterBindingKind? existingBindingKind = null,
        bool isCompatibleDefinition = true)
    {
        var hasExistingDefinition = existingName is not null || existingGuid.HasValue;

        // 名前だけで同一視せず、公開契約のGUIDが一致しなければ既存値を保護して停止します。
        if (hasExistingDefinition
            && (!string.Equals(expectedName, existingName, StringComparison.Ordinal)
                || existingGuid != expectedGuid))
        {
            return new ParameterBindingDecision(
                ParameterBindingAction.Conflict,
                $"既存定義「{existingName ?? "(名前なし)"}」のGUIDまたは名前が期待値と異なります。");
        }

        if (hasExistingDefinition && !isCompatibleDefinition)
        {
            return new ParameterBindingDecision(
                ParameterBindingAction.Conflict,
                $"既存定義「{expectedName}」のデータ型または設定が互換ではありません。");
        }

        if (hasExpectedBinding && existingBindingKind != expectedBindingKind)
        {
            return new ParameterBindingDecision(
                ParameterBindingAction.Conflict,
                $"既存定義「{expectedName}」の登録種別が{expectedBindingKind}ではありません。");
        }

        if (!hasExpectedBinding)
        {
            return new ParameterBindingDecision(
                ParameterBindingAction.CreateNew,
                $"共有パラメータ「{expectedName}」を新規登録します。");
        }

        if (!hasAllRequiredCategories)
        {
            return new ParameterBindingDecision(
                ParameterBindingAction.ExtendCategories,
                $"共有パラメータ「{expectedName}」の対象カテゴリを追加します。");
        }

        return new ParameterBindingDecision(
            ParameterBindingAction.AlreadyCorrect,
            $"共有パラメータ「{expectedName}」は設定済みです。");
    }
}

/// <summary>カテゴリごとの登録結果と理由を保持します。</summary>
public sealed record CategoryBindingResult(string CategoryName, string Status, string Message);

/// <summary>共有パラメータ登録のカテゴリ結果と競合をまとめます。</summary>
public sealed record ParameterBindingReport(
    IReadOnlyList<CategoryBindingResult> Categories,
    IReadOnlyList<string> Conflicts);
