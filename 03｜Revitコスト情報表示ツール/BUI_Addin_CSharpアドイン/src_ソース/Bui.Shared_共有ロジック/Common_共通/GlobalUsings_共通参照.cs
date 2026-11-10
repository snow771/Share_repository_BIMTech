// このファイルの目的:
//   BUI全体で使う .NET 標準の名前空間を、実ソースファイルとして明示的に宣言します。
//
// なぜ必要か:
//   csproj の ImplicitUsings が生成する global using は、WPF の XAML を
//   コンパイルする一時プロジェクト（Bui.Addin_xxxxx_wpftmp）へ引き継がれません。
//   その状態で Revit の C++/CLI 製DLLを参照すると、DLLがグローバル名前空間へ
//   持ち込む internal な IOException などを掴んでしまい、
//   「'Path' という名前は存在しません」「'IOException' はアクセスできない保護レベル」
//   というビルドエラーが大量に発生します。
//
// 修正時の影響:
//   このファイルは Bui.Addin と Bui.Tests の両方へソースリンクされます。
//   削除すると上記のビルドエラーが再発します。行を減らす場合は、
//   会社PCでアドイン本体のビルドが通ることを必ず確認してください。

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
