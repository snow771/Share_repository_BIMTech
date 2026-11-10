# 単価CSV・構成CSVの即時読み直し 設計

## 背景・目的

BUIの設定タブには、データフォルダ（製品CSV/SQL）・単価CSV（Prices.csv）・
構成CSV（Assemblies.csv）という3つの独立したデータソース設定がある。
このうちデータフォルダは「切り替えて読み直す」ボタンを押すとRevitを
再起動せずにその場で反映される（`DataSourceSwitch` による差し替え）。

一方、単価CSVと構成CSVは `BuiDockPaneComposition.Create()`（BUIパネルを
開いたとき）に1回だけ読み込み先パスが確定し、以後変更されない。設定を
保存しても、Revitを完全に再起動するまで反映されない。この非対称な挙動は
2026-09-15、インストーラーの動作確認中に運用上の問題として指摘された
（[[bui-csv-reload-without-restart-todo]]）。

本設計は、単価CSVと構成CSVも「切り替えて読み直す」ボタンで即時反映
されるようにする。

## スコープ

- 対象: 単価CSV（Prices.csv）・構成CSV（Assemblies.csv）の即時読み直し
- 対象外: データフォルダ（製品CSV/SQL）の切り替え — 既存のまま変更しない
- 対象外: 設定の自動保存や、保存ボタンを押しただけでの自動反映（決定A、
  2026-09-15にユーザー承認済み。反映は「切り替えて読み直す」ボタンを
  押したときだけ）

## アーキテクチャ

既存の `DataSourceSwitch`（製品データの出どころを1つの参照として持ち、
`SwitchTo` で中身だけ差し替える）と同じ考え方を、単価CSV・構成CSVにも
適用する。ViewModelへ注入済みのオブジェクトの実体（`SwitchingPriceRepository`、
新設の `SwitchingAssemblyRepository`）は据え置いたまま、内部の読み込み先
パスだけを差し替える。実体を作り直さない理由は `DataSourceSwitch` の
ファイル冒頭コメントと同じ：検索結果・層の表・保留中のプレビューを
消さないため。

## コンポーネント

### 1. `CsvPathResolver`（新規・共有ユーティリティ）

**置き場所:** `Bui.Shared_共有ロジック\Configuration_設定\CsvPathResolver_CSVパス解決.cs`

`BuiDockPaneComposition_画面構成.cs` に現在ある private static メソッド
`ResolveCsvPath` を、そのままpublic staticな共有クラスへ切り出す。

```csharp
namespace Bui.Configuration;

/// <summary>設定欄が空欄のときの既定CSVパスを解決します。起動時と再読み込み時で同じ関数を使い、ズレを防ぎます。</summary>
public static class CsvPathResolver
{
    public static string Resolve(string? configuredPath, string dataRoot, string fallbackFileName) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(dataRoot, fallbackFileName)
            : configuredPath;
}
```

起動時（`BuiDockPaneComposition.Create()`）と、今回追加する再読み込み時
（`RevitDataSourceConnector`）の両方がこの関数を呼ぶ。ロジックの二重管理に
よるズレは、今回のインストーラー作業で繰り返し踏んだ問題（allowlistの
ズレなど）であり、同じ轍を踏まない。

### 2. `ICsvPathSwitchable`（新規・共有インターフェース）

**置き場所:** `Bui.Shared_共有ロジック\Data_データ窓口\ICsvPathSwitchable_CSVパス切替.cs`

```csharp
namespace Bui.Data;

/// <summary>内部で読んでいるCSVのパスを、実体を作り直さずに差し替えられることを表します。</summary>
public interface ICsvPathSwitchable
{
    /// <summary>読み込み先を新しいパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    void SwitchCsvPath(string csvPath);
}
```

### 3. `SwitchingPriceRepository`（既存クラスを拡張）

**変更ファイル:** `Bui.Shared_共有ロジック\Data_データ窓口\SwitchingPriceRepository_単価窓口切替.cs`

- `ICsvPathSwitchable` を実装に追加する。
- コンストラクタへ `PriceColumnMapping mapping` を追加する（**破壊的変更**。
  既存のコンストラクタは `(IPriceRepository csvPrices, DataSourceSwitch source)` の
  2引数だが、`SwitchCsvPath` で新しい `CsvPriceRepository` を組み立てる際に
  列マッピングが必要なため、3引数 `(IPriceRepository csvPrices, DataSourceSwitch source, PriceColumnMapping mapping)`
  にする。受け取った `mapping` は `_mapping` フィールドとして保持し、切り替え時に
  そのまま使い回す（列マッピングは設定タブでは編集できない値のため、
  切り替え時に変える理由がない）。
- 内部の `IPriceRepository _csvPrices` を `volatile` な差し替え可能フィールドにする
  （`DataSourceSwitch._current` と同じ `Volatile.Read`/`Volatile.Write` パターン）。
- `SwitchCsvPath(string csvPath)` は `new CsvPriceRepository(csvPath, _mapping)` を作り、
  `_csvPrices` を差し替える。
- `GetStatusAsync` / `SearchAsync` / `UpsertAsync` の実装（SQL接続中はCSVを読まない
  ガード）は変更しない。

