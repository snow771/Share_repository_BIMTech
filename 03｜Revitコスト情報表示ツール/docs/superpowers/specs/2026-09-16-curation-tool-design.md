# 仮登録レビューツール（Bui.Curation） 設計

## 背景・目的

BUIには既に「候補が無いので新規登録」機能があり、現場の利用者がCSVに実在しない建材を、仮ID（`NewProductIdPrefix` + `NewProductIdDigits`、例: `BM_NEW-000001`）付きでその場のProducts.csvへ登録できる。これはRevitへ即座にIDを反映するための、意図的に軽量な登録経路である。

一方、仮登録されたデータは次の作業が別途必要になる。

- 仮IDから、正式な採番ルールに基づくProduct_IDへの書き換え
- 不足している項目（メーカー、性能値など）の加筆
- 内容が整った状態でのSQL Server（GoldStandardDB）への登録

現場のBUI利用者はそれぞれローカルの独立したCSVフォルダを持っており（集約の仕組みは無い）、この整理・登録作業は管理担当者1名（またはごく少数）が、複数の現場フォルダから仮登録データをまとめて定期的にレビューする形で行う。

本設計は、この作業のための新しいツール（Bui.Curation）を作る。

## スコープ

- 対象: 複数フォルダからの仮登録データの取込、一覧レビュー、不足項目の加筆、正式なProduct_IDへの書き換え、SQL Serverへの直接登録、旧新ID CSVへの追記
- 対象外: 現場のCSVフォルダを自動的に集約する仕組み（共有ストレージ化など）。今回は管理者がフォルダを都度指定して取り込む
- 対象外: 正式なProduct_IDの自動採番。決まったルールが無いため、管理者が別途確認して手入力する（重複チェックはツールが行う）
- 対象外: 複数の管理担当者が同時に同じレビューを行う場合の排他制御（利用者は現状1名を想定）

## アーキテクチャ

新しいWPFデスクトップアプリ `Bui.Curation`（Revit非依存）を、`Bui.Addin` と同列の新規プロジェクトとして追加する。`Bui.Shared_共有ロジック` は現状どおりプロジェクトファイルを持たない「ソースリンクだけのフォルダ」であり（`Bui.Addin`・`Bui.Tests` がそれぞれ `<Compile Include="..\Bui.Shared_共有ロジック\**\*.cs">` で取り込む形）、`Bui.Curation` も同じ方式でBui.Sharedのソースを取り込む。

Revit APIへの依存が無いため、このプロジェクト全体は私物PCでもビルド・実行して確認できる（今回のBUI本体の作業の多くとは違い、「未検証」のまま会社PCへ渡す必要がない）。

SQL Server接続コードの置き場所も、既存の層分けに合わせる。`SqlConnectionStringFactory`（接続文字列の組み立てだけ、パッケージ依存なし）はBui.Shared側にあり、そのまま再利用する。一方、`Microsoft.Data.SqlClient` を実際に使うコード（`SqlClientTableReader_SQL接続.cs`が良い先例）はBui.Addin側にあり、Bui.Sharedへパッケージ依存を持ち込まない方針を守っている。`Bui.Curation`も同じ方針を踏襲し、SQLクライアントを直接使うコード（`CurationDbWriter`）はBui.Curation側に置く。

作業用データ（レビュー待ち一覧の状態）は、SQLiteなど新しい依存を持ち込まず、**ローカルのJSONファイル1つ**（`%LOCALAPPDATA%\StartsCAM\BUI\Curation\queue.json` を既定とする）で管理する。BUI全体の「CSV/JSON中心、依存を増やさない」という方針に合わせる。

## データフロー

1. **取込**: 管理者が現場のデータフォルダを1つ以上指定する。各フォルダのProducts.csv（および関連するProduct_Specs.csv等）を読み、設定済みの仮ID接頭辞（`NewProductIdPrefix`）で始まるProduct_IDの行だけを拾う。取込結果は、ツール専用の作業用データ（`queue.json`）へ追加する。**元のCSVには一切書き込まない。**（出どころフォルダ＋元の仮ID）の組を一意キーとし、既に取り込み済み・完了済みのものは再度取り込まない。
2. **一覧・レビュー**: 取込んだ仮登録データを表形式で一覧表示する（出どころフォルダ、仮ID、製品名、不足項目の有無を列に出す）。行を選ぶと詳細編集画面が開き、BUIの新規登録フォーム（`NewProductDraft`・`NewProductColumns`・`NewProductRequiredColumns`）に準じた項目の加筆と、正式なProduct_IDの手入力を行う。同じ詳細画面に、この製品に関連する他の表（Product_Specs、Product_Images）の状況を並べたチェックリストを表示する（該当行が何件あるか。0件でもエラーにはしない）。
（Sourcesは各Product_Specs行が個別に参照する間接的な関係で、Product_Variantsは現状どのコードからも直接参照されておらずproduct_idでの結び付き方が実機で未確認のため、今回はこの2表をチェックリストの対象から外す。将来これらの関係が固まった時点で追加できる。）あわせて、Products.csv上のメーカー名・建材分類の値が、Manufacturers.csv・Material_Categories.csvのマスタに実在するかどうかも確認し、無ければ警告として表示する。**これらはすべて情報表示であり、登録そのものはブロックしない。**判断は管理者に委ねる。
3. **重複チェック**: 入力された正式なProduct_IDが、(a) 現在のSQL Server上のProductsテーブル、(b) この作業セッション内でまだSQL未登録の他の行、のどちらとも重複しないことを確認する。重複していれば登録前に止める。
4. **DB登録**: レビューが済んだ行を選んで「登録」を押すと、SQL Serverへ直接接続し、1件を1つのトランザクションとして、該当する複数テーブル（Products、Product_Specsなど）へUPSERTする。成功したら、続けて「旧新ID CSV」（`old_id,new_id` 形式、`IdAliasCsvPath`）へ1行追記する。既存のold_idと重複する場合は追記せず、その行を「要確認」として残す（`CsvIdAliasLoader`が壊れたCSVを読んだ際に丸ごと失敗する設計のため、追記前に慎重な検証が必要）。登録が終わった行は`queue.json`上で「完了」とし、同じフォルダを後で再取込しても再表示しない。

