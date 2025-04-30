namespace gentest.Models.TestExecution
{
    public class AssertionResult
    {
        public string Type { get; set; }
        public string Target { get; set; }
        public string Condition { get; set; }
        public object ExpectedValue { get; set; }
        public object ActualValue { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string Exception { get; set; }
    }
}