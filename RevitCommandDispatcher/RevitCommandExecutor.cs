using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCommandDispatcher.Commands;
using RevitCommandDispatcher.Exceptions;

namespace RevitCommandDispatcher
{
    public interface IRevitCommandExecutor
    {
        /// <summary>
        /// Resolves <paramref name="method"/> to a command and runs it on the current
        /// (Revit main) thread, wrapping writes in a transaction. Throws typed
        /// <see cref="CommandException"/>s on failure for the transport to map to JSON-RPC errors.
        /// </summary>
        object Execute(UIApplication app, string method, JToken parameters);
    }

    /// <summary>
    /// Pure Revit-side execution: no WebSocket, no JSON-RPC envelope, no threading.
    /// Intended to be called exclusively from an IExternalEventHandler.Execute body.
    /// </summary>
    public sealed class RevitCommandExecutor : IRevitCommandExecutor
    {
        private readonly CommandRegistry _registry;

        public RevitCommandExecutor(CommandRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public object Execute(UIApplication app, string method, JToken parameters)
        {
            if (!_registry.TryGet(method, out var command))
                throw new CommandNotFoundException(method);

            var uiDoc = app.ActiveUIDocument;
            if ((command.RequiresDocument || command.RequiresTransaction) && uiDoc?.Document == null)
                throw new NoActiveDocumentException();

            if (!command.RequiresTransaction)
                return InvokeGuarded(command, app, parameters);

            var doc = uiDoc.Document;
            using (var tx = new Transaction(doc, command.Method))
            {
                tx.Start();
                try
                {
                    var result = InvokeGuarded(command, app, parameters);
                    tx.Commit();
                    return result;
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    throw;
                }
            }
        }

        private static object InvokeGuarded(IRevitCommand command, UIApplication app, JToken parameters)
        {
            try
            {
                return command.Execute(app, parameters);
            }
            catch (CommandException)
            {
                // Already a typed, mappable error — let it bubble unchanged.
                throw;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                // Revit API failures -> custom -32000.
                throw new RevitCommandException(
                    $"Revit API error while executing '{command.Method}': {ex.Message}", ex);
            }
        }
    }
}
