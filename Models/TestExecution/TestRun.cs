namespace GenTest.Models.TestExecution
{
    public class TestRun
    {
        public string Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string BaseUrl { get; set; }
        public int TotalTests { get; set; }
        public int TestsCompleted { get; set; }
        public int TestsPassed { get; set; }
        public int TestsFailed { get; set; }
        public double Duration { get; set; } // in milliseconds
    }
}