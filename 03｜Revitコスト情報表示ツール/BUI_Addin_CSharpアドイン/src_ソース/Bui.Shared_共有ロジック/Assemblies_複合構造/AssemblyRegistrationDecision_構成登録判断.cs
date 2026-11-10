// このファイルはAssembly_IDの再利用・新規候補・Revit失敗後のCSV補償を、Revit APIから分離して判断するために存在します。
// AssemblyFingerprint、AssemblyIdAllocator、IAssemblyRepositoryに依存し、判断変更は既存IDの保護と補償削除の対象に影響します。
using System.IO;
using Bui.Common;
using Bui.Data;

namespace Bui.Assemblies;

/// <summary>表示するAssembly_IDと、CSV追加確認が必要かを保持します。</summary>
public sealed record AssemblyRegistrationPlan(
    AssemblyRecord Record,
    string Fingerprint,
    bool ReusesExistingAssembly,
    bool RequiresRegistrationConfirmation);

/// <summary>構成照合を優先し、一致がない場合だけ新規IDを提案します。</summary>
public static class AssemblyRegistrationPlanner
{
    public static async Task<OperationResult<AssemblyRegistrationPlan>> PlanAsync(
        AssemblyRecord draft,
        IAssemblyRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(repository);

        var fingerprintResult = AssemblyFingerprint.Create(draft);
        if (!fingerprintResult.IsSuccess)
        {
            return new OperationResult<AssemblyRegistrationPlan>(default, fingerprintResult.Error);
        }

        try
        {
            var fingerprint = fingerprintResult.Value!;
            var existing = await repository
                .FindByFingerprintAsync(fingerprint, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return OperationResult<AssemblyRegistrationPlan>.Success(new AssemblyRegistrationPlan(
                    existing,
                    fingerprint,
                    ReusesExistingAssembly: true,
                    RequiresRegistrationConfirmation: false));
            }

            var ids = await repository.GetAllIdsAsync(cancellationToken).ConfigureAwait(false);
            var candidate = AssemblyIdAllocator.Next(ids, "ASM-", 6);
            return OperationResult<AssemblyRegistrationPlan>.Success(new AssemblyRegistrationPlan(
                draft with { AssemblyId = candidate },
                fingerprint,
                ReusesExistingAssembly: false,
                RequiresRegistrationConfirmation: true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 理由を落とさずに画面へ渡します。件数や汎用文だけにすると、
            // 「Assembly_IDが付かない」としか分からず、原因を一往復で1つしか潰せません。
            // 実際に、構成CSVが未作成なだけの状態がこの一文に丸められ、原因不明になりました。
            return OperationResult<AssemblyRegistrationPlan>.Failure(
                "ASSEMBLY_REGISTRATION_PREVIEW_FAILED",
                "Assembly_ID候補を確認できませんでした。理由: " + DescribeCause(exception),
                exception.ToString());
        }
    }

    /// <summary>構成CSVの読取で起きた例外を、利用者が次に何をすればよいか分かる一文にします。</summary>
    private static string DescribeCause(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "構成CSVを開く権限がありません。設定タブで書き込めるフォルダを指定してください。",
        InvalidDataException invalid => invalid.Message,
        IOException io => io.Message,
        _ => exception.Message
    };
}

public enum AssemblyCompensationAction
{
    None,
    RemoveNewRecord
}

/// <summary>既存再利用行を決して削除しない補償規則を提供します。</summary>
public static class AssemblyPersistenceDecision
{
    public static AssemblyCompensationAction DecideCompensation(
        bool wasNewRecordAdded,
        bool reusesExistingAssembly,
        bool revitWriteCommitted) =>
        // 既存IDの再利用中は、呼出側が誤って「追加済み」と渡しても絶対に削除しません。
        wasNewRecordAdded && !reusesExistingAssembly && !revitWriteCommitted
            ? AssemblyCompensationAction.RemoveNewRecord
            : AssemblyCompensationAction.None;
}
