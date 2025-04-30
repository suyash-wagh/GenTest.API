using System.Text.Json.Serialization;

namespace gentest.Models.TestGeneration
{
    // Helper classes for deserializing Gemini responses
    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}