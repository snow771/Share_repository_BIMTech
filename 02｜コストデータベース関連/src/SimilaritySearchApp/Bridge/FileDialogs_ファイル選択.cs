using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SimilaritySearchApp.Bridge;

/// <summary>画面から呼ぶファイル・フォルダ選択ダイアログ（WPF 標準）。キャンセル時は null。</summary>
public static class FileDialogs
{
    public sealed class PickRequest
    {
        /// <summary>folder / open / save</summary>
        public string Kind { get; set; } = "open";
        public string Title { get; set; } = "";

        /// <summary>例: "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"</summary>
        public string Filter { get; set; } = "すべてのファイル (*.*)|*.*";

        /// <summary>初期表示（ファイルかフォルダのパス）</summary>
        public string? Initial { get; set; }

        public string? DefaultFileName { get; set; }
    }

    public static string? Pick(PickRequest req, Window? owner) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            var initialDir = InitialDirectory(req.Initial);
            switch (req.Kind)
            {
                case "folder":
                {
                    var d = new OpenFolderDialog { Title = req.Title, InitialDirectory = initialDir ?? "" };
                    return Show(d, owner) ? d.FolderName : null;
                }
                case "save":
                {
                    var d = new SaveFileDialog
                    {
                        Title = req.Title, Filter = req.Filter, InitialDirectory = initialDir ?? "",
                        FileName = req.DefaultFileName ?? "", OverwritePrompt = true,
                    };
                    return Show(d, owner) ? d.FileName : null;
                }
                default:
                {
                    var d = new OpenFileDialog { Title = req.Title, Filter = req.Filter, InitialDirectory = initialDir ?? "", CheckFileExists = true };
                    return Show(d, owner) ? d.FileName : null;
                }
            }
        });

    /// <summary>ファイルを複数選ぶ（キャンセル時は空）。</summary>
    public static List<string> PickMany(PickRequest req, Window? owner) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            var d = new OpenFileDialog
            {
                Title = req.Title, Filter = req.Filter, InitialDirectory = InitialDirectory(req.Initial) ?? "",
                CheckFileExists = true, Multiselect = true,
            };
            return Show(d, owner) ? d.FileNames.ToList() : new List<string>();
        });

    private static bool Show(CommonDialog d, Window? owner) =>
        (owner is null ? d.ShowDialog() : d.ShowDialog(owner)) == true;

    private static string? InitialDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path);
        return dir is not null && Directory.Exists(dir) ? dir : null;
    }
}