## コンポーネント

### 1. `ProvisionalRecordScanner`（新規・Bui.Shared、テスト可能）

指定フォルダのProducts.csvから、`NewProductIdPrefix`に一致する行を抽出し、関連するProduct_Specs.csv等をProduct_IDで突き合わせて1件分のドラフト（列名→値の辞書、`NewProductDraft`と同じ形）を組み立てる。関連CSVが読めない・欠けている場合は、その旨を行ごとの警告として残し、処理を止めない。

### 2. `CurationQueueItem` / `CurationQueueStore`（新規・Bui.Shared、テスト可能）

`CurationQueueItem` は1件の仮登録データを表すレコード（出どころフォルダ、元の仮ID、現在のドラフト値、状態＝Pending/Completed、割り当てた正式ID、警告メッセージの一覧）。状態はPending/Completedの2値のみとし、「要確認」はPendingのまま警告メッセージ一覧に理由を積む形で表現する（別の状態値を増やさない）。`CurationQueueStore` は `queue.json` への読み書きを行う。同じ（出どころフォルダ＋元の仮ID）を二重に追加しない。

### 3. `RelatedTableChecker`（新規・Bui.Shared、テスト可能）

1件のドラフト（Product_ID、メーカー名、建材分類などを含む）を受け取り、次の情報を組み立てる純粋関数。DB接続は持たず、呼び出し側が渡したテーブルデータに対して突き合わせるだけにする。

- Product_Specs・Product_Imagesのうち、このProduct_IDに一致する行（`product_id`列）が何件あるか（0件でも警告にはしない。単なる件数表示）。
- メーカー名がManufacturers.csvの実在値と一致するか。一致しなければ警告文字列を1件返す。
- 建材分類がMaterial_Categories.csvの実在値と一致するか。一致しなければ警告文字列を1件返す。

戻り値はすべて表示用の情報（件数・警告文の一覧）であり、登録の可否には使わない。

### 4. `OfficialIdValidator`（新規・Bui.Shared、テスト可能）

入力された正式Product_IDが、渡されたSQL上の既存ID一覧・キュー内の他のドラフトの正式ID一覧のいずれとも重複しないことを確認する純粋関数。DB接続そのものは持たない（呼び出し側が現在のID一覧を渡す）。

### 5. `OldNewIdAliasWriter`（新規・Bui.Shared、テスト可能）

`old_id,new_id` のCSVへ1行追記する。追記前に既存ファイルを読み、同じold_idが無いことを確認する（あれば追記せず失敗を返す）。ファイルが存在しない場合はヘッダー付きで新規作成する。UTF-8（BOM付き）で書く（既存のCSV群と同じ規約）。

### 6. `CurationDbWriter`（新規・Bui.Curation、目視確認）

`Microsoft.Data.SqlClient` を使い、1件分のドラフトをProducts・Product_Specs等の複数テーブルへ、1トランザクションでUPSERTする。SQL文とパラメータの組み立て部分は、実行から分離してテスト可能な形にする（文字列としてのSQLを検証できるようにする。実際の接続・実行はテストしない）。

### 7. `Bui.Curation`（新規・WPF UI）

一覧画面・詳細編集画面。BUI本体の設計と同様、ViewModelをViewから分離し、ロジック部分（一覧のフィルタリング・ボタンの有効/無効判定など）はテスト可能にする。

## エラー処理

- 取込時: 関連CSVが読めない行は「要確認」として一覧に残し、全体の取込は止めない。
- 正式ID重複: 登録前に検出し、理由を表示して登録させない。
- DB接続失敗: その回の登録を行わず、理由を画面に表示する。1件はトランザクションでまとめるため、一部テーブルだけ書き込まれた状態を残さない。
- 旧新ID CSV追記の失敗（重複old_id等）: DB登録自体は成功済みとして扱い（後戻りさせない）、キュー上の状態はCompletedとしたうえで、「旧新ID CSVへの追記に失敗しました（理由）」という警告をその行に残す。一覧上で警告付きのCompleted行として見分けられるようにする。

## テスト

`ProvisionalRecordScanner`・`CurationQueueStore`・`RelatedTableChecker`・`OfficialIdValidator`・`OldNewIdAliasWriter` は、すべてBui.Shared側の純粋なロジックとしてBui.Tests（私物PCで実行可能）で検証する。`CurationDbWriter` はSQL文組み立て部分だけをテストし、実際のSQL Server接続・実行とWPFのUI部分は目視確認とする。

## 実装しない項目（YAGNI）

- 現場CSVフォルダの自動集約・同期の仕組み
- 正式Product_IDの自動採番
- 複数管理者による同時レビューの排他制御
- 仮登録データの却下・削除機能（今回はレビュー→登録の一方向のみ。却下が必要になった場合は別途検討する）
