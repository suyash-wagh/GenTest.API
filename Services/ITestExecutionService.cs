using GenTest.Models.Common;
using GenTest.Models.TestExecution;

namespace GenTest.Services
{
    public interface ITestExecutionService
    {
        Task<TestRunResult> ExecuteTestRunAsync(
            List<TestCase> testCases,
            string baseUrl,
            Dictionary<string, object>? globalVariables = null,
            Dictionary<string, string>? globalHeaders = null,
            CancellationToken cancellationToken = default);
    }
}
