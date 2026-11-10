# SQL Serverをデータ元にする — 設計

日付: 2026-09-08
対象: BUI Revit 2025アドイン（`BUI_Addin_CSharpアドイン`）

## 1. 何を作るか

CSVでの検証が一区切りついたので、同じ動きがSQL Serverでも出るかを確かめる。

| 項目 | 値 |
| --- | --- |
| サーバー名 | `localhost\sqlexpress` |
| 認証 | Windows認証 |
| データベース名 | `GoldStandardDB` |

あわせて、データ元を切り替えるボタンを設け、**使っていない側のファイルを読みに行かない**ようにする。

確認済みの決定事項。

| 論点 | 決定 |
| --- | --- |
| SQL接続時の書き込み | **読み取り専用から始める**。新規登録・マスタ追加は、読み取りの検証が終わってから |
| Assembly_ID（構成データ） | **構成CSVのまま**。DBに構成テーブルが無く、BUIが生成するデータでマスタとは性質が違う |
| 切り替えの動き | **その場で切り替わる**。Revitの再起動は不要 |
| 単価情報 | 対象外（保留のまま） |

## 2. なぜ「表の読み込み口」だけを差し替えるのか

`GoldStandardCsvSet` は11枚の表を**生の行のまま**持つ入れ物で、結合・整形は
`GoldStandardFlattener` 以降が担っている。SQLから同じ形の表を作れば、
その先は1行も変えずに動く。

```
      CSVフォルダ ──┐
                    ├→ 表一式（11枚の生の行）→ 平坦化 → 製品カード・検索・マスタ・候補
      SQL Server ──┘
```

SQL用のリポジトリを別に作る案（`SqlProductRepository` など5つ）は採らない。
CSV版とSQL版で表示が食い違う経路が5本に増え、
**「CSVと同じ動きが出るか」という検証の目的に逆行する**ため。

各リポジトリの中で分岐する案も採らない。判断があちこちに散り、
「SQLなのにここだけCSVを読んでいた」を防げない。

## 3. 構成

### 3.1 表の読み込み口

```csharp
internal interface IGoldStandardTableSource
{
    GoldStandardTableSet Load();   // 11枚の表を生の行のまま返す
    string Describe();             // 画面に出す出どころの説明
    bool SupportsWrite { get; }    // CSVだけ true
    long Version { get; }          // キャッシュの作り直し判定
}
```

| 実装 | 中身 | Version |
| --- | --- | --- |
| `CsvTableSource` | いまの `GoldStandardCsvSet.Load` を移す | フォルダ内CSVの最終更新時刻 |
| `SqlServerTableSource` | 11テーブルを `SELECT * FROM dbo.<表名>` | 「読み直す」ごとに増える連番 |

`GoldStandardCsvSet` は `GoldStandardTableSet` へ改名する。もうCSV専用ではないため。

**値は文字列で持つ。**CSV経路と完全に同じ形にしておけば、その先の平坦化・整形・表示が
1本の経路のままになり、「SQLだと表示が違う」が構造的に起きない。
`NULL` は空文字にする。CSVの空欄と同じ扱いになる。

### 3.2 いま使っている読み込み口

`DataSourceSwitch` が1つだけ持つ。

```
BuiDockPaneViewModel（画面の状態はそのまま）
        ↓ 参照するインターフェイスは変わらない
GoldStandardProductRepository / DetailSource / MasterSource
        ↓ 毎回「いまの読み込み口」を聞く
DataSourceSwitch ──→ CsvTableSource または SqlServerTableSource
```

画面が持つオブジェクトは差し替わらないので、**切り替えても検索結果や層の表は消えない。**
`GoldStandardProductRepository` と `GoldStandardDetailSource` が持っている
「データフォルダのパス」を `DataSourceSwitch` に変える。

### 3.3 SQL接続中にディスクを触るもの

| 触るもの | いつ |
| --- | --- |
| `Assemblies.csv` | Assembly_IDを採番するときだけ |
| 製品画像のファイル | 画像のある製品カードを開いたときだけ |
| 旧新ID対応CSV | 点検タブ（設定されている場合のみ） |
| 設定JSON | 保存・読み込み時 |

製品・属性・マスタのCSVは**一切開かない**。
単価もSQL接続中は空の窓口に差し替え、ファイルを見に行かない。

画像は `Product_Images.file_name` をデータフォルダから解決する。
SQL接続中もデータフォルダの設定が画像の置き場として使われる。
`image_url` を読みに行かない方針は変えない（設計 2026-09-06 §画像）。

## 4. 設定と画面

### 4.1 設定タブ

```
データ元    ( ● ) CSVフォルダ        C:\BUI_PoCData
            (   ) SQL Server

  サーバー名        [ localhost\sqlexpress        ]
  データベース名    [ GoldStandardDB              ]
  認証              Windows認証（固定）

  ［接続を確認］  ［切り替えて読み直す］
```

`DataSettings.Provider` に `"SqlServer"` を追加する。CSVは既存の `"GoldStandard"` のまま。
`DataProviderPolicy.Validate` に `"SqlServer"` を通す。

接続情報は `Settings.Local_個別設定.json` に平文で保存する。
Windows認証なので秘密値は無く、認証情報ファイルには何も増えない。

