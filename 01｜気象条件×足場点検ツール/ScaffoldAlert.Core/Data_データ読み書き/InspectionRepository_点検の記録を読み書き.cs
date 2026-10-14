using Microsoft.Data.SqlClient;

namespace ScaffoldAlert.Core.Data;

/// <summary>対応するアラート1件の中身。</summary>
public class AlertDetail
{
    public int AlertId { get; set; }
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ManagerName { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public decimal? TriggerValue { get; set; }
    public string? TriggerText { get; set; }
    public string AlertStatus { get; set; } = string.Empty;
    public string? LegalBasis { get; set; }
    public DateTime? InstructedAt { get; set; }
    public string? InstructedBy { get; set; }
    public string? InstructionNote { get; set; }

    /// <summary>
    /// まだ対応の入力ができる状態か。
    ///
    /// 点検完了・対象外になっているものには入力欄を出さない。
    /// 出してしまうと、対応済みのアラートに点検記録をもう一度
    /// 登録でき、点検記録簿に同じ事象が二重に並んでしまう。
    /// </summary>
    public bool CanRespond
        => AlertStatus == "OPEN" || AlertStatus == "INSTRUCTED";
}

/// <summary>点検記録簿の1行。臨検にそのまま出せる形。</summary>
public class AuditRow
{
    /// <summary>
    /// どのアラートの記録か。
    ///
    /// 詳細のモーダルと編集画面は、これが無いと「どの記録か」を
    /// 指せない。表に出す必要はないが、行に持たせておく。
    /// </summary>
    public int AlertId { get; set; }

    /// <summary>どの現場か。</summary>
    public int SiteId { get; set; }

    public string SiteName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public decimal? TriggerValue { get; set; }
    public string? TriggerText { get; set; }
    public string? LegalBasis { get; set; }
    public DateTime? InspectedAt { get; set; }
    public string? InspectorName { get; set; }
    public string? InspectorQualify { get; set; }
    public string? InspectionResult { get; set; }
    public string? FindingNote { get; set; }
    public int? DaysToInspection { get; set; }
    public string AlertStatus { get; set; } = string.Empty;

    /// <summary>指示した日時。指示していなければ空。</summary>
    public DateTime? InstructedAt { get; set; }

    /// <summary>指示した人。対象外にしたときは、判断した人。</summary>
    public string? InstructedBy { get; set; }

    /// <summary>
    /// 指示の内容。対象外にしたときは、その理由。
    ///
    /// 対象外の理由は「[対象外] 」を頭に付けて、
    /// 同じ欄に入れている (T_Alert.InstructionNote)。
    /// </summary>
    public string? InstructionNote { get; set; }

    /// <summary>足場の種類。</summary>
    public string? ScaffoldType { get; set; }

    /// <summary>点検記録を登録した人。</summary>
    public string? RegisteredBy { get; set; }

    /// <summary>点検記録を登録した日時。</summary>
    public DateTime? InspectionCreatedAt { get; set; }

    /// <summary>点検記録を最後に直した日時。</summary>
    public DateTime? InspectionUpdatedAt { get; set; }

    /// <summary>
    /// 点検記録が、登録のあとで直されているか。
    ///
    /// ★画面に必ず出す
    ///   臨検に出す記録なので、こっそり書き換わるのは良くない。
    ///   「あとから直した」こと自体は運用上ふつうにあるが、
    ///   分からないまま出すのは別の話。
    ///
    /// 1分の差は、登録したときの誤差として見逃す。
    /// </summary>
    public bool InspectionEdited
        => InspectionCreatedAt is not null
           && InspectionUpdatedAt is not null
           && (InspectionUpdatedAt.Value - InspectionCreatedAt.Value)
               .TotalMinutes > 1;

    /// <summary>点検が済んでいるか。</summary>
    public bool IsDone => InspectedAt is not null;

    /// <summary>点検の対象外にしたものか。</summary>
    public bool IsNotRequired => AlertStatus == "NOT_REQUIRED";

    /// <summary>指示済(点検待ち)か。</summary>
    public bool IsInstructed => AlertStatus == "INSTRUCTED";

