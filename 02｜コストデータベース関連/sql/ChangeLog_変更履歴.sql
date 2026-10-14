-- ChangeLog_変更履歴.sql
-- 変更履歴のテーブルを作成する（存在すれば何もしない）。
-- 通常はアプリが初回の保存時に自動で作成する。利用者に CREATE TABLE の権限が無い場合だけ、管理者が実行する。
-- 内容はアプリの ChangeLogRepository.CreateSql と同じ。

IF OBJECT_ID(N'[dbo].[ChangeBatch_変更操作]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChangeBatch_変更操作] (
        BatchId           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,  -- 操作番号
        ExecutedAt        DATETIME2(3)   NOT NULL,                 -- いつ
        UserName          NVARCHAR(256)  NOT NULL,                 -- だれが（Windows のドメイン\ユーザー名）
        MachineName       NVARCHAR(128)  NULL,                     -- どの PC で
        DbLogin           NVARCHAR(256)  NULL,                     -- DB のログイン名（SUSER_SNAME）
        Source            NVARCHAR(50)   NOT NULL,                 -- DB編集 / Excel取込 / Stencile登録 / 取り消し
        Summary           NVARCHAR(1000) NULL,                     -- 要約
        Tables            NVARCHAR(1000) NULL,                     -- 変更したテーブル
        RowTotal          INT            NOT NULL,                 -- 変更した行数
        RevertsBatchIds   NVARCHAR(1000) NULL,                     -- （取り消しの操作）取り消した操作番号
        RevertedByBatchId INT            NULL                      -- この操作を取り消した操作番号
    );
    CREATE INDEX IX_ChangeBatch_ExecutedAt ON [dbo].[ChangeBatch_変更操作] (ExecutedAt);
END;

IF OBJECT_ID(N'[dbo].[ChangeRow_変更明細]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChangeRow_変更明細] (
        RowId       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BatchId     INT           NOT NULL,                        -- 操作番号
        SeqNo       INT           NOT NULL,                        -- 操作内の順番
        TableSchema NVARCHAR(128) NOT NULL,
        TableName   NVARCHAR(256) NOT NULL,                        -- どのテーブルの
        Operation   NVARCHAR(10)  NOT NULL,                        -- INSERT / UPDATE / DELETE
        BeforeJson  NVARCHAR(MAX) NULL,                            -- 変更前の行（JSON）
        AfterJson   NVARCHAR(MAX) NULL                             -- 変更後の行（JSON）
    );
    CREATE INDEX IX_ChangeRow_Batch ON [dbo].[ChangeRow_変更明細] (BatchId, SeqNo);
END;