**呼び出し側の追随が必要な箇所（コンストラクタ変更の影響）:**
- `BuiDockPaneComposition_画面構成.cs` — `new SwitchingPriceRepository(new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource)`
  を `new SwitchingPriceRepository(new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource, loaded.Settings.Data.PriceColumns)` に変える
  （同じ `PriceColumns` をもう一度渡すだけ）。
- `tests_テスト\Bui.Tests\Data_データ\ReadOnlyDataSourceTests_読取専用テスト.cs` —
  2箇所で `new SwitchingPriceRepository(csv, ...)` を呼んでいる（53〜55行目、71〜72行目付近）。
  どちらも第3引数に `PriceColumnMapping.PocDefault` を追加する
  （この2つのテストは切り替え機能自体を検証していないため、既定値で問題ない）。

### 4. `SwitchingAssemblyRepository`（新規）

**置き場所:** `Bui.Shared_共有ロジック\Data_データ窓口\SwitchingAssemblyRepository_構成窓口切替.cs`

`SwitchingPriceRepository` と対になる構成CSV版。現状 `CsvAssemblyRepository` を
そのまま直接使っている箇所を、これで包む。

```csharp
namespace Bui.Data;

/// <summary>構成CSVの読み込み先を、実体を作り直さずに切り替えられる窓口です。</summary>
public sealed class SwitchingAssemblyRepository : IAssemblyRepository, ICsvPathSwitchable
{
    private IAssemblyRepository _current;

    public SwitchingAssemblyRepository(IAssemblyRepository initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    public void SwitchCsvPath(string csvPath) =>
        Volatile.Write(ref _current, new CsvAssemblyRepository(csvPath));

    public Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).FindByFingerprintAsync(fingerprint, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).GetAllIdsAsync(cancellationToken);

    public Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).AddAsync(record, fingerprint, cancellationToken);

    public Task RemoveAsync(string assemblyId, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).RemoveAsync(assemblyId, cancellationToken);
}
```

（`_current` はフィールドの型を `IAssemblyRepository` にし、`SwitchCsvPath` 内で
具象型 `CsvAssemblyRepository` を new する。`DataSourceSwitch` がCSV/SQLの両方を
扱うのに対し、こちらはCSV専用なので `ForCsvFolder` のような静的ファクトリは不要。）

### 5. `IDataSourceConnector`（既存インターフェースを拡張）

**変更ファイル:** `Bui.Shared_共有ロジック\UI_画面\IDataSourceConnector_データ元接続.cs`

2メソッドを追加する。

```csharp
/// <summary>単価CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
void UsePriceCsv(string configuredPath);

/// <summary>構成CSVの読み込み先を切り替えます。空欄ならBUI管理下の既定ファイルを使います。</summary>
void UseAssemblyCsv(string configuredPath);
```

### 6. `RevitDataSourceConnector`（既存クラスを拡張・Bui.Addin）

**変更ファイル:** `Bui.Addin\Data_データ\RevitDataSourceConnector_データ元接続.cs`

コンストラクタに3つ追加する。

```csharp
public RevitDataSourceConnector(
    DataSourceSwitch dataSourceSwitch,
    ICsvPathSwitchable priceRepository,
    ICsvPathSwitchable assemblyRepository,
    string dataRoot)
```

`_priceRepository` / `_assemblyRepository` / `_dataRoot` をフィールドに保持し、
新しい2メソッドを実装する。

```csharp
public void UsePriceCsv(string configuredPath) =>
    _priceRepository.SwitchCsvPath(CsvPathResolver.Resolve(configuredPath, _dataRoot, "Prices.csv"));

public void UseAssemblyCsv(string configuredPath) =>
    _assemblyRepository.SwitchCsvPath(CsvPathResolver.Resolve(configuredPath, _dataRoot, "Assemblies.csv"));
```

このクラスはBui.Addin（Revit依存）に属し、私物PCではビルド確認できない
（[[bui-unverifiable-layer-needs-tested-limits]]）。上記のとおり中身は
`CsvPathResolver.Resolve` の呼び出しと `SwitchCsvPath` への委譲だけの薄い
配線にとどめ、判断ロジックは一切持たせない。テストが必要なロジックは
すべてBui.Shared側（`CsvPathResolver`、`SwitchingPriceRepository`、
`SwitchingAssemblyRepository`）に置く。

### 7. `BuiDockPaneComposition.Create()`（既存メソッドを変更・Bui.Addin）

**変更ファイル:** `Bui.Addin\UI_画面\BuiDockPaneComposition_画面構成.cs`

- `pricePath` / `assemblyPath` の解決を、private static `ResolveCsvPath` の
  呼び出しから `CsvPathResolver.Resolve` の呼び出しへ変える。private static
  `ResolveCsvPath` メソッド自体は削除する（重複コードを残さない）。
- `assemblies` の組み立てを `new CsvAssemblyRepository(assemblyPath)` から
  `new SwitchingAssemblyRepository(new CsvAssemblyRepository(assemblyPath))` に変える。
  `identifierWorkflow` など、`assemblies` を受け取る側の型は `IAssemblyRepository`
  のままなので、他の呼び出し箇所の変更は不要。
