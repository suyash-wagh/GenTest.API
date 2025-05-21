using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class TestRequest
    {
        [JsonPropertyName("method")]
        public HttpMethodExtended Method { get; set; } = HttpMethodExtended.GET;

        [JsonPropertyName("path")]
        public required string Path { get; set; } // Relative path, can contain {{placeholders}}

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("pathParameters")]
        public Dictionary<string, string>? PathParameters { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("queryParameters")]
        public Dictionary<string, string>? QueryParameters { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("body")]
        public object? Body { get; set; } // Can be string, JObject, or other for auto-serialization

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; } // e.g., application/json, application/xml, application/x-www-form-urlencoded, multipart/form-data

        [JsonPropertyName("formParameters")] // For application/x-www-form-urlencoded
        public Dictionary<string, string>? FormParameters { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("fileParameters")] // For multipart/form-data
        public List<FileParameter>? FileParameters { get; set; } = new List<FileParameter>();
    }
}