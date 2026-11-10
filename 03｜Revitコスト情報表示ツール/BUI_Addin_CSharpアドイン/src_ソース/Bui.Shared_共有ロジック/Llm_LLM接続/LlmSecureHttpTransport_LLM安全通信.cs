// このファイルは、検査済みIPへのソケット接続を固定し、LLM通信のDNS再解決とリダイレクト追従を防ぐために存在します。
// SocketsHttpHandler、LocalEndpointValidationとHttpClientに依存し、この構成を変えるとDNS rebinding防止とTLSのホスト名検証に影響します。
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Bui.Llm;

internal static class LlmSecureHttpTransport
{
    /// <summary>検査済み接続先へのみ通信するHttpClientを生成します。</summary>
    internal static HttpClient Create(Uri endpoint, IReadOnlyList<IPAddress> validatedAddresses)
    {
        var connector = new PinnedEndpointConnector(endpoint, validatedAddresses);
        var socketsHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = connector.ConnectAsync
        };
        return CreateCore(socketsHandler);
    }

    internal static HttpClient CreateForTesting(
        Uri endpoint,
        IReadOnlyList<IPAddress> validatedAddresses,
        HttpMessageHandler transport)
    {
        _ = new PinnedEndpointConnector(endpoint, validatedAddresses);
        return CreateCore(transport);
    }

    private static HttpClient CreateCore(HttpMessageHandler transport) =>
        new(new RedirectRejectingHandler(transport), disposeHandler: true);
}

internal sealed class RedirectRejectingHandler : DelegatingHandler
{
    internal RedirectRejectingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        if (statusCode is >= 300 and <= 399)
        {
            response.Dispose();
            throw new HttpRequestException("LLM redirect responses are rejected.");
        }

        return response;
    }
}

internal sealed class PinnedEndpointConnector
{
    private readonly string expectedHost;
    private readonly int expectedPort;
    private readonly IPAddress[] validatedAddresses;

    internal PinnedEndpointConnector(Uri endpoint, IReadOnlyList<IPAddress> validatedAddresses)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(validatedAddresses);
        if (!endpoint.IsAbsoluteUri || validatedAddresses.Count == 0)
        {
            throw new ArgumentException("検査済みのLLM接続先が必要です。", nameof(endpoint));
        }

        expectedHost = NormalizeHost(endpoint.IdnHost);
        expectedPort = endpoint.Port;
        this.validatedAddresses = validatedAddresses.Select(CloneAddress).ToArray();
    }

    internal IReadOnlyList<IPEndPoint> SelectCandidates(DnsEndPoint destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (NormalizeHost(destination.Host) != expectedHost || destination.Port != expectedPort)
        {
            throw new HttpRequestException("LLM connection destination does not match the validated authority.");
        }

        // DnsEndPointのホスト名をOSに再解決させず、ポリシーが返した値だけを接続候補にします。
        return validatedAddresses
            .Select(address => new IPEndPoint(CloneAddress(address), destination.Port))
            .ToArray();
    }

    internal async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var candidate in SelectCandidates(context.DnsEndPoint))
        {
            var socket = new Socket(candidate.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(candidate, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException("None of the validated LLM addresses accepted the connection.", lastError);
    }

    private static string NormalizeHost(string host) => host.TrimEnd('.').ToLowerInvariant();

    private static IPAddress CloneAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPAddress(address.GetAddressBytes(), address.ScopeId)
            : new IPAddress(address.GetAddressBytes());
}
