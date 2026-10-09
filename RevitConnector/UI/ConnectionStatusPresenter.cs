using System;
using System.Threading;
using Autodesk.Revit.UI;
using RevitConnector.Transport;
using RevitConnector.Utils;

namespace RevitConnector.UI
{
    /// <summary>
    /// Applies connection snapshots to the ribbon on the Revit main thread.
    /// The socket loop publishes from a background thread; <see cref="ExternalEvent.Raise"/>
    /// is thread-safe and coalesces bursts into one <see cref="Execute"/>.
    /// </summary>
    internal sealed class ConnectionStatusPresenter : IExternalEventHandler, IDisposable
    {
        private readonly ExternalEvent _externalEvent;
        private ConnectionSnapshot _pending;
        private int _disposed;
        private PushButton _statusButton;
        private PushButton _reconnectButton;
        private ConnectionIcons _icons;
        private string _appliedKey;

        public ConnectionStatusPresenter()
        {
            // OnStartup is a valid Revit API context, which ExternalEvent.Create requires.
            _externalEvent = ExternalEvent.Create(this);
        }

        public void Bind(PushButton statusButton, PushButton reconnectButton, ConnectionIcons icons)
        {
            _statusButton = statusButton ?? throw new ArgumentNullException(nameof(statusButton));
            _reconnectButton = reconnectButton ?? throw new ArgumentNullException(nameof(reconnectButton));
            _icons = icons;
            Apply(Interlocked.CompareExchange(ref _pending, null, null) ?? ConnectorSession.Snapshot);
        }

        public void Publish(ConnectionSnapshot snapshot)
        {
            if (snapshot == null || Volatile.Read(ref _disposed) == 1)
                return;

            ConnectorSession.SetSnapshot(snapshot);
            Interlocked.Exchange(ref _pending, snapshot);
            // Always raise. Execute no-ops until Bind, and a publish that lands between
            // reading the snapshot and assigning the buttons is still applied on idle.
            try
            {
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                Logger.Warn("Connection ribbon update was not scheduled: " + ex.Message);
            }
        }

        public void Execute(UIApplication app)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

            try
            {
                Apply(Interlocked.CompareExchange(ref _pending, null, null) ?? ConnectorSession.Snapshot);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update the MCP connection ribbon.", ex);
            }
        }

        public string GetName() => "RevitConnector.ConnectionStatusPresenter";

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _externalEvent?.Dispose();
        }

        private void Apply(ConnectionSnapshot snapshot)
        {
            if (snapshot == null || _statusButton == null || _reconnectButton == null)
                return;

            var key = snapshot.State + "|" + snapshot.Detail + "|" + snapshot.NextRetryMs + "|" + snapshot.Attempt;
            if (key == _appliedKey)
                return;
            _appliedKey = key;

            _statusButton.ItemText = ConnectionStatusText.Label(snapshot.State);
            _statusButton.ToolTip = ConnectionStatusText.Describe(snapshot);

            if (_icons != null)
            {
                _statusButton.LargeImage = _icons.Large(snapshot.State);
                _statusButton.Image = _icons.Small(snapshot.State);
            }

            _reconnectButton.ToolTip = ConnectionStatusText.ReconnectTooltip(snapshot.State);
            _reconnectButton.Enabled = snapshot.State != ConnectionState.Disconnected;
        }
    }
}
