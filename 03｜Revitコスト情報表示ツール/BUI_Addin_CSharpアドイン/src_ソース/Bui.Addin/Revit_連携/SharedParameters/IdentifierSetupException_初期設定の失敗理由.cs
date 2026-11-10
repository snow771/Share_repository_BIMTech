// このファイルはID初期設定が失敗した理由を、利用者にそのまま見せてよい日本語文として運びます。
// InstallIdentifiersCommandがこの型だけを特別扱いし、汎用の「予期しない問題」ではなく理由を画面へ出します。
// 新しい失敗経路を書くときは、必ずこの型で投げてください。件数や汎用文だけで済ませると、
// 会社PCでしか再現できない不具合の原因が画面に出ず、往復が積み上がります。

namespace Bui.Revit.SharedParameters;

/// <summary>ID初期設定で、原因が特定できている失敗を表します。Messageは利用者向けの日本語です。</summary>
public sealed class IdentifierSetupException : Exception
{
    public IdentifierSetupException(string userMessage) : base(userMessage)
    {
    }

    public IdentifierSetupException(string userMessage, Exception innerException)
        : base(userMessage, innerException)
    {
    }
}
