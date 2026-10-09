using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitConnector.UI;

namespace RevitConnector.Commands
{
    /// <summary>
    /// Asks the background socket loop to drop the current attempt and connect again immediately.
    /// Does not start a second loop and does not touch the Revit model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ReconnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var client = ConnectorSession.Client;
            if (client == null || !client.RequestReconnect())
            {
                message = "The MCP bridge is not running.";
                TaskDialog.Show(
                    "MCP connection",
                    "The bridge is not running. Restart Revit and try again.");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
