using GenTest.Models.Common;
using System.Text.Json.Serialization;

namespace GenTest.Models.TestExecution
{
    public class TestCaseResult
    {
        [JsonPropertyName("testCaseId")]
        public required string TestCaseId { get; set; }

        [JsonPropertyName("testCaseName")]
        public string? TestCaseName { get; set; }

        [JsonPropertyName("status")]
        public TestStatus Status { get; set; } = TestStatus.Pending;

        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public DateTime EndTime { get; set; }

        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; }

        [JsonPropertyName("requestUrl")]
        public string? RequestUrl { get; set; }

        [JsonPropertyName("requestMethod")]
        public string? RequestMethod { get; set; }

        [JsonPropertyName("requestHeaders")]
        public Dictionary<string, string>? RequestHeaders { get; set; }

        [JsonPropertyName("requestBody")]
        public string? RequestBody { get; set; }

        [JsonPropertyName("responseStatusCode")]
        public int? ResponseStatusCode { get; set; }

        [JsonPropertyName("responseHeaders")]
        public Dictionary<string, string>? ResponseHeaders { get; set; }

        [JsonPropertyName("responseBody")]
        public string? ResponseBody { get; set; }

        [JsonPropertyName("assertions")]
        public List<AssertionResult> AssertionResults { get; set; } = new List<AssertionResult>();

        [JsonPropertyName("extractedVariables")]
        public Dictionary<string, object?>? ExtractedVariables { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("stackTrace")]
        public string? StackTrace { get; set; }

        [JsonPropertyName("retryAttempts")]
        public int RetryAttempts { get; set; }
    }

    public enum TestStatus
    {
        Pending,
        Skipped,
        Running,
        Passed,
        Failed,
        Error,
        Blocked 
    }
}