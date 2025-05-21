using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class VariableExtractionRule
    {
        [JsonPropertyName("variableName")]
        public required string VariableName { get; set; }

        [JsonPropertyName("source")]
        public ExtractionSource Source { get; set; } = ExtractionSource.ResponseBody;

        [JsonPropertyName("path")] // e.g., JSONPath for body, Header name for headers
        public required string Path { get; set; }

        [JsonPropertyName("regex")] // Optional regex to apply on the extracted value
        public string? Regex { get; set; }
    }

    public enum ExtractionSource
    {
        ResponseBody,
        ResponseHeader,
        ResponseStatusCode
    }
}