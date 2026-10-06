using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCommandDispatcher;
using RevitCommandDispatcher.Exceptions;
using RevitConnector.Protocol;
using RevitConnector.Utils;

namespace RevitConnector.Core
{
    /// <summary>
    /// Runs queued commands ON THE REVIT MAIN THREAD. A single ExternalEvent backed by this
    /// handler serializes all Revit API access, so no locking around the API is required.
    /// </summary>
    public sealed class CommandExecutionHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<CommandExecution> _queue;
        private readonly IRevitCommandExecutor _executor;

        public CommandExecutionHandler(ConcurrentQueue<CommandExecution> queue, IRevitCommandExecutor executor)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public void Execute(UIApplication app)
        {
            // Drain the whole queue: several Raise() calls may coalesce into one Execute().
            while (_queue.TryDequeue(out var exec))
            {
                DataExchangeResponse response;
                try
                {
                    var result = _executor.Execute(app, exec.Request.Method, exec.Request.Params);
                    response = DataExchangeResponse.FromResult(exec.Request.Id, result);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Command '{exec.Request.Method}' failed.", ex);
                    response = MapError(exec.Request.Id, ex);
                }

                // Never throw out of the pump: a single bad command must not kill the loop.
                exec.Completion.TrySetResult(response);
            }
        }

        public string GetName() => "RevitConnector.CommandExecutionHandler";

        private static DataExchangeResponse MapError(JToken id, Exception ex)
        {
            switch (ex)
            {
                case CommandNotFoundException _:
                    return DataExchangeResponse.FromError(id, DataExchangeErrorCodes.MethodNotFound, ex.Message);
                case InvalidParamsException _:
                    return DataExchangeResponse.FromError(id, DataExchangeErrorCodes.InvalidParams, ex.Message);
                case NoActiveDocumentException _:
                    return DataExchangeResponse.FromError(id, DataExchangeErrorCodes.NoActiveDocument, ex.Message);
                case RevitCommandException _:
                    return DataExchangeResponse.FromError(id, DataExchangeErrorCodes.RevitApiError, ex.Message);
                default:
                    return DataExchangeResponse.FromError(id, DataExchangeErrorCodes.InternalError, ex.Message);
            }
        }
    }
}