- `new RevitDataSourceConnector(dataSource)` の呼び出しを
  `new RevitDataSourceConnector(dataSource, prices, assemblies, dataRoot)` に変える
  （`prices` は既存の `SwitchingPriceRepository` インスタンス、`assemblies` は
  上で作った `SwitchingAssemblyRepository` インスタンス、`dataRoot` は
  既存の変数をそのまま渡す）。

### 8. `SwitchDataSourceAsync`（既存メソッドを変更・Bui.Shared）

**変更ファイル:** `Bui.Shared_共有ロジック\UI_画面\BuiDockPaneViewModel_パネル状態.cs`

`_dataSourceConnector` の null チェックの直後、CSV/SQLの分岐より前に、
単価CSV・構成CSVの切り替えを無条件で行う（どちらのモードでも構成CSVは
Assembly_IDの書き込み先として使われるため）。

```csharp
_dataSourceConnector.UsePriceCsv(PriceCsvPath ?? string.Empty);
_dataSourceConnector.UseAssemblyCsv(AssemblyCsvPath ?? string.Empty);
```

CSV分岐（`if (!UseSqlServer)`）内にある、次の2つを変更する。

- `adoptedFolder` の直前コメント（941〜943行目、「単価CSV・構成CSVの
  リポジトリは起動時から作られたままで…」という現状の制約を説明した
  コメント）は事実でなくなるため削除する。
- 完了メッセージから、次の一文を削除する（もう成立しないため）。
  `" 単価CSVと構成CSVは保存済みの設定のままです。反映するには「保存」を押してからRevitを再起動してください。"`
  この一文を作っていた `savedFolder` との比較 `if` ブロックも合わせて削除する。

SQL分岐は変更しない（単価CSV・構成CSVの切り替えは分岐の外で既に行われている）。

## エラー処理

- 切り替え先のCSVが存在しない・列が壊れているなどの状態は、
  `CsvPriceRepository` / `CsvAssemblyRepository` が既に持っている「理由付きの
  空データ」を返す設計をそのまま使う。`SwitchCsvPath` 自体は失敗しない
  （ファイルの存在確認はしない。存在しなければ次に読んだときに
  「単価CSVが見つかりません。設定タブのパスを確認してください。」等が
  返る、という既存の挙動のまま）。
- 「切り替えて読み直す」を押した後、単価CSV・構成CSVの状態が壊れていても、
  製品データの切り替え自体は失敗させない（現状どおり、製品側は製品側の
  成否だけで判断する）。

## テスト

すべてBui.Shared側（Bui.Tests、私物PCで実行できる）に置く。

1. **`CsvPathResolver.Resolve`** — 空欄/空白/nullなら `dataRoot + fallbackFileName`
   を返すこと。値が入っていればそのまま返すこと（前後空白を含む場合の
   トリム有無は既存の `ResolveCsvPath` の挙動を踏襲し、変えない）。

2. **`SwitchingPriceRepository`** — 一時フォルダに2つの単価CSV（内容が違う）を
   作り、Aで構築した `SwitchingPriceRepository` に対して `SwitchCsvPath` でBへ
   切り替え、`SearchAsync` がBの内容を返すこと（Aの内容に依存するキャッシュが
   残っていないこと）。CSVモード中に呼ばれることを想定したテストとする
   （SQL接続中に呼ばれた場合の挙動は既存の `IsCsvFolder` ガードのままで、
   本設計では変えない）。

3. **`SwitchingAssemblyRepository`** — 一時フォルダに2つの構成CSV（登録内容が
   違う）を作り、`SwitchCsvPath` で切り替えた後 `GetAllIdsAsync` /
   `FindByFingerprintAsync` が新しい方の内容を返すこと。

4. **`BuiDockPaneViewModel.SwitchDataSourceAsync`**
   （`DataSourceViewModelTests_データ元切替画面テスト.cs` に追加）
   - 「切り替えて読み直す」を押すと、`FakeConnector.UsePriceCsv` /
     `UseAssemblyCsv` が、保存済みの値ではなく**入力欄の現在値**
     （`sut.PriceCsvPath` / `sut.AssemblyCsvPath`）で呼ばれること。
   - CSV分岐・SQL分岐のどちらでも呼ばれること（2パターンとも確認）。
   - 完了メッセージに「再起動」という文言が含まれなくなったこと。
   - このテストを通すため、`FakeConnector`（同ファイル内のprivateテスト
     ダブル）に `IDataSourceConnector` の新2メソッドの実装を追加する
     （呼び出し回数と最後に渡されたパスを記録するフィールドを持たせる）。

## 実装しない項目（YAGNI）

- 単価CSVの列マッピング（`PriceColumnMapping`）を設定タブから編集する機能
  — 現状も編集不可であり、本設計はそれを変えない。
- 「切り替えて読み直す」を押さなくても自動で反映される仕組み（決定Aで
  却下済み）。
- 単価CSV・構成CSV単体だけを読み直す専用ボタン（決定Aで却下済み。既存の
  「切り替えて読み直す」ボタン1つに統一する）。
