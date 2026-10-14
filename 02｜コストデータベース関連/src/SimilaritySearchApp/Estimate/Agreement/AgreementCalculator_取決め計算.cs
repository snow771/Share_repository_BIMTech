using System.Globalization;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Estimate.Agreement;

/// <summary>確定条件の判定に必要な、原資料（元PDF）の状況。</summary>
public sealed record EvidenceInfo(bool HasPdf, bool EvidenceIsPdfOfThisEstimate, int? PdfPageCount);

/// <summary>
/// 取決めの計算と確定条件（純粋な計算。DB・画面に依存しない）。
///
/// 計算規則 TotalRate-v1（諸経費なし）:
///   掛率       ＝ 取決め総額 ÷ 基準総額（小数12桁）
///   換算単価   ＝ 単価 × 掛率（10円単位）。単価が無い行は空欄（金額だけ換算）
///   換算金額   ＝ 金額 × 掛率（円単位）
///
/// 計算規則 TotalRate-v2（諸経費あり。見積が「内訳合計＋諸経費＝総合計」の形のとき）:
///   諸経費を各明細の単価へ配分してから、最終の掛率をかける。諸経費の行そのものは換算しない。
///   諸経費掛率   ＝ (内訳合計 ＋ 諸経費) ÷ 内訳合計（小数12桁）   … 内訳合計＝諸経費の行を除いた計算対象行の金額合計
///   諸経費込み単価 ＝ 単価 × 諸経費掛率（10円単位）／ 諸経費込み金額 ＝ 金額 × 諸経費掛率（円）
///   掛率（最終） ＝ 取決め総額 ÷ 基準総額（総合計）（小数12桁）
///   換算単価     ＝ 単価 × 諸経費掛率 × 掛率（10円単位）／ 換算金額 ＝ 金額 × 諸経費掛率 × 掛率（円）
///   （途中では丸めず、最後に 1 回だけ丸める）
///
/// 共通: 丸めは四捨五入（中間値は 0 から遠い側）。残差＝取決め総額－換算金額の合計（特定の明細へは配賦しない）。
/// 明細の値は、人の補正値があれば補正値を、無ければ OCR の原値を使う。
/// 表示名は「取決め換算単価」（PDF に書かれた個別単価とは別物）。
/// </summary>
public static class AgreementCalculator
{
    public const int RateDecimals = 12;
    /// <summary>単価の計算結果（諸経費込み単価・取決め換算単価）を丸める単位（10 円単位。例: 1,071.98 → 1,070）</summary>
    public const decimal UnitPriceStep = 10m;
    public const int AmountDecimals = 0;

    public const string OverheadNone = "none";
    public const string OverheadLines = "lines";
    public const string OverheadInput = "input";

    public const string RuleV1 = "TotalRate-v1";
    public const string RuleV2 = "TotalRate-v2";

    public static decimal Round(decimal v, int decimals) => Math.Round(v, decimals, MidpointRounding.AwayFromZero);

    /// <summary>単価を 10 円単位に四捨五入（中間値は 0 から遠い側。1,075 → 1,080）。</summary>
    public static decimal RoundUnitPrice(decimal v) => Math.Round(v / UnitPriceStep, 0, MidpointRounding.AwayFromZero) * UnitPriceStep;

    /// <summary>補正値があれば補正値、無ければ原値。</summary>
    public static (decimal? Quantity, string? Unit, decimal? UnitPrice, decimal? Amount, bool Corrected) Effective(LineView l) =>
        (l.CorrectedQuantity ?? l.Quantity,
         string.IsNullOrWhiteSpace(l.CorrectedUnit) ? l.Unit : l.CorrectedUnit,
         l.CorrectedUnitPrice ?? l.UnitPrice,
         l.CorrectedAmount ?? l.Amount,
         l.CorrectedQuantity is not null || !string.IsNullOrWhiteSpace(l.CorrectedUnit) || l.CorrectedUnitPrice is not null || l.CorrectedAmount is not null);

    /// <summary>計算対象か（未設定なら「金額明細」を対象とする）。</summary>
    public static bool IsTarget(LineView l) => l.IsCalculationTarget ?? l.RecordType == "金額明細";

    /// <summary>人の確認が必要な行か（OCR が要確認・検算不一致・重複候補とした行）。</summary>
    public static bool NeedsReview(LineView l) =>
        l.AmountCheck == "要確認" || !string.IsNullOrWhiteSpace(l.DuplicateCandidate) || !string.IsNullOrWhiteSpace(l.ReviewRequired);

