using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class TestCase
    {
        [JsonPropertyName("testCaseId")]
        public required string TestCaseId { get; set; }

        [JsonPropertyName("testCaseName")]
        public string? TestCaseName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("priority")]
        public TestPriority Priority { get; set; } = TestPriority.Medium;

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; } = new List<string>();

        [JsonPropertyName("prerequisites")] // List of TestCaseIds that must pass before this one runs
        public List<string>? Prerequisites { get; set; } = new List<string>();

        [JsonPropertyName("variables")] // Variables defined at the test case level
        public Dictionary<string, object>? Variables { get; set; } = new Dictionary<string, object>();
        
        [JsonPropertyName("authentication")]
        public AuthenticationDetails? Authentication { get; set; }

        [JsonPropertyName("request")]
        public required TestRequest Request { get; set; }

        [JsonPropertyName("expectedResponse")] // Optional, assertions are primary
        public TestResponse? ExpectedResponse { get; set; }

        [JsonPropertyName("assertions")]
        public List<TestAssertion> Assertions { get; set; } = new List<TestAssertion>();

        [JsonPropertyName("extractVariables")] // Rules to extract variables from the response
        public List<VariableExtractionRule>? ExtractVariables { get; set; }

        [JsonPropertyName("mockRequirements")] // Kept for future use
        public List<MockRequirement>? MockRequirements { get; set; }

        [JsonPropertyName("skip")]
        public bool Skip { get; set; } = false;
    }

    public enum TestPriority
    {
        Lowest,
        Low,
        Medium,
        High,
        Highest
    }

    public enum HttpMethodExtended
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH,
        HEAD,
        OPTIONS
    }
}