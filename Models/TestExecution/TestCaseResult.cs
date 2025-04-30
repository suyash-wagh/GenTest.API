using gentest.Models.Common;

namespace gentest.Models.TestExecution
{
    public class TestCaseResult
    {
        public string TestCaseId { get; set; }
        public string TestCaseName { get; set; }
        public bool Passed { get; set; }
        public int ResponseStatusCode { get; set; }
        public Dictionary<string, string> ResponseHeaders { get; set; }
        public string ResponseBody { get; set; }
        public long ExecutionTime { get; set; } // in milliseconds
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string ErrorMessage { get; set; }
        public string Exception { get; set; }
        public List<AssertionResult> FailedAssertions { get; set; }
    }
}