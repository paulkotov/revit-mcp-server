using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitConnector.UI;

namespace RevitConnector.Commands
{
    /// <summary>
    /// Stops Revit from executing MCP calls. Leaves the WebSocket and its retry loop running.
    /// Does not touch the model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class DisconnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var client = ConnectorSession.Client;
            if (client == null || !client.RequestStopListening())
            {
                message = "Revit could not stop listening.";
                TaskDialog.Show(
                    "MCP connection",
                    "Revit could not stop listening for MCP commands. Restart Revit and try again.");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
