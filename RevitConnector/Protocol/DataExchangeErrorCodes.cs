namespace RevitConnector.Protocol
{
    /// <summary>JSON-RPC 2.0 standard codes plus this bridge's server-defined codes.</summary>
    public static class DataExchangeErrorCodes
    {
        // Standard (-32768..-32000 reserved)
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        // Server-defined application range (-32000..-32099)
        public const int RevitApiError = -32000;
        public const int NoActiveDocument = -32001;
        public const int ExecutionTimeout = -32002;
    }
}
