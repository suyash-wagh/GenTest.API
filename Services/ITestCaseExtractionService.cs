using GenTest.Models.Common;

namespace GenTest.Services;
public interface ITestCaseExtractionService
{
    List<TestCase> ExtractTestCasesFromResponse(string response);
}