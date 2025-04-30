using System.Threading.Tasks;
using gentest.Models.Common;
using gentest.Models.TestExecution;

namespace gentest.Services
{
    public interface ITestExecutionService
    {
        public Task<List<TestCaseResult>> ExecuteTestCasesAsync(
            List<TestCase> testCases, 
            string baseUrl, 
            Dictionary<string, string>? globalHeaders = null,
            CancellationToken cancellationToken = default);
    }
}