// このファイルの目的:
//   設定に入ったCSVパスから、余分な引用符と空白を取り除きます。
//
// なぜ必要か:
//   Windowsのエクスプローラーで「パスのコピー」を使うと、
//   前後に " が付いた 　"C:\...\建材情報.csv"　 という文字列がコピーされます。
//   これをそのまま設定へ貼ると、引用符まで含めたファイル名として扱われ、
//   ファイルが見つからず「検索しても常に0件」という分かりにくい症状になります。
//   実際に会社PCでこの状態が発生しました。
//
//   利用者に「引用符を消してください」と要求するより、受け取る側で
//   吸収するほうが確実です。参照ボタンから選んだ場合は元から引用符が付かないため、
//   この処理は何も変えません。
//
// 修正時の影響:
//   読み込み時と保存時の両方で通しています。片方だけにすると、
//   既に保存済みの壊れた値が直らない、または保存のたびに壊れるようになります。

namespace Bui.Configuration;

/// <summary>設定に入力されたパス文字列を、そのまま使える形へ整えます。</summary>
public static class CsvPathNormalizer
{
    /// <summary>前後の空白と引用符を取り除きます。中身が空になる場合は空文字を返します。</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var trimmed = path.Trim();

        // 「"C:\...\建材情報.csv"」のように前後を引用符で囲まれた形だけを外します。
        // 途中に含まれる引用符は、意図した文字の可能性があるため触りません。
        while (trimmed.Length >= 2
            && (trimmed[0] == '"' || trimmed[0] == '\'')
            && trimmed[^1] == trimmed[0])
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    /// <summary>任意指定のパス用。値が無い場合はnullのままにします。</summary>
    public static string? NormalizeOptional(string? path)
    {
        var normalized = Normalize(path);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>データ設定に含まれる全てのCSVパスを整えます。</summary>
    public static DataSettings Normalize(DataSettings data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data with
        {
            // フォルダパスも「パスのコピー」で引用符が付きます。ファイルと同じ扱いにします。
            DataFolderPath = Normalize(data.DataFolderPath),
            PriceCsvPath = Normalize(data.PriceCsvPath),
            AssemblyCsvPath = Normalize(data.AssemblyCsvPath),
            IdAliasCsvPath = NormalizeOptional(data.IdAliasCsvPath),
            SynonymCsvPath = NormalizeOptional(data.SynonymCsvPath)
        };
    }
}
