// このファイルは、同名・未保存のRevit文書でもセッション内で厳密に区別できる不透明キーを発行します。
// Autodesk Document参照とConditionalWeakTableに依存し、変更は点検後の誤文書ナビゲーション防止に影響します。
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace Bui.Revit.UI;

internal static class RevitDocumentIdentity
{
    private static readonly ConditionalWeakTable<Document, Holder> Keys = new();

    public static string GetKey(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Keys.GetValue(document, static _ => new Holder(Guid.NewGuid().ToString("N"))).Value;
    }

    private sealed record Holder(string Value);
}
