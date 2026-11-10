// このファイルは、時間のかかる画面操作の二重実行防止と取消を共通化します。
// CancellationTokenとINotifyPropertyChangedに依存し、変更はすべての検索・点検ボタンの実行制御に影響します。
using System.Windows.Input;

namespace Bui.Common;

/// <summary>Task 11のログ/エラー保護が、async-void境界の例外を安全に受け取る契約です。</summary>
public interface IAsyncCommandErrorHandler
{
    void Handle(Exception exception);
}

/// <summary>一度に1回だけ非同期処理を実行し、現在の処理だけを取消できます。</summary>
public sealed class AsyncCommand : ObservableObject, ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly IAsyncCommandErrorHandler _errorHandler;
    private readonly object _gate = new();
    private CancellationTokenSource? _runningCancellation;
    private bool _isRunning;

    public AsyncCommand(Func<CancellationToken, Task> execute, IAsyncCommandErrorHandler? errorHandler = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _errorHandler = errorHandler ?? IgnoreAsyncCommandErrorHandler.Instance;
        CancelCommand = new CancelCurrentCommand(this);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanExecute));
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool CanExecute(object? parameter) => !IsRunning;

    public event EventHandler? CanExecuteChanged;

    /// <summary>画面が実行中操作だけを取り消すためのボタン用コマンドです。</summary>
    public ICommand CancelCommand { get; }

    /// <summary>WPFボタンからの呼出しを、テスト可能なExecuteAsyncと同じ経路へ渡します。</summary>
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            // ユーザーの取消は障害ではないため、Task 11のエラーログへ渡しません。
        }
        catch (Exception exception)
        {
            try
            {
                _errorHandler.Handle(exception);
            }
            catch
            {
                // ICommand.Executeはasync void境界です。Task 11側のログ出力自体が失敗しても、
                // RevitのUIスレッドへ未処理例外を戻してアプリ全体を停止させません。
            }
        }
    }

    public async Task ExecuteAsync()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_runningCancellation is not null)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _runningCancellation = cancellation;
            IsRunning = true;
        }

        try
        {
            // WPFから呼ばれたときは呼出元コンテキストへ戻し、ボタン状態をUIスレッドで通知します。
            await _execute(cancellation.Token);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_runningCancellation, cancellation))
                {
                    _runningCancellation = null;
                    IsRunning = false;
                }
            }

            cancellation.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            // DisposeとCancelが競合しないよう、所有中のロック内で通知します。
            _runningCancellation?.Cancel();
        }
    }

    private sealed class CancelCurrentCommand(AsyncCommand owner) : ICommand
    {
        public bool CanExecute(object? parameter) => owner.IsRunning;
        public void Execute(object? parameter) => owner.Cancel();
        public event EventHandler? CanExecuteChanged
        {
            add => owner.CanExecuteChanged += value;
            remove => owner.CanExecuteChanged -= value;
        }
    }

    private sealed class IgnoreAsyncCommandErrorHandler : IAsyncCommandErrorHandler
    {
        public static IgnoreAsyncCommandErrorHandler Instance { get; } = new();
        public void Handle(Exception exception) { }
    }
}
