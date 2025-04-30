using System.Text.Json.Serialization;

namespace gentest.Models.Common
{
    public class TestRequest
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }
        
        [JsonPropertyName("path")]
        public string Path { get; set; }
        
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; }
        
        [JsonPropertyName("pathParameters")]
        public Dictionary<string, string> PathParameters { get; set; }
        
        [JsonPropertyName("queryParameters")]
        public Dictionary<string, string> QueryParameters { get; set; }
        
        [JsonPropertyName("body")]
        public object Body { get; set; }
    }
}