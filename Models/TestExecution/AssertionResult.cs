using System.Text.Json.Serialization;
using GenTest.Models.Common;

namespace GenTest.Models.TestExecution
{
    public class AssertionResult
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public AssertionType Type { get; set; }

        [JsonPropertyName("target")]
        public string? Target { get; set; }

        [JsonPropertyName("condition")]
        public AssertionCondition Condition { get; set; }

        [JsonPropertyName("expectedValue")]
        public object? ExpectedValue { get; set; }

        [JsonPropertyName("actualValue")]
        public object? ActualValue { get; set; }

        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}