// このファイルの目的:
//   「SQL Serverを使うか」1つの値で、2つのラジオボタンを排他にします。
//   専用の列挙を足すより、既存のbool 1つで済ませます。

using System.Globalization;
using System.Windows.Data;

namespace Bui.UI;

/// <summary>真偽を反転します。ラジオボタンの排他に使います。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}
