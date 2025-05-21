namespace GenTest.Models.TestExecution
{
    public class TestExecutorSettings
    {
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxDegreeOfParallelism { get; set; } = 4;
        public int MaxRetries { get; set; } = 0;
        public int RetryDelayMilliseconds { get; set; } = 1000;
        public bool AllowUntrustedSSL { get; set; } = false;
    }
}