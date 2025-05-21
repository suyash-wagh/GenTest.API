using System.Threading.Tasks;
using GenTest.Models.Common;
using Microsoft.OpenApi.Models;

namespace GenTest.Services
{
    public interface ITestGenerationService
    {
        Task<List<TestCase>> GenerateTestCasesAsync(string swaggerFilePath, List<string>? selectedEndpoints);
    }
}