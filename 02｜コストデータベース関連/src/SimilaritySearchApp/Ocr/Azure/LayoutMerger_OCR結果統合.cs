using System.Text.Json.Nodes;

namespace SimilaritySearchApp.Ocr.Azure;

/// <summary>
/// 2 ページずつ解析した Layout 結果を、PDF 全体の 1 つの JSON にまとめる（Python 版 merge_layout_results と同じ）。
///   ・pages / tables / paragraphs など配列は順につなげる
///   ・pageNumber は元 PDF のページ番号に、span の offset は統合後の content の位置に補正する
///   ・batch_info（元 PDF・ページ数・分割の内訳）を付ける
/// </summary>
public static class LayoutMerger
{
    public sealed record ChunkResult(int ChunkIndex, int StartPage, int EndPage, JsonObject Result);

    private static readonly string[] ListKeys =
        { "pages", "tables", "paragraphs", "sections", "styles", "keyValuePairs", "languages", "figures", "documents" };

    public static JsonObject Merge(IReadOnlyList<ChunkResult> chunks, string sourcePdfName, int totalPages, int chunkPages, DateTime? now = null)
    {
        if (chunks.Count == 0) throw new InvalidOperationException("統合対象の OCR 結果がありません。");

        // 先頭ブロックの付帯情報（apiVersion・modelId など）を引き継ぐ
        var merged = new JsonObject();
        foreach (var (key, value) in chunks[0].Result)
        {
            if (key == "content" || ListKeys.Contains(key)) continue;
            merged[key] = value?.DeepClone();
        }
        var lists = ListKeys.ToDictionary(k => k, _ => new JsonArray());

        var contentParts = new List<string>();
        var contentOffset = 0;
        foreach (var chunk in chunks)
        {
            var pageOffset = chunk.StartPage - 1;
            var chunkContent = (string?)chunk.Result["content"] ?? "";
            foreach (var key in ListKeys)
            {
                if (chunk.Result[key] is not JsonArray arr || arr.Count == 0) continue;
                foreach (var item in arr) lists[key].Add(Adjust(item, pageOffset, contentOffset));
            }
            contentParts.Add(chunkContent);
            contentOffset += chunkContent.Length + 1;
        }

        merged["content"] = string.Join("\n", contentParts);
        foreach (var key in ListKeys) merged[key] = lists[key];
        merged["batch_info"] = new JsonObject
        {
            ["source_pdf"] = sourcePdfName,
            ["source_pdf_page_count"] = totalPages,
            ["ocr_page_count"] = lists["pages"].Count,
            ["chunk_pages"] = chunkPages,
            ["chunks"] = new JsonArray(chunks.Select(c => (JsonNode)new JsonObject
            {
                ["chunk_index"] = c.ChunkIndex,
                ["start_page"] = c.StartPage,
                ["end_page"] = c.EndPage,
                ["ocr_page_count"] = (c.Result["pages"] as JsonArray)?.Count ?? 0,
            }).ToArray()),
            ["created_at"] = (now ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        return merged;
    }

    /// <summary>pageNumber と offset（整数）を補正した複製を作る。</summary>
    private static JsonNode? Adjust(JsonNode? node, int pageOffset, int contentOffset)
    {
        switch (node)
        {
            case JsonArray arr:
                return new JsonArray(arr.Select(v => Adjust(v, pageOffset, contentOffset)).ToArray());
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key == "pageNumber" && TryInt(value, out var p)) result[key] = p + pageOffset;
                    else if (key == "offset" && TryInt(value, out var o)) result[key] = o + contentOffset;
                    else result[key] = Adjust(value, pageOffset, contentOffset);
                }
                return result;
            }
            default:
                return node?.DeepClone();
        }
    }

    private static bool TryInt(JsonNode? v, out int n)
    {
        n = 0;
        return v is JsonValue jv && jv.TryGetValue(out n);
    }
}
