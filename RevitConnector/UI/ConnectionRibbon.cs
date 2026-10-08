using System;
using System.Linq;
using Autodesk.Revit.UI;
using RevitConnector.Commands;
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
                "Connecting",
                assembly,
                typeof(ShowConnectionStatusCommand).FullName)
            {
                ToolTip = "MCP bridge status. Click for details.",
                LongDescription =
                    "Green: the WebSocket to the local MCP server is open. " +
                    "Amber: a connection attempt is in progress. " +
                    "Red: the bridge is offline."
            };

            var reconnectData = new PushButtonData(
                "McpReconnect",
                "Reconnect",
                assembly,
                typeof(ReconnectCommand).FullName)
            {
                ToolTip = "Drop the current socket and connect to the MCP server immediately.",
                LongDescription =
                    "Skips the automatic wait and opens a new WebSocket. " +
                    "In-flight MCP calls on the old socket will fail and can be retried.",
                Image = icons?.ReconnectSmall,
                LargeImage = icons?.ReconnectLarge
            };

            var statusButton = (PushButton)panel.AddItem(statusData);
            var reconnectButton = (PushButton)panel.AddItem(reconnectData);
            presenter.Bind(statusButton, reconnectButton, icons);
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
