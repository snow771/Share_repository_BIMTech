// このファイルは、画面に値の変更を通知する共通基底クラスです。
// .NET標準のINotifyPropertyChangedのみに依存し、変更すると全ViewModelのWPF表示更新に影響します。
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Bui.Common;

/// <summary>同じ値の二重通知を避けながら、バインド先へ変更を伝えます。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
