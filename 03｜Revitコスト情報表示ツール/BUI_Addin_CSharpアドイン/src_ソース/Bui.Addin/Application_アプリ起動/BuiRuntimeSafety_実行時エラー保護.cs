// このファイルはRevitが直接呼ぶ入口を、共通のErrorBoundaryで保護します。
// FileBuiLoggerとErrorBoundaryに依存し、保存先の変更はすべてのRevit入口ログに影響します。
using Bui.Common;
using Bui.Configuration;
using Bui.Logging;
using System.Reflection;

namespace Bui.Application;

internal static class BuiRuntimeSafety
{
    private static readonly Lazy<ErrorBoundary> Boundary = new(() =>
    {
        var localRoot = BuiLocalPaths.Root;
        var assembly = typeof(BuiRuntimeSafety).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = !string.IsNullOrWhiteSpace(informational)
            ? informational.Split('+', 2)[0]
            : assembly.GetName().Version?.ToString(3) ?? "unknown";
        // ログのバージョンはコードへ二重記載せず、実行中DLLのメタデータから取得します。
        return new ErrorBoundary(new FileBuiLogger(Path.Combine(localRoot, "Logs"), version));
    });

    public static OperationResult<T> Run<T>(string operation, Func<T> action) => Boundary.Value.Run(operation, action);
}
