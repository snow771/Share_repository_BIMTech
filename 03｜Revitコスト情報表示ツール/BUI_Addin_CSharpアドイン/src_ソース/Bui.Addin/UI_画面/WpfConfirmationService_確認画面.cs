// このファイルは、ID書込み前の明示確認を標準WPFダイアログで受け取ります。
// IUserConfirmationServiceとWPFに依存し、ボタンの意味を変えるとモデル書込みの安全契約に影響します。
using System.Windows;

namespace Bui.UI;

public sealed class WpfConfirmationService : IUserConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }
}
