using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitConnector.Configuration;
using RevitConnector.Protocol;
using RevitConnector.Utils;

namespace RevitConnector.Transport
{
    /// <summary>
    /// Background WebSocket client: connect-with-backoff, receive loop, serialized sends.
    /// Revit is the WS client; the Node MCP server hosts the WS server.
    /// </summary>
    public sealed class WebSocketClient : IDisposable
    {
        private readonly ConnectorSettings _settings;
        private readonly DataExchangeDispatcher _dispatcher;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _socket;
        private long _lastActivityTicks;

        public WebSocketClient(ConnectorSettings settings, DataExchangeDispatcher dispatcher)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var backoff = _settings.InitialBackoffMs;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (_socket = new ClientWebSocket())
                    {
                        // Protocol-level keep-alive: periodic Ping/Pong frames keep the
                        // link warm and let the OS surface a dead TCP as an exception.
                        _socket.Options.KeepAliveInterval = _settings.HeartbeatInterval;

                        Logger.Info($"Connecting to {_settings.Uri} ...");
                        await _socket.ConnectAsync(_settings.Uri, ct).ConfigureAwait(false);
                        Logger.Info("Connected to MCP server.");

                        backoff = _settings.InitialBackoffMs; // reset after a successful connect
                        MarkActivity();

                        // Application-level watchdog: abort a silently dead ("half-open")
                        // connection so the loop below reconnects.
                        using (var connCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            var watchdog = RunWatchdogAsync(_socket, connCts.Token);
                            try
                            {
                                await ReceiveLoopAsync(_socket, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                connCts.Cancel();
                                await watchdog.ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"WS disconnected: {ex.Message}. Reconnecting in {backoff} ms.");
                    try
                    {
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    backoff = Math.Min(backoff * 2, _settings.MaxBackoffMs);
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[8192];

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(socket, buffer, ct).ConfigureAwait(false);
                MarkActivity();
                if (message == null)
                    break; // close frame received

                // Fire-and-forget: a long-running command must not stall the receive loop.
                // Correlation is preserved by the JSON-RPC id carried in each message.
                _ = HandleMessageAsync(message, ct);
            }
        }

        private async Task HandleMessageAsync(string raw, CancellationToken ct)
        {
            try
            {
                var response = await _dispatcher.ProcessAsync(raw, ct).ConfigureAwait(false);
                if (response != null)
                    await SendAsync(response, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                /* shutting down */
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to handle incoming message.", ex);
            }
        }

        /// <summary>
        /// Polls for inbound silence. If no message arrives within HeartbeatTimeout the
        /// connection is treated as half-open and aborted, which raises an exception in
        /// ReceiveLoopAsync and lets RunAsync reconnect.
        /// </summary>
        private async Task RunWatchdogAsync(ClientWebSocket socket, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_settings.HeartbeatInterval, ct).ConfigureAwait(false);

                    var idleTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastActivityTicks);
                    var idle = TimeSpan.FromTicks(idleTicks);

                    if (idle > _settings.HeartbeatTimeout && socket.State == WebSocketState.Open)
                    {
                        Logger.Warn(
                            $"No traffic for {idle.TotalSeconds:F0}s (> {_settings.HeartbeatTimeout.TotalSeconds:F0}s). " +
                            "Aborting connection to force reconnect.");
                        socket.Abort();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                /* connection closed normally */
            }
        }

        private void MarkActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

        private static async Task<string> ReadMessageAsync(ClientWebSocket socket, byte[] buffer, CancellationToken ct)
        {
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", ct)
                            .ConfigureAwait(false);
                        return null;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private async Task SendAsync(DataExchangeResponse response, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            // ClientWebSocket.SendAsync is NOT safe for concurrent callers.
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var socket = _socket;
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            _sendLock.Dispose();
            _socket?.Dispose();
        }
    }
}