| 追加する設定 | 例 |
| --- | --- |
| `SqlServerName` | `localhost\sqlexpress` |
| `SqlDatabaseName` | `GoldStandardDB` |

**接続文字列は利用者に書かせない。**サーバー名とDB名から組み立てる。

```
Server=<サーバー名>;Database=<DB名>;Integrated Security=True;
TrustServerCertificate=True;Connect Timeout=5;Application Name=BUI
```

### 4.2 「接続を確認」

失敗したら**理由をそのまま出す**。SQL Serverが返した原文も併記する。

| エラー番号 | 画面に出す文 |
| --- | --- |
| 2 / 53 / 258 | SQL Serverに接続できませんでした。インスタンス名が正しいか、SQL Server Express が起動しているかを確認してください。 |
| 4060 | データベース「<DB名>」を開けません。名前が正しいか、このWindowsユーザーに参照権限があるかを確認してください。 |
| 18456 | このWindowsユーザーではログインできません。データベースへの参照権限を確認してください。 |
| その他 | SQL Serverへの接続に失敗しました。 |

成功したら件数を出す。

> 接続できました。製品 1,204件 / メーカー 87件 / 属性 15,302件

サーバー名とDB名は利用者が入力した値なので、画面に出してよい。
接続文字列そのものは出さない。

### 4.3 読み取り専用の見せ方

| 対象 | SQL接続中 |
| --- | --- |
| 検索・絞り込み・比較・概要カード・点検 | そのまま動く |
| Revitへの Product_ID 付与 | そのまま動く |
| Assembly_ID の採番 | そのまま動く（書き先は構成CSV） |
| 候補が無いので新規登録 | **押せない** |
| 一覧に無いので新しく登録する | **チェックできない** |

新規登録の枠に常に出す。

> SQL接続中は製品を登録できません。設定タブで「CSVフォルダ」に切り替えてください。

押せないボタンを黙って置かない。なぜ押せないかと、どうすれば押せるかを必ず添える。

画面側の見た目だけに頼らず、`GoldStandardProductRepository.UpsertAsync` と
`AddValueAsync` も `SupportsWrite == false` なら**書く前に**断る。
画面の不具合で押せてしまっても、DBやCSVには何も起きない。

## 5. 技術的なリスク

**`Microsoft.Data.SqlClient` がRevitの中で動くかは、こちらでは確かめられない。**

Revit 2025 はアドインを自分のプロセスへ読み込む。このパッケージは `Azure.Identity` など
10個ほどの依存DLLを連れてくるため、Revitが既に読み込んでいる別バージョンとぶつかり得る。
私のPCにはRevitが無く、Revit依存コードをコンパイルすらできない。

ぶつかっても被害が出ない置き方にする。

1. **起動経路からSQLを外す。**パッケージに触れるのは利用者が「SQL Server」へ
   切り替えたときだけ。読み込みに失敗してもBUIパネル自体は今までどおり開く
2. 最初のSQL呼び出しを包み、アセンブリの読み込み失敗を**落ちる代わりに読める文言**にする
3. **配置診断に追加する。**SQL用のDLLが配置先フォルダに実際に届いているかを確認できるようにする

ぶつかった場合の逃げ道は ODBC 経由（依存が1つだけ）だが、**先には作らない。**
ぶつかると決まってから作る。

## 6. 異常時の扱い

| 状況 | 動作 |
| --- | --- |
| SQLに接続できない | 理由と対処を画面へ。データ元は切り替えず、CSVのまま動き続ける |
| 表が1枚欠けている | CSVと同じく空として扱う。`Products` だけは必須 |
| SQL用DLLを読み込めない | 「SQL接続に必要な部品を読み込めませんでした」と出し、CSVのまま動き続ける |
| 切り替え中に例外 | 元の読み込み口へ戻す。中途半端な状態にしない |
| SQL接続中に登録を試みた | 書く前に断り、CSVへ切り替える案内を出す |

## 7. 検証

`Bui.Shared` に入る部分は単体テストで確認できる。

1. SQLの行 → 表一式への変換（NULL→空文字、列名の保持、表が欠けた場合）
2. 切り替えで、使わない側の読み込み口に一切触れないこと
3. SQL接続中に新規登録・マスタ追加が書く前に断られること
4. SQL Serverのエラー番号（2 / 53 / 4060 / 18456）が日本語の対処文になること
5. 切り替えても画面の状態（検索結果・層の表・保留中のプレビュー）が消えないこと
6. 接続に失敗したときにデータ元が切り替わらないこと

**Revitまたは実DBが必要で、こちらでは確認できないもの**（会社PCでの確認事項）。

- `Microsoft.Data.SqlClient` がRevitの中で動くこと
- 実際の `GoldStandardDB` に接続でき、CSVと**同じ表示**が出ること
- 11テーブルの読み込みにかかる時間
- 切り替え後にRevitへのID付与が今までどおり動くこと

私のPCにはSQL Serverも無い。接続まわりは会社PCの「接続を確認」ボタンが唯一の検証手段になる。

## 8. 対象外

- SQLへの書き込み（新規登録・マスタ追加）。読み取りの検証が終わってから
- 構成データ（Assembly_ID）のDB化
- 単価情報との連携
- SQL Server認証（Windows認証のみ）
- ODBC経由の代替経路（アセンブリがぶつかると決まってから）
