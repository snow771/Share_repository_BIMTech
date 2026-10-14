namespace SimilaritySearchApp.Models;

/// <summary>アプリ設定（%AppData%\SimilaritySearchApp\settings.json に保存）。</summary>
public sealed class AppSettings
{
    public DbSettings Db { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
    public StencilSettings Stencil { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
}

/// <summary>見積AI-OCR（Azure AI Document Intelligence）の接続先・利用枚数の上限・前回使った値。</summary>
public sealed class OcrSettings
{
    /// <summary>例: https://xxxx.cognitiveservices.azure.com/ 。空なら環境変数 DI_ENDPOINT を使う</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>DPAPI（CurrentUser）で暗号化したキー（Base64）。空なら環境変数 DI_KEY を使う</summary>
    public string EncryptedKey { get; set; } = "";

    /// <summary>1回の OCR に送るページ数（無料枠 F0 は 2 ページまで）</summary>
    public int ChunkPages { get; set; } = 2;

    /// <summary>利用枚数（ページ）の上限と、色を変える目安</summary>
    public int Limit { get; set; } = 500;
    public int WarnAt { get; set; } = 300;
    public int DangerAt { get; set; } = 400;

    /// <summary>上限を数える期間: day（1日）/ month（1か月）</summary>
    public string Period { get; set; } = "day";

    // 前回使った値（次回の初期値）
    public string OutputFolder { get; set; } = "";
    public string CommonRulesPath { get; set; } = "";
    public string SupplierRulesPath { get; set; } = "";
    public bool DoStructure { get; set; } = true;
    public bool KeepRawJson { get; set; } = true;
    public bool KeepRawExcel { get; set; } = true;
    public bool KeepChunks { get; set; }
}

/// <summary>Stencile変換ウィンドウで前回使った値（次回の初期値）。</summary>
public sealed class StencilSettings
{
    public string SourceFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public bool DoProjectInfo { get; set; } = true;
    public bool DoUnitPrice { get; set; } = true;

    /// <summary>空なら自動検出（元データフォルダ内 → 同梱の既定マッピング）</summary>
    public string MappingPath { get; set; } = "";
    public string TemplatePath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
}

/// <summary>SQL Server 接続設定。</summary>
public sealed class DbSettings
{
    public string Server { get; set; } = @"24DECNMO001\SQLEXPRESS";
    public string Database { get; set; } = "Unit_Price_Display_単価表示";

    /// <summary>true: Windows 認証 / false: SQL Server 認証</summary>
    public bool UseWindowsAuth { get; set; } = true;
    public string User { get; set; } = "";

    /// <summary>DPAPI（CurrentUser）で暗号化したパスワード（Base64）。平文は保存しない。</summary>
    public string EncryptedPassword { get; set; } = "";

    /// <summary>自己署名証明書のサーバーを許可する（社内 SQLEXPRESS 向け）。</summary>
    public bool TrustServerCertificate { get; set; } = true;

    public int CommandTimeoutSec { get; set; } = 120;

    public TableNames Tables { get; set; } = new();
}

/// <summary>参照するテーブル名。</summary>
public sealed class TableNames
{
    public string Projects { get; set; } = "ProjectInformation_物件情報";
    public string Assess { get; set; } = "Appraisal_Unit_Price_査定単価";
    public string Revise { get; set; } = "Revised_Unit_Price_修正単価";
    public string Category { get; set; } = "Category_カテゴリマスタ";
    public string IndexAllocation { get; set; } = "Index_Allocation_指数割当マスタ";
    public string Deflator { get; set; } = "Deflator_補正係数";
    public string Term { get; set; } = "Term_期間マスタ";
    public string Index { get; set; } = "Index_指数マスタ";

    /// <summary>建築材料マスタ（標準単価）。読めなくてもアプリは動く（【標準単価】タブだけ使えない）。</summary>
    public string MaterialMaster { get; set; } = "MaterialMaster_建築材料マスタ";

    /// <summary>変更履歴（保存1回分）。無ければアプリが作成する。</summary>
    public string ChangeBatch { get; set; } = "ChangeBatch_変更操作";

    /// <summary>変更履歴（行ごとの変更前・変更後）。無ければアプリが作成する。</summary>
    public string ChangeRow { get; set; } = "ChangeRow_変更明細";
}

/// <summary>ローカル LLM（Ollama）接続設定。</summary>
public sealed class LlmSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "hf.co/Qwen/Qwen3-4B-GGUF:Q4_K_M";
    public int TimeoutSec { get; set; } = 600;
    public double Temperature { get; set; } = 0.2;
    public int NumPredict { get; set; } = 512;

    /// <summary>
    /// コンテキスト長（num_ctx。入力＋出力で扱えるトークン数）。Ollama の既定（2048〜4096）では
    /// AI条件設定の指示文が入りきらないため広げる。0 ならサーバーの既定のまま。
    /// </summary>
    public int NumCtx { get; set; } = 8192;
}
