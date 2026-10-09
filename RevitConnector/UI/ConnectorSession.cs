using System.Threading;
using RevitConnector.Transport;

namespace RevitConnector.UI
{
    /// <summary>
    /// Revit constructs <see cref="Autodesk.Revit.UI.IExternalCommand"/> instances itself,
    /// so the ribbon commands reach the live client through this session.
    /// </summary>
    internal static class ConnectorSession
    {
        private static ConnectionSnapshot _snapshot;

        public static WebSocketClient Client { get; private set; }

        public static ConnectionSnapshot Snapshot =>
            Interlocked.CompareExchange(ref _snapshot, null, null);

        public static void Attach(WebSocketClient client)
        {
            Client = client;
            SetSnapshot(client?.CurrentSnapshot);
        }

        public static void SetSnapshot(ConnectionSnapshot snapshot) =>
            Interlocked.Exchange(ref _snapshot, snapshot);
    }
}
