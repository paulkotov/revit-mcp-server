using System;
using System.Linq;
using Autodesk.Revit.UI;
using RevitConnector.Commands;
using RevitConnector.Transport;
using RevitConnector.Utils;

namespace RevitConnector.UI
{
    internal static class ConnectionRibbon
    {
        public const string TabName = "MCP";
        public const string PanelName = "Connection";

        public static void Build(UIControlledApplication application, ConnectionStatusPresenter presenter)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));

            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Exception)
            {
                // The tab is already there when the add-in is reloaded in-process.
            }

            var panel = FindPanel(application) ?? application.CreateRibbonPanel(TabName, PanelName);
            if (panel.GetItems().Any(item => item.Name == "McpConnectionStatus"))
            {
                Logger.Warn("MCP connection ribbon already exists; leaving the existing buttons.");
                return;
            }

            ConnectionIcons icons = null;
            try
            {
                icons = ConnectionIcons.Create();
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not build connection icons: " + ex.Message);
            }

            var assembly = typeof(ConnectionRibbon).Assembly.Location;

            var statusData = new PushButtonData(
                "McpConnectionStatus",
                "Offline",
                assembly,
                typeof(ShowConnectionStatusCommand).FullName)
            {
                ToolTip = "MCP bridge status. Click for details.",
                LongDescription =
                    "The WebSocket connects and retries on its own. " +
                    "Green: the link is open. The label says Connected until you press Connect, then Listening. " +
                    "Amber: a connection attempt is in progress. " +
                    "Red: the bridge is offline. " +
                    "Revit does not execute MCP commands until you press Connect.",
                Image = icons?.Small(ConnectionState.Disconnected),
                LargeImage = icons?.Large(ConnectionState.Disconnected)
            };

            var connectData = new PushButtonData(
                "McpConnect",
                "Connect",
                assembly,
                typeof(ConnectCommand).FullName)
            {
                ToolTip = ConnectionStatusText.ConnectTooltip,
                LongDescription =
                    "Lets Revit execute commands that arrive on the WebSocket. " +
                    "The socket itself opens without this button. " +
                    "Nothing runs in the model until you press Connect.",
                Image = icons?.ConnectSmall,
                LargeImage = icons?.ConnectLarge
            };

            var disconnectData = new PushButtonData(
                "McpDisconnect",
                "Disconnect",
                assembly,
                typeof(DisconnectCommand).FullName)
            {
                ToolTip = ConnectionStatusText.DisconnectTooltip,
                LongDescription =
                    "Stops Revit from executing MCP commands. " +
                    "The WebSocket stays connected and still reconnects on its own.",
                Image = icons?.DisconnectSmall,
                LargeImage = icons?.DisconnectLarge
            };

            var statusButton = (PushButton)panel.AddItem(statusData);
            var connectButton = (PushButton)panel.AddItem(connectData);
            var disconnectButton = (PushButton)panel.AddItem(disconnectData);
            presenter.Bind(statusButton, connectButton, disconnectButton, icons);
        }

        private static RibbonPanel FindPanel(UIControlledApplication application)
        {
            try
            {
                return application.GetRibbonPanels(TabName)
                    .FirstOrDefault(panel => panel.Name == PanelName);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
