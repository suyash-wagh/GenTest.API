using System.Threading.Tasks;
using gentest.Models.Common;
using Microsoft.OpenApi.Models;

namespace gentest.Services
{
    public interface ITestGenerationService
    {
        Task<List<TestCase>> GenerateTestCasesAsync(string swaggerFilePath, List<string> selectedEndpoints);
    }
}