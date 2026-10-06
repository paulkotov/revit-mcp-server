using System;
using System.IO;
using Newtonsoft.Json;
using RevitConnector.Utils;

namespace RevitConnector.Configuration
{
    public sealed class ConnectorSettings
    {
        /// <summary>WebSocket endpoint of the Node MCP server (which hosts the WS server).</summary>
        public string ServerUri { get; set; } = "ws://127.0.0.1:8080";

        /// <summary>Max time to wait for the Revit main thread to process a command.</summary>
        public int ExecutionTimeoutMs { get; set; } = 60000;

        public int InitialBackoffMs { get; set; } = 1000;
        public int MaxBackoffMs { get; set; } = 30000;

        /// <summary>WebSocket protocol keep-alive interval and watchdog poll period.</summary>
        public int HeartbeatIntervalMs { get; set; } = 15000;

        /// <summary>
        /// Max tolerated silence (no inbound traffic) before the watchdog aborts the socket
        /// to force a reconnect. Keep it a few multiples of <see cref="HeartbeatIntervalMs"/>.
        /// </summary>
        public int HeartbeatTimeoutMs { get; set; } = 45000;

        [JsonIgnore]
        public Uri Uri => new Uri(ServerUri);

        [JsonIgnore]
        public TimeSpan ExecutionTimeout => TimeSpan.FromMilliseconds(ExecutionTimeoutMs);

        [JsonIgnore]
        public TimeSpan HeartbeatInterval => TimeSpan.FromMilliseconds(HeartbeatIntervalMs);

        [JsonIgnore]
        public TimeSpan HeartbeatTimeout => TimeSpan.FromMilliseconds(HeartbeatTimeoutMs);

        /// <summary>Loads connectorSettings.json next to the assembly; writes defaults if absent.</summary>
        public static ConnectorSettings Load()
        {
            var path = Path.Combine(PathManager.GetAppDataDirectoryPath(), "connectorSettings.json");
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<ConnectorSettings>(File.ReadAllText(path))
                           ?? new ConnectorSettings();

                var defaults = new ConnectorSettings();
                File.WriteAllText(path, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                return defaults;
            }
            catch
            {
                return new ConnectorSettings();
            }
        }
    }
}
