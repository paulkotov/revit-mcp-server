using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands
{
    /// <summary>
    /// Contract for a single Revit API operation exposed over JSON-RPC.
    /// Implementations run ON THE REVIT MAIN THREAD inside a valid API context
    /// (invoked from <see cref="Autodesk.Revit.UI.IExternalEventHandler.Execute"/>).
    /// They must never spin up their own threads that touch the Revit API.
    /// </summary>
    public interface IRevitCommand
    {
        /// <summary>JSON-RPC method name this command answers to, e.g. "revit.getVersion".</summary>
        string Method { get; }

        /// <summary>
        /// When true the executor wraps <see cref="Execute"/> in a <see cref="Autodesk.Revit.DB.Transaction"/>.
        /// Set to false for read-only queries (they still run in API context but need no transaction).
        /// </summary>
        bool RequiresTransaction { get; }

        /// <summary>
        /// When true the executor requires an active <see cref="UIDocument"/> / <see cref="Autodesk.Revit.DB.Document"/>.
        /// Set to false for application-level probes (ping, version) that do not touch the document.
        /// </summary>
        bool RequiresDocument { get; }

        /// <summary>
        /// Executes the operation. Throw to signal failure; the transport layer maps
        /// exceptions to JSON-RPC error objects. Return value is serialized as the "result".
        /// </summary>
        object Execute(UIApplication app, JToken parameters);
    }
}
