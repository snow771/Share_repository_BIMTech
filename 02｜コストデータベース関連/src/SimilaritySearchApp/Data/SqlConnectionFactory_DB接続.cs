using System.Globalization;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp.Data;

/// <summary>SQL Server への接続（Windows 認証 / SQL Server 認証）と、DB 値の変換ヘルパ。</summary>
public sealed class SqlConnectionFactory
{
    private readonly DbSettings _settings;

    public SqlConnectionFactory(DbSettings settings) => _settings = settings;

    public int CommandTimeoutSec => _settings.CommandTimeoutSec;

    public string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = _settings.Server,
            InitialCatalog = _settings.Database,
            IntegratedSecurity = _settings.UseWindowsAuth,
            TrustServerCertificate = _settings.TrustServerCertificate,
            ConnectTimeout = 15,
            ApplicationName = "SimilaritySearchApp",
        };
        if (!_settings.UseWindowsAuth)
        {
            b.UserID = _settings.User;
            b.Password = SettingsStore.Unprotect(_settings.EncryptedPassword);
        }
        return b.ConnectionString;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(BuildConnectionString());
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>接続テスト。成功時はサーバーのバージョンを含むメッセージを返す。</summary>
    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        return $"接続OK: {_settings.Server} / {_settings.Database}（SQL Server {conn.ServerVersion}）";
    }

    // ── SQL 組み立て・値変換の共通ヘルパ ──

    /// <summary>テーブル名を [ ] で囲む（] は ]] にエスケープ）。</summary>
    public static string Quote(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";

    public static object? Value(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i);

    public static double? ToDouble(object? v) => v switch
    {
        null or DBNull => null,
        double d => double.IsFinite(d) ? d : null,
        float f => f,
        decimal m => (double)m,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        string s when double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static int? ToInt(object? v) => ToDouble(v) is double d ? (int)Math.Round(d) : null;

    public static DateTime? ToDate(object? v) => v switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        string s when DateTime.TryParse(s, CultureInfo.GetCultureInfo("ja-JP"), DateTimeStyles.None, out var d) => d,
        _ => null,
    };
}