    /// <summary>
    /// 直せる記録か。
    ///
    /// 未対応のものだけは直せない。指示も点検もまだなので、
    /// 直す先がそもそも無い（記録するのはアラート対応の画面から）。
    ///
    /// 点検完了のものも直せる。指示を出しても、すぐに点検が
    /// できるとは限らず、あとから補修結果や指摘事項を
    /// 書き足す必要が出るため。直した日時は記録に残る。
    /// </summary>
    public bool CanEdit => AlertStatus != "OPEN";

    /// <summary>
    /// 対象外にした理由。頭の「[対象外] 」を外して返す。
    ///
    /// 画面には理由だけを出したい。印は保存の都合で付けているもので、
    /// 読む人には関係がない。
    /// </summary>
    public string SkipReason
    {
        get
        {
            string note = InstructionNote ?? string.Empty;
            const string mark = "[対象外] ";

            return note.StartsWith(mark) ? note.Substring(mark.Length) : note;
        }
    }
}

/// <summary>
/// すでに登録されている点検記録1件。直す画面に出すために読む。
/// </summary>
public class InspectionDetail
{
    public int InspectionId { get; set; }
    public int AlertId { get; set; }
    public DateTime InspectedAt { get; set; }
    public string InspectorName { get; set; } = string.Empty;
    public string? InspectorQualify { get; set; }
    public string? ScaffoldType { get; set; }

    /// <summary>OK / NG_FIXED / NG_OPEN。</summary>
    public string InspectionResult { get; set; } = string.Empty;

    public string? FindingNote { get; set; }
    public string RegisteredBy { get; set; } = string.Empty;

    /// <summary>登録した日時。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最後に直した日時。</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>登録のあとで直されているか。</summary>
    public bool Edited => (UpdatedAt - CreatedAt).TotalMinutes > 1;
}

/// <summary>点検記録を登録するときに受け取る中身。</summary>
public class InspectionInput
{
    public int AlertId { get; set; }

    /// <summary>点検を実施した日時。</summary>
    public DateTime InspectedAt { get; set; }

    /// <summary>点検実施者の氏名。法令上の必須記録項目。</summary>
    public string InspectorName { get; set; } = string.Empty;

    /// <summary>点検者の資格 (足場の組立て等作業主任者 など)。</summary>
    public string? InspectorQualify { get; set; }

    /// <summary>足場種類。</summary>
    public string? ScaffoldType { get; set; }

    /// <summary>OK / NG_FIXED / NG_OPEN。</summary>
    public string InspectionResult { get; set; } = string.Empty;

    /// <summary>指摘事項・補修内容。</summary>
    public string? FindingNote { get; set; }

    /// <summary>この記録を登録した人。</summary>
    public string RegisteredBy { get; set; } = string.Empty;
}

/// <summary>
/// アラートへの対応と、点検の記録を読み書きする係。
///
/// ★このファイルだけがデータベースへ書き込む。
///   他の画面はすべて読むだけ。
///   どこで記録が変わるのかを1か所にまとめておくため。
/// </summary>
public class InspectionRepository
{
    private readonly SqlConnectionFactory _factory;

