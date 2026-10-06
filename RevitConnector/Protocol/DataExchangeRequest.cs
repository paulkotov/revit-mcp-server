using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitConnector.Protocol
{
    public sealed class DataExchangeRequest
    {
        /// <summary>Wire protocol version field (<c>jsonrpc</c> in JSON-RPC 2.0).</summary>
        [JsonProperty("jsonrpc")]
        public string ProtocolVersion { get; set; } = "2.0";

        /// <summary>String, number or null. Kept as JToken so we can echo it back verbatim.</summary>
        [JsonProperty("id")]
        public JToken Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        /// <summary>Raw params object/array; validated/deserialized by each command.</summary>
        [JsonProperty("params")]
        public JToken Params { get; set; }

        /// <summary>A notification has no id and must not receive a response.</summary>
        [JsonIgnore]
        public bool IsNotification => Id == null || Id.Type == JTokenType.Null;
    }
}