    /// <summary>金額明細の合計（補正後の値）。基準総額「明細合計」の金額（手入力の諸経費があれば加える）。</summary>
    public static decimal DetailTotal(IEnumerable<LineView> lines) =>
        lines.Where(l => l.RecordType == "金額明細").Sum(l => Effective(l).Amount ?? 0m);

    /// <summary>この取決めで使う計算規則の版。</summary>
    public static string RuleVersion(AgreementInput i) => UsesOverhead(i) ? RuleV2 : RuleV1;

    public static bool UsesOverhead(AgreementInput i) => i.OverheadSource is OverheadLines or OverheadInput;

    public static AgreementPreview Compute(AgreementInput input, HeaderView header, IReadOnlyList<LineView> lines, EvidenceInfo evidence)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();

        // ── 紐づけ・原資料 ──
        if (string.IsNullOrWhiteSpace(header.ProjectId)) blocking.Add("物件が紐づけられていません（上の「紐づけ」で物件を選んで保存してください）。");
        if (header.SupplierId is null) blocking.Add("業者が紐づけられていません（上の「紐づけ」で業者を選んで保存してください）。");
        if (!evidence.HasPdf)
        {
            blocking.Add("元PDFが登録されていません。右の「＋ PDFを追加」で元PDFを登録してください（下書き保存はできます）。");
        }
        else
        {
            if (input.EvidenceDocumentId is null) blocking.Add("根拠の PDF を選んでください。");
            else if (!evidence.EvidenceIsPdfOfThisEstimate) blocking.Add("根拠の PDF がこの見積の元PDFではありません。");
            if (input.EvidencePage is null or <= 0) blocking.Add("取決め総額の根拠になる PDF のページ番号を入力してください。");
            else if (evidence.PdfPageCount is int pages && input.EvidencePage > pages)
                blocking.Add($"根拠ページ {input.EvidencePage} は PDF のページ数（{pages} ページ）を超えています。");
        }

        // ── 諸経費 ──
        var overheadIds = input.OverheadSource == OverheadLines ? input.OverheadLineIds.ToHashSet() : new HashSet<int>();
        bool IsOverheadLine(LineView l) => overheadIds.Contains(l.LineId);
        bool Target(LineView l) => !IsOverheadLine(l) && IsTarget(l);
        decimal overhead = 0;
        switch (input.OverheadSource)
        {
            case OverheadLines:
                var unknown = overheadIds.Where(id => lines.All(l => l.LineId != id)).ToList();
                if (unknown.Count > 0) blocking.Add("諸経費に選んだ行が、この見積の明細にありません。画面を再読込してください。");
                if (overheadIds.Count == 0) blocking.Add("諸経費として扱う明細の行を選んでください（無ければ「諸経費なし」か「手入力」にしてください）。");
                foreach (var l in lines.Where(IsOverheadLine))
                {
                    if (Effective(l).Amount is decimal a) overhead += a;
                    else blocking.Add($"諸経費に選んだ {Label(l)} に金額がありません。");
                }
                break;
            case OverheadInput:
                if (input.OverheadAmount is null) blocking.Add("諸経費の金額を入力してください。");
                else if (input.OverheadAmount < 0) blocking.Add("諸経費の金額は 0 以上にしてください。");
                else overhead = input.OverheadAmount.Value;
                break;
        }
        // 内訳合計: 初期値は計算対象の明細（諸経費の行を除く）の合計＝OCR の結果。画面で変えた値があればそれを使う
        var itemsTotalLines = lines.Where(Target).Sum(l => Effective(l).Amount ?? 0m);
        var itemsTotal = UsesOverhead(input) && input.ItemsTotalOverride is decimal itemsOverride ? itemsOverride : itemsTotalLines;
        if (UsesOverhead(input) && itemsTotal != itemsTotalLines)
            warnings.Add($"内訳合計を {Yen(itemsTotal)} 円にしています（計算対象の明細の合計は {Yen(itemsTotalLines)} 円）。");
        decimal? overheadRate = null;
        if (UsesOverhead(input) && overhead != 0)
        {
            if (itemsTotal <= 0) blocking.Add("内訳合計（諸経費を除く計算対象行の合計）が 0 以下のため、諸経費を配分できません。");
            else overheadRate = Round((itemsTotal + overhead) / itemsTotal, RateDecimals);
        }
        var factor = overheadRate ?? 1m;

