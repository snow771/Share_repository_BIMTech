// このファイルは、プレビュー後も既存Assembly_ID行が同じ内容で残っているかを、Revit書込イベント直前に非同期検査するために存在します。
// IAssemblyRepository、AssemblyRegistrationPlan、AssemblyFingerprintに依存し、検査変更は既存IDを誤ってRevitへ書く危険性に影響します。
using Bui.Common;
using Bui.Data;

namespace Bui.Assemblies;

/// <summary>再利用予定のIDとフィンガープリントがリポジトリに現存することを確認します。</summary>
public static class AssemblyReuseValidator
{
    public static async Task<OperationResult<AssemblyRecord>> ValidateAsync(
        AssemblyRegistrationPlan plan,
        IAssemblyRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(repository);
        if (!plan.ReusesExistingAssembly)
        {
            return OperationResult<AssemblyRecord>.Failure(
                "ASSEMBLY_REUSE_VALIDATION_NOT_APPLICABLE",
                "新規Assembly_ID候補は再利用検査の対象外です。",
                plan.Record.AssemblyId);
        }

        try
        {
            var current = await repository
                .FindByFingerprintAsync(plan.Fingerprint, cancellationToken)
                .ConfigureAwait(false);
            if (current is null
                || !string.Equals(current.AssemblyId, plan.Record.AssemblyId, StringComparison.Ordinal))
            {
                return Stale(plan, current, "ID or row is missing");
            }

            var currentFingerprint = AssemblyFingerprint.Create(current);
            if (!currentFingerprint.IsSuccess
                || !string.Equals(currentFingerprint.Value, plan.Fingerprint, StringComparison.Ordinal))
            {
                return Stale(plan, current, "fingerprint changed");
            }

            return OperationResult<AssemblyRecord>.Success(current);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return OperationResult<AssemblyRecord>.Failure(
                "ASSEMBLY_REUSE_VALIDATION_FAILED",
                "既存Assembly_IDの直前確認に失敗したため、Revitへは書き込みません。",
                exception.ToString());
        }
    }

    private static OperationResult<AssemblyRecord> Stale(
        AssemblyRegistrationPlan plan,
        AssemblyRecord? current,
        string reason) =>
        OperationResult<AssemblyRecord>.Failure(
            "ASSEMBLY_REUSE_STALE",
            "再利用予定のAssembly_ID行が変更または削除されたため、再度プレビューしてください。",
            $"planned={plan.Record.AssemblyId}/{plan.Fingerprint}; current={current?.AssemblyId ?? "<missing>"}; reason={reason}");
}
