using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitConnector.Configuration;
using RevitConnector.Core;
using RevitConnector.Protocol;

namespace RevitConnector.Transport
{
    /// <summary>
    /// Parses an incoming JSON-RPC message, marshals it to the Revit main thread via the
    /// queue + ExternalEvent, and (for requests) awaits the correlated response with a timeout.
    /// Pure async code — runs entirely on background threads.
    /// </summary>
    public sealed class DataExchangeDispatcher
    {
        private readonly ConcurrentQueue<CommandExecution> _queue;
        private readonly ExternalEvent _externalEvent;
        private readonly ConnectorSettings _settings;

        public DataExchangeDispatcher(
            ConcurrentQueue<CommandExecution> queue,
            ExternalEvent externalEvent,
            ConnectorSettings settings)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <returns>
        /// The response to send back, or null for notifications (no id).
        /// Parse / invalid-request failures still return an error response (possibly with a null id).
        /// </returns>
        public async Task<DataExchangeResponse> ProcessAsync(string rawMessage, CancellationToken ct)
        {
            DataExchangeRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<DataExchangeRequest>(rawMessage);
            }
            catch (JsonException ex)
            {
                return DataExchangeResponse.FromError(null, DataExchangeErrorCodes.ParseError, ex.Message);
            }

            if (request == null || string.IsNullOrEmpty(request.Method))
                return DataExchangeResponse.FromError(request?.Id, DataExchangeErrorCodes.InvalidRequest,
                    "Missing 'method' or malformed request.");

            var execution = new CommandExecution(request);
            _queue.Enqueue(execution);
            _externalEvent.Raise(); // ask Revit to drain the queue on its main thread

            if (request.IsNotification)
                return null; // notifications get no response

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(_settings.ExecutionTimeout);

                var completed = await Task.WhenAny(
                    execution.Completion.Task,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

                if (completed == execution.Completion.Task)
                    return await execution.Completion.Task.ConfigureAwait(false);

                // Parent cancellation (shutdown) vs. ExecutionTimeout.
                ct.ThrowIfCancellationRequested();

                return DataExchangeResponse.FromError(request.Id, DataExchangeErrorCodes.ExecutionTimeout,
                    "Revit did not process the command in time (it may be busy or a modal dialog is open).");
            }
        }
    }
}
