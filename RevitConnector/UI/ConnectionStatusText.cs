using System;
using System.Text;
using RevitConnector.Transport;

namespace RevitConnector.UI
{
    internal static class ConnectionStatusText
    {
        public static string Label(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected: return "Connected";
                case ConnectionState.Connecting: return "Connecting";
                case ConnectionState.Reconnecting: return "Retrying";
                default: return "Offline";
            }
        }

        public static string Headline(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return "Revit is linked to the MCP server.";
                case ConnectionState.Connecting:
                    return "Revit is opening a connection to the MCP server.";
                case ConnectionState.Reconnecting:
                    return "The MCP server is unreachable. Revit will retry on its own.";
                default:
                    return "The MCP bridge is not running.";
            }
        }

        public static string ReconnectTooltip(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return "Drop the socket and connect to the MCP server again now.";
                case ConnectionState.Connecting:
                    return "Cancel this attempt and try again now.";
                case ConnectionState.Reconnecting:
                    return "Skip the wait and try again now.";
                default:
                    return "Connect to the MCP server.";
            }
        }

        public static string Describe(ConnectionSnapshot snapshot)
        {
            if (snapshot == null)
                return "The connector has not reported a status yet.";

            var lines = new StringBuilder();
            lines.AppendLine("Local MCP server: " + snapshot.ServerUri);
            lines.AppendLine("Status: " + Label(snapshot.State));
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
