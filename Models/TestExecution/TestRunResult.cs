using System.Text.Json.Serialization;
namespace GenTest.Models.TestExecution
{
    public class TestRunResult
    {
        [JsonPropertyName("testRunId")]
        public string TestRunId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public DateTime EndTime { get; set; }

        [JsonPropertyName("durationMs")]
        public long DurationMs => (long)(EndTime - StartTime).TotalMilliseconds;

        [JsonPropertyName("baseUrl")]
        public required string BaseUrl { get; set; }

        [JsonPropertyName("totalTests")]
        public int TotalTests { get; set; }

        [JsonPropertyName("testsPassed")]
        public int TestsPassed => TestCaseResults.Count(r => r.Status == TestStatus.Passed);

        [JsonPropertyName("testsFailed")]
        public int TestsFailed => TestCaseResults.Count(r => r.Status == TestStatus.Failed);

        [JsonPropertyName("testsSkipped")]
        public int TestsSkipped => TestCaseResults.Count(r => r.Status == TestStatus.Skipped);

        [JsonPropertyName("testsBlocked")]
        public int TestsBlocked => TestCaseResults.Count(r => r.Status == TestStatus.Blocked);

        [JsonPropertyName("testsWithError")]
        public int TestsWithError => TestCaseResults.Count(r => r.Status == TestStatus.Error);

        [JsonPropertyName("globalVariables")]
        public Dictionary<string, object>? GlobalVariables { get; set; }

        [JsonPropertyName("testCaseResults")]
        public List<TestCaseResult> TestCaseResults { get; set; } = new List<TestCaseResult>();
    }
}