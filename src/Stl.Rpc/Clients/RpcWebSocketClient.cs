using System.Net.WebSockets;
using System.Text.Encodings.Web;
using Stl.Rpc.Infrastructure;
using Stl.Rpc.WebSockets;

namespace Stl.Rpc.Clients;

public class RpcWebSocketClient : RpcClient
{
    public record Options
    {
        public static Options Default { get; set; } = new();

        public Func<RpcWebSocketClient, RpcClientPeer, string> HostUrlResolver { get; init; } = DefaultHostUrlResolver;
        public Func<RpcWebSocketClient, RpcClientPeer, Uri> ConnectionUriResolver { get; init; } = DefaultConnectionUriResolver;
        public WebSocketChannel<RpcMessage>.Options WebSocketChannelOptions { get; init; } = WebSocketChannel<RpcMessage>.Options.Default;
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public string RequestPath { get; init; } = "/rpc/ws";
        public string ClientIdParameterName { get; init; } = "clientId";

        public static string DefaultHostUrlResolver(RpcWebSocketClient client, RpcClientPeer peer)
            => peer.Ref.Id.Value;

        public static Uri DefaultConnectionUriResolver(RpcWebSocketClient client, RpcClientPeer peer)
        {
            var settings = client.Settings;
            var url = settings.HostUrlResolver.Invoke(client, peer).TrimSuffix("/");
            var isWebSocketUrl = url.StartsWith("ws://", StringComparison.Ordinal)
                || url.StartsWith("wss://", StringComparison.Ordinal);
            if (!isWebSocketUrl) {
                if (url.StartsWith("http://", StringComparison.Ordinal))
                    url = "ws://" + url.Substring(7);
                else if (url.StartsWith("https://", StringComparison.Ordinal))
                    url = "wss://" + url.Substring(8);
                else
                    url = "wss://" + url;
                url += settings.RequestPath;
            }

            var uriBuilder = new UriBuilder(url);
            var queryTail = $"{settings.ClientIdParameterName}={UrlEncoder.Default.Encode(client.ClientId)}";
            if (!uriBuilder.Query.IsNullOrEmpty())
                uriBuilder.Query += "&" + queryTail;
            else
                uriBuilder.Query = queryTail;
            return uriBuilder.Uri;
        }
    }

    public Options Settings { get; }

    public RpcWebSocketClient(Options settings, IServiceProvider services)
        : base(services)
        => Settings = settings;

    public override async Task<Channel<RpcMessage>> CreateChannel(RpcClientPeer peer, CancellationToken cancellationToken)
    {
        var uri = Settings.ConnectionUriResolver(this, peer);
        var webSocket = Services.GetRequiredService<ClientWebSocket>();
        await webSocket.ConnectAsync(uri, cancellationToken)
            .WaitAsync(Settings.ConnectTimeout, cancellationToken)
            .ConfigureAwait(false);
        var channel = new WebSocketChannel<RpcMessage>(Settings.WebSocketChannelOptions, webSocket, Services);
        return channel;
    }
}
