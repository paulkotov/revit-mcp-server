using System;

namespace RevitConnector.Transport
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    /// <summary>
    /// Immutable view of the bridge link. Published from the socket loop and read by the ribbon.
    /// </summary>
    public sealed class ConnectionSnapshot
    {
        public ConnectionSnapshot(
            ConnectionState state,
            string serverUri,
            string detail,
            DateTime? connectedAtUtc,
            int? nextRetryMs,
            int attempt)
        {
            State = state;
            ServerUri = serverUri ?? string.Empty;
            Detail = detail ?? string.Empty;
            ConnectedAtUtc = connectedAtUtc;
            NextRetryMs = nextRetryMs;
            Attempt = attempt;
        }

        public ConnectionState State { get; }
        public string ServerUri { get; }
        public string Detail { get; }
        public DateTime? ConnectedAtUtc { get; }
        public int? NextRetryMs { get; }
        public int Attempt { get; }
    }
}
