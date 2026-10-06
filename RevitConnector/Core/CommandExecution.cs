using System.Threading.Tasks;
using RevitConnector.Protocol;

namespace RevitConnector.Core
{
    /// <summary>
    /// A unit of work handed from the background WS thread to the Revit main thread.
    /// The <see cref="Completion"/> source bridges the thread boundary and carries the
    /// JSON-RPC correlation implicitly (the awaiting continuation already knows the id).
    /// </summary>
    public sealed class CommandExecution
    {
        public DataExchangeRequest Request { get; }

        /// <remarks>
        /// RunContinuationsAsynchronously is essential: the continuation performs the
        /// WebSocket send, which must NOT run on the Revit main thread.
        /// </remarks>
        public TaskCompletionSource<DataExchangeResponse> Completion { get; } =
            new TaskCompletionSource<DataExchangeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        public CommandExecution(DataExchangeRequest request)
        {
            Request = request;
        }
    }
}
