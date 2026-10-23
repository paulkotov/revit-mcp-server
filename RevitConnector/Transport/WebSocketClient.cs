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
    /// Background WebSocket client. <see cref="Start"/> opens the socket and retries on its own.
    /// Incoming calls are not executed in Revit until <see cref="RequestListen"/>; 
    /// <see cref="RequestStopListening"/> ignores them again without dropping the socket.
    /// Revit is the WS client; the Node MCP server hosts the WS server.
    /// </summary>
    public sealed class WebSocketClient : IDisposable
    {
        private const int MinBackoffMs = 200;

        private readonly ConnectorSettings _settings;
        private readonly DataExchangeDispatcher _dispatcher;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _socket;
        private CancellationTokenSource _lifetimeCts;
        private ConnectionSnapshot _snapshot;
        private int _running;
        private int _loopEntered;
        private int _stopping;
        private int _listening;

        public WebSocketClient(ConnectorSettings settings, DataExchangeDispatcher dispatcher)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _snapshot = new ConnectionSnapshot(
                ConnectionState.Disconnected,
                _settings.ServerUri,
                "Not connected.",
                connectedAtUtc: null,
                nextRetryMs: null,
                attempt: 0,
                listening: false);
        }

        public ConnectionSnapshot CurrentSnapshot =>
            Interlocked.CompareExchange(ref _snapshot, null, null);

        /// <summary>True while MCP calls are executed in Revit.</summary>
        public bool IsListening => Volatile.Read(ref _listening) == 1;

        /// <summary>True when Connect can turn listening on.</summary>
        public bool CanListen => Volatile.Read(ref _stopping) == 0 && !IsListening;

        /// <summary>True when Disconnect can turn listening off.</summary>
        public bool CanStopListening => Volatile.Read(ref _stopping) == 0 && IsListening;

        public event Action<ConnectionSnapshot> StatusChanged;

        /// <summary>
        /// Opens the socket and keeps retrying until disposal.
        /// Does not execute incoming commands; that starts at <see cref="RequestListen"/>.
        /// </summary>
        public bool Start()
        {
            if (Volatile.Read(ref _stopping) == 1)
                return false;

            if (Volatile.Read(ref _loopEntered) != 0)
            {
                return Volatile.Read(ref _running) == 1
                    && Interlocked.CompareExchange(ref _lifetimeCts, null, null) != null;
            }

            var cts = new CancellationTokenSource();
            if (Interlocked.CompareExchange(ref _lifetimeCts, cts, null) != null)
            {
                cts.Dispose();
                return Volatile.Read(ref _stopping) == 0;
            }

            if (Volatile.Read(ref _loopEntered) != 0 || Volatile.Read(ref _stopping) == 1)
            {
                ReleaseLifetime(cts);
                return false;
            }

            Logger.Info("Connection loop starting.");
            Task.Run(() => RunAsync(cts));
            return true;
        }

        /// <summary>
        /// Executes subsequent MCP calls in Revit. The socket loop is started if it is not already up.
        /// </summary>
        public bool RequestListen()
        {
            if (!Start())
                return false;

            if (Interlocked.Exchange(ref _listening, 1) == 1)
                return true;

            Logger.Info("Revit is listening for MCP commands.");
            Republish();
            return true;
        }

        /// <summary>
        /// Ignores incoming MCP calls. Leaves the socket and its retry loop running.
        /// </summary>
        public bool RequestStopListening()
        {
            if (Volatile.Read(ref _stopping) == 1)
                return false;

            if (Interlocked.Exchange(ref _listening, 0) == 0)
                return true;

            Logger.Info("Revit stopped listening for MCP commands.");
            Republish();
            return true;
        }

        private async Task RunAsync(CancellationTokenSource lifetime)
        {
            if (Interlocked.CompareExchange(ref _loopEntered, 1, 0) != 0)
            {
                ReleaseLifetime(lifetime);
                return;
            }

            Interlocked.Exchange(ref _running, 1);

            CancellationToken ct;
            var attempt = 0;
            try
            {
                ct = lifetime.Token;
            }
            catch (ObjectDisposedException)
            {
                Finish(lifetime, attempt);
                return;
            }

            var backoff = NormalizeBackoff(_settings.InitialBackoffMs);

            try
            {
                while (!ct.IsCancellationRequested && Volatile.Read(ref _stopping) == 0)
                {
                    attempt++;
                    ClientWebSocket socket = null;
                    string dropReason = null;

                    try
                    {
                        socket = new ClientWebSocket();
                        // Protocol pings. A dead TCP shows up as an exception in the receive loop.
                        // Idle RPC silence must NOT drop the socket: a healthy session often has no traffic.
                        if (_settings.HeartbeatIntervalMs > 0)
                            socket.Options.KeepAliveInterval = _settings.HeartbeatInterval;

                        SetSocket(socket);

                        Publish(ConnectionState.Connecting, "Opening WebSocket.", null, null, attempt);
                        Logger.Info($"Connecting to {_settings.Uri} (attempt {attempt}) ...");
                        await socket.ConnectAsync(_settings.Uri, ct).ConfigureAwait(false);

                        if (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                            break;

                        var connectedAt = DateTime.UtcNow;
                        backoff = NormalizeBackoff(_settings.InitialBackoffMs);
                        Publish(
                            ConnectionState.Connected,
                            "Linked to the MCP server.",
                            connectedAt,
                            null,
                            attempt);
                        Logger.Info("Connected to MCP server.");

                        await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);

                        if (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                            break;

                        dropReason = "Server closed the connection.";
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                            break;

                        dropReason = string.IsNullOrWhiteSpace(ex.Message)
                            ? "Connection lost."
                            : ex.Message;
                    }
                    finally
                    {
                        ReleaseSocket(socket);
                    }

                    if (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                        break;

                    if (dropReason == null)
                        continue;

                    var pause = await PauseAfterDropAsync(dropReason, backoff, attempt, ct)
                        .ConfigureAwait(false);
                    if (!pause.ShouldContinue)
                        break;

                    backoff = pause.BackoffMs;
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    Logger.Error("Connection loop failed.", ex);
            }
            finally
            {
                Finish(lifetime, attempt);
            }
        }

        private void Finish(CancellationTokenSource lifetime, int attempt)
        {
            Interlocked.Exchange(ref _running, 0);
            ReleaseLifetime(lifetime);
            Interlocked.Exchange(ref _loopEntered, 0);
            Publish(ConnectionState.Disconnected, "Disconnected.", null, null, attempt);
            Logger.Info("Connection loop stopped.");
        }

        private async Task<RetryPlan> PauseAfterDropAsync(
            string reason,
            int backoff,
            int attempt,
            CancellationToken ct)
        {
            var waitMs = Math.Max(backoff, MinBackoffMs);
            Logger.Warn($"WS disconnected: {reason}. Reconnecting in {waitMs} ms.");
            Publish(ConnectionState.Reconnecting, reason, null, waitMs, attempt);

            try
            {
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return RetryPlan.Stop();
            }

            return RetryPlan.Again(NextBackoff(waitMs));
        }

        private int NextBackoff(int current)
        {
            var doubled = current <= 0 ? _settings.InitialBackoffMs : current * 2L;
            var cap = Math.Max(_settings.MaxBackoffMs, MinBackoffMs);
            return (int)Math.Max(MinBackoffMs, Math.Min(doubled, cap));
        }

        private static int NormalizeBackoff(int configured) => Math.Max(configured, MinBackoffMs);

        private readonly struct RetryPlan
        {
            public bool ShouldContinue { get; }
            public int BackoffMs { get; }

            private RetryPlan(bool shouldContinue, int backoffMs)
            {
                ShouldContinue = shouldContinue;
                BackoffMs = backoffMs;
            }

            public static RetryPlan Stop() => new RetryPlan(false, 0);
            public static RetryPlan Again(int backoffMs) => new RetryPlan(true, backoffMs);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[8192];

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(socket, buffer, ct).ConfigureAwait(false);
                if (message == null)
                    break;

                // A long-running command must not stall the receive loop.
                // Correlation is the JSON-RPC id carried in each message.
                _ = HandleMessageAsync(message, ct);
            }
        }

        private async Task HandleMessageAsync(string raw, CancellationToken ct)
        {
            try
            {
                DataExchangeResponse response;
                if (Volatile.Read(ref _listening) == 0)
                    response = RejectWhileIdle(raw);
                else
                    response = await _dispatcher.ProcessAsync(raw, ct).ConfigureAwait(false);

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
        /// Answers a request without entering the Revit API. Notifications are dropped.
        /// </summary>
        private static DataExchangeResponse RejectWhileIdle(string raw)
        {
            DataExchangeRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<DataExchangeRequest>(raw);
            }
            catch (JsonException ex)
            {
                Logger.Warn("Command rejected while idle: invalid JSON. " + ex.Message);
                return DataExchangeResponse.FromError(null, DataExchangeErrorCodes.ParseError, ex.Message);
            }

            if (request == null || request.IsNotification)
                return null;

            Logger.Info("Command ignored; Revit is not listening: " + request.Method);
            return DataExchangeResponse.FromError(
                request.Id,
                DataExchangeErrorCodes.NotListening,
                "Revit is not listening. Press Connect on the MCP tab.");
        }

        private void Republish()
        {
            var current = CurrentSnapshot;
            if (current == null)
                return;

            Publish(current.State, current.Detail, current.ConnectedAtUtc, current.NextRetryMs, current.Attempt);
        }

        private void Publish(
            ConnectionState state,
            string detail,
            DateTime? connectedAtUtc,
            int? nextRetryMs,
            int attempt)
        {
            ConnectionSnapshot next = null;
            ConnectionSnapshot previous = null;
            for (var attemptToPublish = 0; attemptToPublish < 3; attemptToPublish++)
            {
                var listening = Volatile.Read(ref _listening) == 1;
                next = new ConnectionSnapshot(
                    state,
                    _settings.ServerUri,
                    detail,
                    connectedAtUtc,
                    nextRetryMs,
                    attempt,
                    listening);
                previous = Interlocked.Exchange(ref _snapshot, next);
                if ((Volatile.Read(ref _listening) == 1) == listening)
                    break;
            }

            if (previous != null
                && previous.State == next.State
                && previous.Detail == next.Detail
                && previous.NextRetryMs == next.NextRetryMs
                && previous.ConnectedAtUtc == next.ConnectedAtUtc
                && previous.Listening == next.Listening)
                return;

            Logger.Info($"MCP connection {next.State}: {next.Detail}");
            try
            {
                StatusChanged?.Invoke(next);
            }
            catch (Exception ex)
            {
                Logger.Error("Connection status listener failed.", ex);
            }
        }

        private void CancelLifetime()
        {
            var cts = Interlocked.CompareExchange(ref _lifetimeCts, null, null);
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { /* loop already finished */ }
        }

        private void ReleaseLifetime(CancellationTokenSource owned)
        {
            if (owned == null)
                return;

            if (Interlocked.CompareExchange(ref _lifetimeCts, null, owned) == owned)
            {
                try { owned.Dispose(); }
                catch (ObjectDisposedException) { /* already disposed */ }
            }
        }

        private void SetSocket(ClientWebSocket socket) => Interlocked.Exchange(ref _socket, socket);

        private ClientWebSocket GetSocket() => Interlocked.CompareExchange(ref _socket, null, null);

        private void ReleaseSocket(ClientWebSocket socket)
        {
            if (socket == null)
                return;

            Interlocked.CompareExchange(ref _socket, null, socket);
            try { socket.Dispose(); }
            catch { /* already disposed or aborted */ }
        }

        private static void Abort(ClientWebSocket socket)
        {
            if (socket == null)
                return;

            try
            {
                var state = socket.State;
                if (state == WebSocketState.None
                    || state == WebSocketState.Closed
                    || state == WebSocketState.Aborted)
                    return;

                socket.Abort();
            }
            catch
            {
                /* already dead */
            }
        }

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
                var socket = GetSocket();
                if (socket == null || socket.State != WebSocketState.Open)
                    return;

                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException || ex is WebSocketException || ex is IOException)
            {
                Logger.Warn("Dropped RPC response because the socket is closed: " + ex.Message);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 1)
                return;

            Interlocked.Exchange(ref _listening, 0);
            CancelLifetime();
            Abort(GetSocket());
            // The send lock is intentionally not disposed: a response may still be
            // leaving the finally-release while Revit is shutting down.
        }
    }
}
