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
    ///
    /// A manual reconnect increments <see cref="_generation"/> and cancels the current attempt.
    /// The loop retries immediately and stays alive. It stops only when the host token is cancelled.
    /// </summary>
    public sealed class WebSocketClient : IDisposable
    {
        private const int MinBackoffMs = 200;
        private const int ReconnectDebounceMs = 300;

        private readonly ConnectorSettings _settings;
        private readonly DataExchangeDispatcher _dispatcher;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _socket;
        private CancellationTokenSource _attemptCts;
        private ConnectionSnapshot _snapshot;
        private int _generation;
        private int _running;
        private int _loopEntered;
        private int _stopping;
        private int _lastReconnectTick = int.MinValue;

        public WebSocketClient(ConnectorSettings settings, DataExchangeDispatcher dispatcher)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _snapshot = new ConnectionSnapshot(
                ConnectionState.Connecting,
                _settings.ServerUri,
                "Starting.",
                connectedAtUtc: null,
                nextRetryMs: null,
                attempt: 0);
        }

        public ConnectionSnapshot CurrentSnapshot =>
            Interlocked.CompareExchange(ref _snapshot, null, null);

        /// <summary>True after <see cref="MarkRunning"/> until the loop exits.</summary>
        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public event Action<ConnectionSnapshot> StatusChanged;

        /// <summary>
        /// Call on the Revit UI thread immediately before starting <see cref="RunAsync"/>
        /// so Reconnect is meaningful before the thread-pool task is scheduled.
        /// </summary>
        public void MarkRunning()
        {
            Interlocked.CompareExchange(ref _running, 1, 0);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _loopEntered, 1, 0) != 0)
                throw new InvalidOperationException("The connection loop is already running.");

            Interlocked.Exchange(ref _running, 1);
            var backoff = NormalizeBackoff(_settings.InitialBackoffMs);
            var attempt = 0;

            try
            {
                while (!ct.IsCancellationRequested && Volatile.Read(ref _stopping) == 0)
                {
                    attempt++;
                    var generation = Volatile.Read(ref _generation);
                    ClientWebSocket socket = null;
                    CancellationTokenSource attemptCts = null;
                    string dropReason = null;
                    var forced = false;

                    try
                    {
                        attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        Interlocked.Exchange(ref _attemptCts, attemptCts);

                        socket = new ClientWebSocket();
                        // Protocol pings. A dead TCP shows up as an exception in the receive loop.
                        // Idle RPC silence must NOT drop the socket: a healthy session often has no traffic.
                        if (_settings.HeartbeatIntervalMs > 0)
                            socket.Options.KeepAliveInterval = _settings.HeartbeatInterval;

                        SetSocket(socket);

                        Publish(ConnectionState.Connecting, "Opening WebSocket.", null, null, attempt);
                        Logger.Info($"Connecting to {_settings.Uri} (attempt {attempt}) ...");
                        await socket.ConnectAsync(_settings.Uri, attemptCts.Token).ConfigureAwait(false);

                        if (ct.IsCancellationRequested)
                            break;

                        if (ReconnectRequested(generation))
                        {
                            forced = true;
                        }
                        else
                        {
                            var connectedAt = DateTime.UtcNow;
                            backoff = NormalizeBackoff(_settings.InitialBackoffMs);
                            Publish(
                                ConnectionState.Connected,
                                "Linked to the MCP server.",
                                connectedAt,
                                null,
                                attempt);
                            Logger.Info("Connected to MCP server.");

                            // Read with the attempt token so Reconnect unblocks the loop.
                            // Dispatch with the host token so an in-flight command can still
                            // reply on the next socket instead of being cancelled mid-transaction.
                            await ReceiveLoopAsync(socket, attemptCts.Token, ct).ConfigureAwait(false);

                            if (ct.IsCancellationRequested)
                                break;

                            if (ReconnectRequested(generation))
                                forced = true;
                            else
                                dropReason = "Server closed the connection.";
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested || Volatile.Read(ref _stopping) == 1)
                            break;

                        if (ReconnectRequested(generation))
                            forced = true;
                        else
                            dropReason = string.IsNullOrWhiteSpace(ex.Message)
                                ? "Connection lost."
                                : ex.Message;
                    }
                    finally
                    {
                        if (attemptCts != null)
                            Interlocked.CompareExchange(ref _attemptCts, null, attemptCts);

                        try { attemptCts?.Dispose(); }
                        catch (ObjectDisposedException) { /* already disposed */ }

                        ReleaseSocket(socket);
                    }

                    if (forced)
                    {
                        Logger.Info("Reconnecting now (requested).");
                        backoff = NormalizeBackoff(_settings.InitialBackoffMs);
                        continue;
                    }

                    if (dropReason == null)
                        continue;

                    var pause = await PauseAfterDropAsync(dropReason, backoff, generation, attempt, ct)
                        .ConfigureAwait(false);
                    if (!pause.ShouldContinue)
                        break;

                    backoff = pause.BackoffMs;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                Interlocked.Exchange(ref _loopEntered, 0);
                Publish(ConnectionState.Disconnected, "Stopped.", null, null, attempt);
                Logger.Info("Connection loop stopped.");
            }
        }

        /// <summary>
        /// Drops the current socket and retries immediately, skipping backoff.
        /// Returns false when the loop is not running.
        /// </summary>
        public bool RequestReconnect()
        {
            if (Volatile.Read(ref _stopping) == 1 || Volatile.Read(ref _running) != 1)
                return false;

            var now = Environment.TickCount;
            if (_lastReconnectTick != int.MinValue &&
                unchecked(now - _lastReconnectTick) < ReconnectDebounceMs)
                return true;

            _lastReconnectTick = now;
            Interlocked.Increment(ref _generation);

            var attempt = CurrentSnapshot?.Attempt ?? 0;
            Logger.Info("Manual reconnect requested.");
            Publish(ConnectionState.Connecting, "Reconnect requested.", null, null, attempt);

            try { _attemptCts?.Cancel(); }
            catch (ObjectDisposedException) { /* attempt already finished */ }

            Abort(GetSocket());
            return true;
        }

        private bool ReconnectRequested(int generation) =>
            Volatile.Read(ref _generation) != generation;

        private async Task<RetryPlan> PauseAfterDropAsync(
            string reason,
            int backoff,
            int generation,
            int attempt,
            CancellationToken ct)
        {
            if (ReconnectRequested(generation))
                return RetryPlan.Again(NormalizeBackoff(_settings.InitialBackoffMs));

            var waitMs = Math.Max(backoff, MinBackoffMs);
            Logger.Warn($"WS disconnected: {reason}. Reconnecting in {waitMs} ms.");
            Publish(ConnectionState.Reconnecting, reason, null, waitMs, attempt);

            var wait = await WaitBackoffAsync(waitMs, generation, ct).ConfigureAwait(false);
            if (wait == WaitResult.Shutdown)
                return RetryPlan.Stop();

            if (wait == WaitResult.Forced)
            {
                Logger.Info("Reconnecting now (requested).");
                return RetryPlan.Again(NormalizeBackoff(_settings.InitialBackoffMs));
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

        private enum WaitResult { Elapsed, Forced, Shutdown }

        private async Task<WaitResult> WaitBackoffAsync(int backoffMs, int generation, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, backoffMs));
            while (true)
            {
                if (ct.IsCancellationRequested)
                    return WaitResult.Shutdown;

                if (ReconnectRequested(generation))
                    return WaitResult.Forced;

                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                    return WaitResult.Elapsed;

                try
                {
                    await Task.Delay(Math.Min(200, remaining), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WaitResult.Shutdown;
                }
            }
        }

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

        private async Task ReceiveLoopAsync(
            ClientWebSocket socket,
            CancellationToken attemptCt,
            CancellationToken lifetimeCt)
        {
            var buffer = new byte[8192];

            while (socket.State == WebSocketState.Open
                   && !attemptCt.IsCancellationRequested
                   && !lifetimeCt.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(socket, buffer, attemptCt).ConfigureAwait(false);
                if (message == null)
                    break;

                // A long-running command must not stall the receive loop.
                // Correlation is the JSON-RPC id carried in each message.
                _ = HandleMessageAsync(message, lifetimeCt);
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
                /* shutting down or reconnecting */
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to handle incoming message.", ex);
            }
        }

        private void Publish(
            ConnectionState state,
            string detail,
            DateTime? connectedAtUtc,
            int? nextRetryMs,
            int attempt)
        {
            var next = new ConnectionSnapshot(
                state,
                _settings.ServerUri,
                detail,
                connectedAtUtc,
                nextRetryMs,
                attempt);

            var previous = Interlocked.Exchange(ref _snapshot, next);
            if (previous != null
                && previous.State == next.State
                && previous.Detail == next.Detail
                && previous.NextRetryMs == next.NextRetryMs
                && previous.ConnectedAtUtc == next.ConnectedAtUtc)
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

            try { _attemptCts?.Cancel(); }
            catch (ObjectDisposedException) { /* attempt already finished */ }

            Abort(GetSocket());
            // The send lock is intentionally not disposed: a response may still be
            // leaving the finally-release while Revit is shutting down.
        }
    }
}
