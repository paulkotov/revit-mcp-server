using Newtonsoft.Json;

namespace RevitConnector.Protocol
{
    public sealed class DataExchangeError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }

        public DataExchangeError() { }

        public DataExchangeError(int code, string message, object data = null)
        {
            Code = code;
            Message = message;
            Data = data;
        }
    }
}