    public InspectionRepository(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>対応するアラート1件を読む。無ければ空。</summary>
    public AlertDetail? LoadAlert(int alertId)
    {
        const string sql = @"
            SELECT a.AlertId, a.SiteId, s.SiteName, s.Address, s.ManagerName,
                   a.EventType, a.OccurredAt, a.TriggerValue, a.TriggerText,
                   a.AlertStatus, a.LegalBasis,
                   a.InstructedAt, a.InstructedBy, a.InstructionNote
            FROM dbo.T_Alert AS a
            INNER JOIN dbo.M_Site AS s ON a.SiteId = s.SiteId
            WHERE a.AlertId = @alertId;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new AlertDetail
        {
            AlertId = reader.GetInt32(0),
            SiteId = reader.GetInt32(1),
            SiteName = reader.GetString(2),
            Address = reader.IsDBNull(3) ? null : reader.GetString(3),
            ManagerName = reader.IsDBNull(4) ? null : reader.GetString(4),
            EventType = reader.GetString(5),
            OccurredAt = reader.GetDateTime(6),
            TriggerValue = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            TriggerText = reader.IsDBNull(8) ? null : reader.GetString(8),
            AlertStatus = reader.GetString(9),
            LegalBasis = reader.IsDBNull(10) ? null : reader.GetString(10),
            InstructedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            InstructedBy = reader.IsDBNull(12) ? null : reader.GetString(12),
            InstructionNote = reader.IsDBNull(13) ? null : reader.GetString(13),
        };
    }

    /// <summary>
    /// 現場へ指示したことを記録する。
    ///
    /// 誰がいつ指示したかを残す。
    /// 臨検で対応の速さを示す根拠になる。
    /// </summary>
    public bool SaveInstruction(int alertId, string who, string? memo)
    {
        // まだ対応中のものだけを更新する。
        // 画面を開いたまま別の人が先に処理した場合に、
        // 完了しているものを差し戻してしまわないようにする。
        const string sql = @"
            UPDATE dbo.T_Alert
            SET AlertStatus = N'INSTRUCTED',
                InstructedAt = SYSDATETIME(),
                InstructedBy = @who,
                InstructionNote = @memo,
                UpdatedAt = SYSDATETIME()
            WHERE AlertId = @alertId
              AND AlertStatus IN (N'OPEN', N'INSTRUCTED');";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        command.Parameters.AddWithValue("@who", who);
        command.Parameters.AddWithValue("@memo",
            string.IsNullOrWhiteSpace(memo) ? DBNull.Value : memo);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 点検記録を登録し、あわせてアラートを点検完了にする。
    ///
    /// 2つの書き込みは、まとめて成功するかまとめて失敗するかの
    /// どちらかにする。片方だけ残ると、点検記録はあるのに
    /// アラートが未対応のまま、といった食い違いが起きる。
    /// </summary>
    /// <returns>登録できたら true。既に対応済みなら false。</returns>
    public bool SaveInspection(InspectionInput input, string reason)
    {
        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            // 対応中のものだけを完了にする。
            // ここで0件なら、既に誰かが処理したということなので、
            // 点検記録も入れずに引き返す。二重に記録しないため。
            const string updateSql = @"
                UPDATE dbo.T_Alert
                SET AlertStatus = N'COMPLETED', UpdatedAt = SYSDATETIME()
                WHERE AlertId = @alertId
                  AND AlertStatus IN (N'OPEN', N'INSTRUCTED');";

            using (var update = new SqlCommand(updateSql, connection, tran))
            {
                update.Parameters.AddWithValue("@alertId", input.AlertId);
                if (update.ExecuteNonQuery() == 0)
                {
                    tran.Rollback();
                    return false;
                }
            }

            // 現場のIDはアラートから引く。画面から受け取らないのは、
            // 別の現場の記録として入ってしまうのを防ぐため。
            const string insertSql = @"
                INSERT INTO dbo.T_Inspection
                    (AlertId, SiteId, InspectionReason, InspectedAt,
                     InspectorName, InspectorQualify, ScaffoldType,
                     InspectionResult, FindingNote, RegisteredBy)
                SELECT @alertId, SiteId, @reason, @inspectedAt,
                       @name, @qualify, @scaffold,
                       @result, @finding, @registeredBy
                FROM dbo.T_Alert
                WHERE AlertId = @alertId;";

            using (var insert = new SqlCommand(insertSql, connection, tran))
            {
                insert.Parameters.AddWithValue("@alertId", input.AlertId);
                insert.Parameters.AddWithValue("@reason", reason);
                insert.Parameters.AddWithValue("@inspectedAt", input.InspectedAt);
                insert.Parameters.AddWithValue("@name", input.InspectorName);
                insert.Parameters.AddWithValue("@qualify",
                    Blank(input.InspectorQualify));
                insert.Parameters.AddWithValue("@scaffold",
                    Blank(input.ScaffoldType));
                insert.Parameters.AddWithValue("@result", input.InspectionResult);
                insert.Parameters.AddWithValue("@finding",
                    Blank(input.FindingNote));
                insert.Parameters.AddWithValue("@registeredBy",
                    input.RegisteredBy);

                insert.ExecuteNonQuery();
            }

            tran.Commit();
            return true;
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 点検が不要と判断したことを記録する。
    ///
    /// 足場を撤去済みなどの場合に使う。
    /// 臨検で理由を問われることがあるため、理由は必ず残す。
    ///
    /// 専用の列は設けず、指示の記録欄を使っている
    /// (テーブルの形を変えない方針のため)。
    /// 先頭に [対象外] を付けて、指示の記録と見分けられるようにする。
    /// </summary>
    public bool SaveNotRequired(int alertId, string who, string why)
    {
        const string sql = @"
            UPDATE dbo.T_Alert
            SET AlertStatus = N'NOT_REQUIRED',
                InstructedAt = SYSDATETIME(),
                InstructedBy = @who,
                InstructionNote = @note,
                UpdatedAt = SYSDATETIME()
            WHERE AlertId = @alertId
              AND AlertStatus IN (N'OPEN', N'INSTRUCTED');";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        command.Parameters.AddWithValue("@who", who);
        command.Parameters.AddWithValue("@note", "[対象外] " + why);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>点検記録簿を読む。</summary>
    public List<AuditRow> LoadAuditTrail(DateTime from, DateTime to)
    {
        // ★V_AuditTrail は使っていない
        //   ビューには AlertId と指示の記録 (誰がいつ何を指示したか) が
        //   入っていない。画面で詳細を出し、直せるようにするには、
        //   その2つが要る。
        //
        //   ビュー自体は動いているものなので触らず、
        //   ここで同じ結び付け方をして、足りない列も一緒に読む。
        //   結び付け方はビューと同じなので、数字は変わらない。
        //     T_Alert に M_Site を内側で結ぶ
        //     T_Inspection は左から結ぶ (点検がまだのものも出す)
        //     消した現場は出さない (IsDeleted = 0)
        //
        // 終了日は、その日の分を含めるため翌日の手前までを見る。
        const string sql = @"
            SELECT a.AlertId, a.SiteId,
                   s.SiteName, s.Address,
                   a.EventType, a.OccurredAt, a.TriggerValue, a.TriggerText,
                   a.LegalBasis,
                   i.InspectedAt, i.InspectorName, i.InspectorQualify,
                   i.InspectionResult, i.FindingNote,
                   DATEDIFF(DAY, a.OccurredAt, i.InspectedAt),
                   a.AlertStatus,
                   a.InstructedAt, a.InstructedBy, a.InstructionNote,
                   i.ScaffoldType, i.RegisteredBy,
                   i.CreatedAt, i.UpdatedAt
            FROM dbo.T_Alert AS a
            INNER JOIN dbo.M_Site AS s ON a.SiteId = s.SiteId
            LEFT JOIN dbo.T_Inspection AS i ON a.AlertId = i.AlertId
            WHERE s.IsDeleted = 0
              AND a.OccurredAt >= @from
              AND a.OccurredAt < DATEADD(DAY, 1, @to)
            ORDER BY a.OccurredAt DESC;";

        var list = new List<AuditRow>();

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new AuditRow
            {
                AlertId = reader.GetInt32(0),
                SiteId = reader.GetInt32(1),
                SiteName = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                EventType = reader.GetString(4),
                OccurredAt = reader.GetDateTime(5),
                TriggerValue = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                TriggerText = reader.IsDBNull(7) ? null : reader.GetString(7),
                LegalBasis = reader.IsDBNull(8) ? null : reader.GetString(8),
                InspectedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                InspectorName = reader.IsDBNull(10)
                    ? null : reader.GetString(10),
                InspectorQualify = reader.IsDBNull(11)
                    ? null : reader.GetString(11),
                InspectionResult = reader.IsDBNull(12)
                    ? null : reader.GetString(12),
                FindingNote = reader.IsDBNull(13) ? null : reader.GetString(13),
                DaysToInspection = reader.IsDBNull(14)
                    ? null : reader.GetInt32(14),
                AlertStatus = reader.GetString(15),
                InstructedAt = reader.IsDBNull(16)
                    ? null : reader.GetDateTime(16),
                InstructedBy = reader.IsDBNull(17)
                    ? null : reader.GetString(17),
                InstructionNote = reader.IsDBNull(18)
                    ? null : reader.GetString(18),
                ScaffoldType = reader.IsDBNull(19)
                    ? null : reader.GetString(19),
                RegisteredBy = reader.IsDBNull(20)
                    ? null : reader.GetString(20),
                InspectionCreatedAt = reader.IsDBNull(21)
                    ? null : reader.GetDateTime(21),
                InspectionUpdatedAt = reader.IsDBNull(22)
                    ? null : reader.GetDateTime(22),
            });
        }

        return list;
    }

    /// <summary>
    /// そのアラートに付いている点検記録を読む。
    ///
    /// 1つのアラートに点検記録は1つだけの想定。
    /// 万一2つ以上あったときは、新しいほうを出す。
    /// </summary>
    public InspectionDetail? LoadInspection(int alertId)
    {
        const string sql = @"
            SELECT TOP 1
                   InspectionId, AlertId, InspectedAt, InspectorName,
                   InspectorQualify, ScaffoldType, InspectionResult,
                   FindingNote, RegisteredBy, CreatedAt, UpdatedAt
            FROM dbo.T_Inspection
            WHERE AlertId = @alertId
            ORDER BY InspectionId DESC;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new InspectionDetail
        {
            InspectionId = reader.GetInt32(0),
            AlertId = reader.GetInt32(1),
            InspectedAt = reader.GetDateTime(2),
            InspectorName = reader.GetString(3),
            InspectorQualify = reader.IsDBNull(4) ? null : reader.GetString(4),
            ScaffoldType = reader.IsDBNull(5) ? null : reader.GetString(5),
            InspectionResult = reader.GetString(6),
            FindingNote = reader.IsDBNull(7) ? null : reader.GetString(7),
            RegisteredBy = reader.GetString(8),
            CreatedAt = reader.GetDateTime(9),
            UpdatedAt = reader.GetDateTime(10),
        };
    }

    /// <summary>
    /// 点検記録を直す。
    ///
    /// ★なぜ直せるようにしているか
    ///   指示を出しても、すぐに点検ができるとは限らない。
    ///   日を置いてから点検し、あとから
    ///     ・補修が終わったので結果を変える
    ///     ・指摘事項を書き足す
    ///     ・資格の書き漏れを埋める
    ///   といった加筆が要る。運用の実際に合わせている。
    ///
    /// ★直したことは残る
    ///   UpdatedAt に「最後に直した日時」が入る。
    ///   登録した日時 (CreatedAt) は動かさない。
    ///   画面では、この2つが違うときに「あとから直した」と出す。
    ///
    /// ★点検実施日時も直せる
    ///   「登録のときに日付を打ち間違えた」が実際に起こるため。
    ///   ただし、事象が起きる前の日時にはできないようにしている
    ///   (それは記録として成り立たない)。
    /// </summary>
    /// <returns>実際に変わったかどうか。</returns>
    public bool UpdateInspection(InspectionInput input)
    {
        const string sql = @"
            UPDATE dbo.T_Inspection
            SET InspectedAt      = @inspectedAt,
                InspectorName    = @name,
                InspectorQualify = @qualify,
                ScaffoldType     = @scaffold,
                InspectionResult = @result,
                FindingNote      = @finding,
                RegisteredBy     = @registeredBy,
                UpdatedAt        = SYSDATETIME()
            WHERE AlertId = @alertId;";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", input.AlertId);
        command.Parameters.AddWithValue("@inspectedAt", input.InspectedAt);
        command.Parameters.AddWithValue("@name", input.InspectorName.Trim());
        command.Parameters.AddWithValue("@qualify", Blank(input.InspectorQualify));
        command.Parameters.AddWithValue("@scaffold", Blank(input.ScaffoldType));
        command.Parameters.AddWithValue("@result", input.InspectionResult);
        command.Parameters.AddWithValue("@finding", Blank(input.FindingNote));
        command.Parameters.AddWithValue("@registeredBy",
                                        input.RegisteredBy.Trim());

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 指示済の記録を直す。
    ///
    /// ★直せるのは「指示済」と「点検完了」
    ///   点検が終わったあとで、指示の書き間違いに気づくことがある。
    ///   未対応のものは、まだ指示していないので直すものがない。
    ///   対象外は別の入口 (UpdateNotRequired) から直す。
    ///
    /// 指示した日時はそのまま残す。
    /// 書き間違いを直すためのものであって、
    /// 「いつ指示したか」を後から作り変えるためのものではない。
    /// </summary>
    /// <returns>実際に変わったかどうか。</returns>
    public bool UpdateInstruction(int alertId, string who, string? memo)
    {
        const string sql = @"
            UPDATE dbo.T_Alert
            SET InstructedBy = @who,
                InstructionNote = @note,
                UpdatedAt = SYSDATETIME()
            WHERE AlertId = @alertId
              AND AlertStatus IN (N'INSTRUCTED', N'COMPLETED');";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        command.Parameters.AddWithValue("@who", who);
        command.Parameters.AddWithValue("@note", Blank(memo));

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 対象外の記録を直す。
    ///
    /// ★直せるのは「対象外」のものだけ。
    ///
    /// 理由の頭に付ける「[対象外] 」は、ここでも同じように付ける。
    /// 付け方を変えると、画面で理由を取り出すところと食い違う。
    /// </summary>
    /// <returns>実際に変わったかどうか。</returns>
    public bool UpdateNotRequired(int alertId, string who, string why)
    {
        const string sql = @"
            UPDATE dbo.T_Alert
            SET InstructedBy = @who,
                InstructionNote = @note,
                UpdatedAt = SYSDATETIME()
            WHERE AlertId = @alertId
              AND AlertStatus = N'NOT_REQUIRED';";

        using var connection = _factory.CreateOpen();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@alertId", alertId);
        command.Parameters.AddWithValue("@who", who);
        command.Parameters.AddWithValue("@note", "[対象外] " + why);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>空文字は「入っていない」として扱う。</summary>
    private static object Blank(string? text)
        => string.IsNullOrWhiteSpace(text) ? DBNull.Value : text.Trim();

    /// <summary>
    /// 選んだアラートをまとめて「指示済」にする。
    ///
    /// 同じ担当者が複数の現場を持っていることがあり、
    /// 1件ずつ開いて入れ直すのは手間が大きい。
    ///
    /// まとめて成功するかまとめて失敗するかのどちらかにする。
    /// 途中で失敗して一部だけ記録が残ると、
    /// どこまで連絡したのかが分からなくなる。
    /// </summary>
    /// <returns>実際に変わった件数。</returns>
    public int SaveInstructionMany(IEnumerable<int> alertIds,
                                   string who, string? memo)
    {
        return UpdateMany(alertIds,
            @"UPDATE dbo.T_Alert
              SET AlertStatus = N'INSTRUCTED',
                  InstructedAt = SYSDATETIME(),
                  InstructedBy = @who,
                  InstructionNote = @note,
                  UpdatedAt = SYSDATETIME()
              WHERE AlertId = @alertId
                AND AlertStatus IN (N'OPEN', N'INSTRUCTED');",
            who,
            string.IsNullOrWhiteSpace(memo) ? null : memo.Trim());
    }

    /// <summary>
    /// 選んだアラートをまとめて「対象外」にする。
    ///
    /// 足場を撤去済み、現場リストがまだ更新されていない、など
    /// 同じ理由で点検が不要な現場がまとまって出ることがある。
    ///
    /// ★理由は選んだ全件に同じものが記録される。
    ///   臨検で理由を問われることがあるため、
    ///   「まとめてよい理由か」を確かめてから使うこと。
    /// </summary>
    /// <returns>実際に変わった件数。</returns>
    public int SaveNotRequiredMany(IEnumerable<int> alertIds,
                                   string who, string why)
    {
        return UpdateMany(alertIds,
            @"UPDATE dbo.T_Alert
              SET AlertStatus = N'NOT_REQUIRED',
                  InstructedAt = SYSDATETIME(),
                  InstructedBy = @who,
                  InstructionNote = @note,
                  UpdatedAt = SYSDATETIME()
              WHERE AlertId = @alertId
                AND AlertStatus IN (N'OPEN', N'INSTRUCTED');",
            who,
            "[対象外] " + why.Trim());
    }

    /// <summary>
    /// 選んだアラートを、同じ内容でまとめて書き換える。
    ///
    /// 上の2つで共通の部分をここにまとめている。
    /// 対応中のものだけを書き換えるので、
    /// 画面を開いたまま別の担当者が先に処理していたものは
    /// そのまま残り、件数にも入らない。
    /// </summary>
    private int UpdateMany(IEnumerable<int> alertIds, string sql,
                           string who, string? note)
    {
        var ids = alertIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        using var connection = _factory.CreateOpen();
        using var tran = connection.BeginTransaction();

        try
        {
            int changed = 0;

            foreach (var id in ids)
            {
                using var command = new SqlCommand(sql, connection, tran);
                command.Parameters.AddWithValue("@alertId", id);
                command.Parameters.AddWithValue("@who", who.Trim());
                command.Parameters.AddWithValue("@note",
                    (object?)note ?? DBNull.Value);

                changed += command.ExecuteNonQuery();
            }

            tran.Commit();
            return changed;
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }
}
