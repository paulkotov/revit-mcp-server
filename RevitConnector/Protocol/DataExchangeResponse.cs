using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitConnector.Protocol
{
    public sealed class DataExchangeResponse
    {
        /// <summary>Wire protocol version field (<c>jsonrpc</c> in JSON-RPC 2.0).</summary>
        [JsonProperty("jsonrpc")]
        public string ProtocolVersion { get; set; } = "2.0";

        [JsonProperty("id")]
        public JToken Id { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public DataExchangeError Error { get; set; }

        /// <summary>
        /// Serialize <c>result</c> (including null) only on success so the payload always has
        /// either <c>result</c> or <c>error</c>, never both — per JSON-RPC 2.0.
        /// </summary>
        public bool ShouldSerializeResult() => Error == null;

        public static DataExchangeResponse FromResult(JToken id, object result)
            => new DataExchangeResponse { Id = id, Result = result };

        public static DataExchangeResponse FromError(JToken id, int code, string message, object data = null)
            => new DataExchangeResponse { Id = id, Error = new DataExchangeError(code, message, data) };
    }
}
