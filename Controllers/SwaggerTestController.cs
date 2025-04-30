using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using gentest.Services;
using gentest.Models.Common;
using gentest.Models.TestExecution;

namespace gentest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SwaggerTestController : ControllerBase
    {
        private readonly ISwaggerFileService _swaggerFileService;
        private readonly ITestGenerationService _testGenerationService;
        private readonly ITestExecutionService _testExecutionService;

        public SwaggerTestController(ISwaggerFileService swaggerFileService,
                                     ITestGenerationService testGenerationService,
                                     ITestExecutionService testExecutionService)
        {
            _swaggerFileService = swaggerFileService;
            _testGenerationService = testGenerationService;
            _testExecutionService = testExecutionService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadSwaggerFile([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var filePath = await _swaggerFileService.SaveSwaggerFileAsync(file);

            if (string.IsNullOrEmpty(filePath))
            {
                return StatusCode(500, "Error saving file.");
            }

            var endpoints = await _swaggerFileService.ParseSwaggerFileAsync(filePath);

            return Ok(new UploadSwaggerFileResponse
            {
                Message = "Swagger file received, saved, and parsed.",
                FilePath = filePath,
                Endpoints = endpoints
            });
        }

        [HttpPost("generate-tests")]
        public async Task<IActionResult> GenerateTests([FromBody] GenerateTestsRequest request)
        {
            if (string.IsNullOrEmpty(request?.SwaggerFilePath) || request?.SelectedEndpoints == null || !request.SelectedEndpoints.Any())
            {
                return BadRequest("Swagger file path and selected endpoints are required.");
            }

            // Assuming GenerateTestsAsync in ITestGenerationService can take a list of selected endpoints
            // You might need to adjust the ITestGenerationService interface and implementation
            var testCases = await _testGenerationService.GenerateTestCasesAsync(request.SwaggerFilePath, request.SelectedEndpoints);

            if (testCases == null || !testCases.Any())
            {
                return StatusCode(500, "Error generating test cases or no test cases generated for selected endpoints.");
            }

            return Ok(new GenerateTestsResponse { TestCases = testCases });
        }

        [HttpPost("execute-tests")]
        public async Task<IActionResult> ExecuteTests([FromBody] ExecuteTestsRequest request)
        {
            if (request?.TestCases == null || !request.TestCases.Any())
            {
                return BadRequest("Test cases to execute are required.");
            }

            var results = await _testExecutionService.ExecuteTestCasesAsync(request.TestCases, request.BaseUrl, request.GlobalHeaders);

            if (results == null)
            {
                return StatusCode(500, "Error executing test cases.");
            }

            return Ok(new ExecuteTestsResponse { Results = results });
        }
    }

    public class UploadSwaggerFileResponse
    {
        public string Message { get; set; }
        public string FilePath { get; set; }
        public List<string> Endpoints { get; set; }
    }

    public class GenerateTestsRequest
    {
        public string SwaggerFilePath { get; set; }
        public List<string> SelectedEndpoints { get; set; }
    }

    public class GenerateTestsResponse
    {
        public List<TestCase> TestCases { get; set; }
    }

    public class ExecuteTestsRequest
    {
        public List<TestCase> TestCases { get; set; }
        public string BaseUrl { get; set; }
        public Dictionary<string, string> GlobalHeaders { get; set; }
    }

    public class ExecuteTestsResponse
    {
        public List<TestCaseResult> Results { get; set; }
    }
}