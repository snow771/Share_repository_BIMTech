// このファイルはLLM接続URLをローカルPCまたは社内LANだけに制限し、意図しない外部送信を防ぐために存在します。
// Uri、IPAddress、注入DNSリゾルバーに依存し、許可範囲や解決規則を変えるとセキュリティ境界とLAN上のLLM接続性に影響します。
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;

namespace Bui.Llm;

/// <summary>
/// LLM接続先の利用可否と、画面に表示できる日本語理由を表します。
/// </summary>
public sealed class LocalEndpointValidation
{
    public LocalEndpointValidation(bool isAllowed, string message)
        : this(isAllowed, message, null)
    {
    }

    internal LocalEndpointValidation(
        bool isAllowed,
        string message,
        IEnumerable<IPAddress>? validatedAddresses = null)
    {
        IsAllowed = isAllowed;
        Message = message;
        // DNS応答配列の後続変更が接続先を差し替えないよう、値と配列の両方を複製します。
        ValidatedAddresses = validatedAddresses is null
            ? ImmutableArray<IPAddress>.Empty
            : validatedAddresses.Select(CloneAddress).ToImmutableArray();
    }

    public bool IsAllowed { get; }

    public string Message { get; }

    public ImmutableArray<IPAddress> ValidatedAddresses { get; }

    private static IPAddress CloneAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPAddress(address.GetAddressBytes(), address.ScopeId)
            : new IPAddress(address.GetAddressBytes());
}

/// <summary>
/// URLの方式と解決先IPアドレスを検査し、外部LLMへの接続を拒否します。
/// </summary>
public static class LocalEndpointPolicy
{
    /// <summary>
    /// 注入されたDNSリゾルバーを使い、接続URLがローカル/LAN専用かを確認します。
    /// </summary>
    public static async Task<LocalEndpointValidation> ValidateAsync(
        Uri endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(dnsResolver);

        if (!endpoint.IsAbsoluteUri
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return Denied("接続URLは http または https のみ指定できます。");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            return Denied("接続URLのホスト名がありません。");
        }

        IPAddress[] addresses;
        try
        {
            // IPリテラルはDNSへ渡さずそのまま検査し、ホスト名は必ず注入された解決境界を通します。
            addresses = IPAddress.TryParse(endpoint.Host, out var literal)
                ? new[] { literal }
                : await dnsResolver(endpoint.IdnHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return Denied("接続先のホスト名を解決できません。");
        }

        if (addresses is null || addresses.Length == 0)
        {
            return Denied("接続先のホスト名を解決できません。");
        }

        // DNSが私有IPと公開IPを同時に返す場合も、公開側へ接続し得るため全体を拒否します。
        if (addresses.Any(address => !IsLocalOrPrivate(address)))
        {
            return Denied("公開ネットワークの接続先は利用できません。ローカルPCまたは社内LANを指定してください。");
        }

        return new LocalEndpointValidation(
            true,
            "ローカルPCまたは社内LANの接続先です。",
            addresses);
    }

    /// <summary>
    /// 通常実行時に使うOSのDNS解決を提供します。
    /// </summary>
    public static Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);

    private static bool IsLocalOrPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var isUniqueLocal = (bytes[0] & 0xfe) == 0xfc;
            var isLinkLocal = bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
            return isUniqueLocal || isLinkLocal;
        }

        return false;
    }

    private static LocalEndpointValidation Denied(string message) => new(false, message);
}
