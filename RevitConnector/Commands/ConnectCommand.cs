using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitConnector.UI;

namespace RevitConnector.Commands
{
    /// <summary>
    /// Lets incoming MCP calls run in Revit. Does not open a second socket and does not touch the model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ConnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var client = ConnectorSession.Client;
            if (client == null || !client.RequestListen())
            {
                message = "Revit could not start listening.";
                TaskDialog.Show(
                    "MCP connection",
                    "Revit could not start listening for MCP commands. Restart Revit and try again.");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
