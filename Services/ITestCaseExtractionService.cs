using gentest.Models.Common;

namespace gentest.Services;
public interface ITestCaseExtractionService
{
    List<TestCase> ExtractTestCasesFromResponse(string response);
}