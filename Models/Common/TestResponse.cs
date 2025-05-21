using System.Text.Json.Serialization;
namespace GenTest.Models.Common
{
    public class TestResponse
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }

        [JsonPropertyName("body")]
        public object? Body { get; set; }

        [JsonPropertyName("schema")]
        public string? Schema { get; set; }
    }
}