// このファイルは、Revit書込み後のCSV補償をUIスレッドから切り離し、同じ処理が何度通知されても一度だけ実行するために存在します。
// IAssemblyRepositoryとAssemblyRegistrationPlanに依存し、補償規則変更は新規CSV行とRevitタイプの整合性に影響します。
using Bui.Data;

namespace Bui.Assemblies;

public enum AssemblyCompensationOutcome
{
    NotRequired,
    RemovedNewRecord,
    RemovalFailed
}

/// <summary>Revit確定有無、補償結果、後処理失敗を画面とログへ返します。</summary>
public sealed record AssemblyPersistenceCompletion(
    bool RevitWriteCommitted,
    AssemblyCompensationOutcome CompensationOutcome,
    IReadOnlyList<string> CleanupFailures,
    string Detail);

/// <summary>最初の完了要求をTaskとして保持し、CSV補償の多重実行を防ぎます。</summary>
public sealed class AssemblyPersistenceCoordinator
{
    private readonly object _gate = new();
    private readonly IAssemblyRepository _repository;
    private readonly AssemblyRegistrationPlan _plan;
    private readonly bool _csvRecordAdded;
    private Task<AssemblyPersistenceCompletion>? _completionTask;

    public AssemblyPersistenceCoordinator(
        IAssemblyRepository repository,
        AssemblyRegistrationPlan plan,
        bool csvRecordAdded)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _csvRecordAdded = csvRecordAdded;
    }

    public Task<AssemblyPersistenceCompletion> CompleteAsync(
        bool revitWriteCommitted,
        IEnumerable<string> cleanupFailures)
    {
        ArgumentNullException.ThrowIfNull(cleanupFailures);
        var copiedFailures = cleanupFailures.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        lock (_gate)
        {
            // Revit失敗後の補償は画面のキャンセルで中断すると不整合が残るため、CancellationToken.Noneで完遂します。
            return _completionTask ??= CompleteCoreAsync(revitWriteCommitted, copiedFailures);
        }
    }

    private async Task<AssemblyPersistenceCompletion> CompleteCoreAsync(
        bool revitWriteCommitted,
        IReadOnlyList<string> cleanupFailures)
    {
        var action = AssemblyPersistenceDecision.DecideCompensation(
            _csvRecordAdded,
            _plan.ReusesExistingAssembly,
            revitWriteCommitted);
        if (action == AssemblyCompensationAction.None)
        {
            var reason = _plan.ReusesExistingAssembly
                ? "既存再利用IDのCSV行は削除していません。"
                : revitWriteCommitted
                    ? "Revit書込みが確定したためCSV補償は不要です。"
                    : "CSVに新規行を追加していないため補償は不要です。";
            return new AssemblyPersistenceCompletion(
                revitWriteCommitted,
                AssemblyCompensationOutcome.NotRequired,
                cleanupFailures,
                AppendCleanup(reason, cleanupFailures));
        }

        try
        {
            await _repository
                .RemoveAsync(_plan.Record.AssemblyId, CancellationToken.None)
                .ConfigureAwait(false);
            return new AssemblyPersistenceCompletion(
                revitWriteCommitted,
                AssemblyCompensationOutcome.RemovedNewRecord,
                cleanupFailures,
                AppendCleanup($"新規CSV行「{_plan.Record.AssemblyId}」を補償削除しました。", cleanupFailures));
        }
        catch (Exception exception)
        {
            return new AssemblyPersistenceCompletion(
                revitWriteCommitted,
                AssemblyCompensationOutcome.RemovalFailed,
                cleanupFailures,
                AppendCleanup(
                    $"新規CSV行「{_plan.Record.AssemblyId}」の補償削除に失敗しました: {exception.Message}",
                    cleanupFailures));
        }
    }

    private static string AppendCleanup(string detail, IReadOnlyList<string> cleanupFailures) =>
        cleanupFailures.Count == 0
            ? detail
            : $"{detail} Revit後処理の詳細: {string.Join(" | ", cleanupFailures)}";
}