        // ── 基準総額 ──
        var detailTotal = DetailTotal(lines) + (input.OverheadSource == OverheadInput ? overhead : 0);
        var headerAmount = header.HeaderEstimateAmount;
        var differs = headerAmount is decimal ha && ha != detailTotal;
        switch (input.BaseSource)
        {
            case EstimateDb.BaseFromHeader:
                if (headerAmount is null) blocking.Add("表紙金額が読み取れていないため、「表紙金額」は選べません。明細合計か確認入力を選んでください。");
                else if (input.BaseAmount != headerAmount) blocking.Add($"基準総額が表紙金額（{Yen(headerAmount)}円）と一致しません。");
                break;
            case EstimateDb.BaseFromDetail:
                if (input.BaseAmount != detailTotal) blocking.Add($"基準総額が金額明細の合計{(input.OverheadSource == OverheadInput ? "＋諸経費" : "")}（{Yen(detailTotal)}円）と一致しません。");
                break;
            case EstimateDb.BaseFromInput:
                break;
            default:
                blocking.Add("基準総額の根拠（表紙金額／明細合計／確認入力）を選んでください。");
                break;
        }
        if (input.BaseAmount is null or <= 0m) blocking.Add("基準総額は 0 より大きい金額にしてください（0 では掛率を計算できません）。");
        if ((input.BaseSource == EstimateDb.BaseFromInput || differs) && string.IsNullOrWhiteSpace(input.BaseReason))
        {
            blocking.Add(differs
                ? $"表紙金額（{Yen(headerAmount)}円）と金額明細の合計（{Yen(detailTotal)}円）が異なります。基準総額を選んだ理由を入力してください。"
                : "「確認入力」を選んだ場合は、基準総額の根拠（どこで確認したか）を入力してください。");
        }
        if (overheadRate is not null && input.BaseAmount is decimal bb && bb != itemsTotal + overhead)
            warnings.Add($"基準総額（{Yen(bb)}円）が 内訳合計＋諸経費（{Yen(itemsTotal + overhead)}円）と一致しないため、残差が出ます。");
        if (string.IsNullOrWhiteSpace(input.TaxBasis)) blocking.Add("税区分（税抜／税込）を選んでください。基準総額と取決め総額は同じ税区分にしてください。");
        else if (input.TaxBasis is not (EstimateDb.TaxExcluded or EstimateDb.TaxIncluded)) blocking.Add($"税区分が正しくありません: {input.TaxBasis}");

        // ── 取決め総額・掛率 ──
        decimal? rate = null;
        if (input.AgreedAmount is null) blocking.Add("取決め総額を入力してください。");
        else if (input.AgreedAmount < 0) blocking.Add("取決め総額は 0 以上にしてください。");
        else if (input.AgreedAmount == 0 && !input.ZeroConfirmed) blocking.Add("取決め総額が 0 円です。0 円で確定する場合は「0 円で確定する」にチェックしてください。");
        if (input.AgreedAmount is decimal agreed && agreed >= 0 && input.BaseAmount is decimal b && b > 0)
        {
            rate = Round(agreed / b, RateDecimals);
            if (rate > 1)
            {
                warnings.Add($"掛率が {Percent(rate)} で、見積より増額です。");
                if (string.IsNullOrWhiteSpace(input.IncreaseReason)) blocking.Add("掛率が 100% を超えています（増額）。増額の理由を入力してください。");
            }
        }

        // ── 明細の換算 ──
        var preview = new List<PreviewLine>();
        var needReview = new List<string>();
        var missingAmount = new List<string>();
        var noUnitPrice = 0;
        var roundingGap = 0;
        decimal total = 0;
        foreach (var l in lines)
        {
            var e = Effective(l);
            var isOverhead = IsOverheadLine(l);
            var target = Target(l);
            decimal? cup = null, cam = null, oup = null, oam = null;
            if (target)
            {
                if (e.Amount is null) missingAmount.Add(Label(l));
                if (NeedsReview(l) && l.HumanReviewStatus != EstimateDb.ReviewDone) needReview.Add(Label(l));
                if (overheadRate is decimal f)
                {
                    oup = e.UnitPrice is decimal u ? RoundUnitPrice(u * f) : null;
                    oam = e.Amount is decimal a ? Round(a * f, AmountDecimals) : null;
                }
                if (rate is decimal r)
                {
                    cup = e.UnitPrice is decimal up ? RoundUnitPrice(up * factor * r) : null;
                    cam = e.Amount is decimal am ? Round(am * factor * r, AmountDecimals) : null;
                    total += cam ?? 0;
                    if (e.UnitPrice is null && e.Amount is not null) noUnitPrice++;
                    if (cup is decimal c && e.Quantity is decimal q && cam is decimal x && Round(q * c, AmountDecimals) != x) roundingGap++;
                }
            }
            var reason = target ? null
                : isOverhead ? "諸経費（各明細の単価へ配分）"
                : l.ExclusionReason ?? "計算対象外";
            preview.Add(new PreviewLine(l.LineId, l.Order, l.RecordType, l.ItemName, l.MaterialSpec, l.DimensionSpec,
                e.Quantity, e.Unit, e.UnitPrice, e.Amount, target, reason, cup, cam, e.Corrected)
            {
                IsOverhead = isOverhead,
                OverheadIncludedUnitPrice = oup,
                OverheadIncludedAmount = oam,
            });
        }

