using System;
using System.Text;
using RevitConnector.Transport;

namespace RevitConnector.UI
{
    internal static class ConnectionStatusText
    {
        public static string Label(ConnectionSnapshot snapshot)
        {
            if (snapshot == null)
                return "Offline";

            switch (snapshot.State)
            {
                case ConnectionState.Connected:
                    return snapshot.Listening ? "Listening" : "Connected";
                case ConnectionState.Connecting: return "Connecting";
                case ConnectionState.Reconnecting: return "Retrying";
                default: return "Offline";
            }
        }

        public static string Headline(ConnectionSnapshot snapshot)
        {
            if (snapshot == null)
                return "No status yet.";

            switch (snapshot.State)
            {
                case ConnectionState.Connected:
                    return snapshot.Listening
                        ? "Revit is listening for MCP commands."
                        : "The WebSocket is open. Revit is not listening for commands.";
                case ConnectionState.Connecting:
                    return "Revit is opening a connection to the MCP server.";
                case ConnectionState.Reconnecting:
                    return "The MCP server is unreachable. Revit will retry on its own.";
                default:
                    return "Revit is not connected to the MCP server.";
            }
        }

        public const string ConnectTooltip = "Let Revit execute MCP commands. The socket connects on its own.";

        public const string DisconnectTooltip =
            "Stop executing MCP commands. The WebSocket stays up.";

        public static string Describe(ConnectionSnapshot snapshot)
        {
            if (snapshot == null)
                return "The connector has not reported a status yet.";

            var lines = new StringBuilder();
            lines.AppendLine("Local MCP server: " + snapshot.ServerUri);
            lines.AppendLine("Status: " + Label(snapshot));
            lines.AppendLine(snapshot.Listening
                ? "Commands: Revit is listening."
                : "Commands: ignored until you press Connect.");
            if (snapshot.Attempt > 0)
                lines.AppendLine("Attempt: " + snapshot.Attempt);
            if (!string.IsNullOrWhiteSpace(snapshot.Detail))
                lines.AppendLine(snapshot.Detail);
            if (snapshot.ConnectedAtUtc.HasValue)
                lines.AppendLine("Up since: " + snapshot.ConnectedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss"));
            if (snapshot.NextRetryMs.HasValue)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(snapshot.NextRetryMs.Value / 1000.0));
                lines.Append("Next attempt in " + seconds + " s.");
            }

            return lines.ToString().TrimEnd();
        }
    }
}
