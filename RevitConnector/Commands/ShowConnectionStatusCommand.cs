using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitConnector.UI;

namespace RevitConnector.Commands
{
    /// <summary>Opens a dialog with the current MCP bridge status. Does not touch the model.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ShowConnectionStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var snapshot = ConnectorSession.Snapshot;
            var dialog = new TaskDialog("MCP connection")
            {
                MainInstruction = snapshot == null
                    ? "No status yet."
                    : ConnectionStatusText.Headline(snapshot.State),
                MainContent = ConnectionStatusText.Describe(snapshot),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