        var targetCount = preview.Count(p => p.IsTarget);
        if (targetCount == 0) blocking.Add("計算対象の明細がありません。");
        if (missingAmount.Count > 0)
            blocking.Add($"計算対象の明細に金額が無い行があります（金額を補正するか、計算対象外にしてください）: {Join(missingAmount)}");
        if (needReview.Count > 0)
            blocking.Add($"要確認・検算不一致・重複候補の明細が {needReview.Count} 行、未確認のままです（「確認済」にするか計算対象外にしてください）: {Join(needReview)}");
        if (noUnitPrice > 0) warnings.Add($"{noUnitPrice} 行は単価が無いため、金額だけを換算しました（取決め換算単価は空欄。単価比較には使いません）。");
        if (roundingGap > 0) warnings.Add($"{roundingGap} 行で「数量 × 取決め換算単価」と「換算金額」が丸めの関係で一致しません（換算金額は 金額 × 掛率 で計算しています）。");
        if (overheadRate is not null)
            warnings.Add($"諸経費 {Yen(overhead)} 円を各明細の単価へ配分しました（諸経費掛率 {Percent(overheadRate)}）。換算単価は 単価 × 諸経費掛率 × 掛率 です。");

        decimal? residual = null;
        if (rate is not null && input.AgreedAmount is decimal ag)
        {
            residual = ag - total;
            if (residual != 0)
            {
                warnings.Add($"取決め総額と換算金額の合計に {Yen(residual)} 円の差（残差）があります。明細へは配賦せず、残差として保存します。");
                if (string.IsNullOrWhiteSpace(input.ResidualReason))
                    blocking.Add($"残差が {Yen(residual)} 円あります（端数・税・値引き・明細の欠落など）。理由を入力してください。");
            }
        }

        return new AgreementPreview(rate, rate is null ? null : total, residual, input.BaseAmount, input.AgreedAmount,
            targetCount, preview.Count - targetCount, preview, blocking, warnings)
        {
            ItemsTotal = itemsTotal,
            OverheadAmount = UsesOverhead(input) ? overhead : null,
            OverheadRate = overheadRate,
        };
    }

    // ── メモの組み立て（残差・増額の理由はメモに見出し付きで保存する）──

    private const string ResidualTag = "【残差の理由】";
    private const string IncreaseTag = "【増額の理由】";

    public static string? ComposeNotes(string? notes, string? residualReason, string? increaseReason)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes.Trim());
        if (!string.IsNullOrWhiteSpace(residualReason)) parts.Add(ResidualTag + residualReason.Trim());
        if (!string.IsNullOrWhiteSpace(increaseReason)) parts.Add(IncreaseTag + increaseReason.Trim());
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    public static (string? Notes, string? ResidualReason, string? IncreaseReason) SplitNotes(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return (null, null, null);
        string? residual = null, increase = null;
        var rest = new List<string>();
        foreach (var line in stored.Split('\n'))
        {
            if (line.StartsWith(ResidualTag, StringComparison.Ordinal)) residual = line[ResidualTag.Length..];
            else if (line.StartsWith(IncreaseTag, StringComparison.Ordinal)) increase = line[IncreaseTag.Length..];
            else rest.Add(line);
        }
        return (rest.Count == 0 ? null : string.Join("\n", rest), residual, increase);
    }

    /// <summary>諸経費の明細 ID の保存形式（"12,15"）。</summary>
    public static string? JoinIds(IEnumerable<int> ids)
    {
        var list = ids.Distinct().OrderBy(x => x).ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    public static List<int> SplitIds(string? stored) =>
        (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null).OfType<int>().ToList();

    private static string Label(LineView l) => $"{l.Order}行目「{(l.ItemName is { Length: > 15 } n ? n[..15] + "…" : l.ItemName)}」";

    private static string Join(List<string> items) => string.Join("、", items.Take(8)) + (items.Count > 8 ? $" ほか{items.Count - 8}行" : "");

    private static string Yen(decimal? v) => v?.ToString("#,##0.####", CultureInfo.InvariantCulture) ?? "—";

    public static string Percent(decimal? rate) => rate is decimal r ? (r * 100).ToString("0.##########", CultureInfo.InvariantCulture) + "%" : "—";
}
