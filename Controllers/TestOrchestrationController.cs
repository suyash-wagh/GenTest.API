using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using GenTest.Models.ApiDefinition;
using GenTest.Models.Common;
using GenTest.Models.TestExecution;
using gentest.Services;
using GenTest.Services;
using GenTest.Services.ApiParsing;

namespace GenTest.Controllers
{
    [ApiController]
    [Route("api/gentest")]
    public class TestOrchestrationController : ControllerBase
    {
        private readonly IEnumerable<IApiDefinitionParser> _apiDefinitionParsers;
        private readonly ITestGenerationService _testGenerationService;
        private readonly ITestExecutionService _testExecutionService;
        private readonly ISwaggerFileService _swaggerFileService;
        private readonly ILogger<TestOrchestrationController> _logger;

        public TestOrchestrationController(
            IEnumerable<IApiDefinitionParser> apiDefinitionParsers,
            ITestGenerationService testGenerationService,
            ITestExecutionService testExecutionService,
            ISwaggerFileService swaggerFileService,
            ILogger<TestOrchestrationController> logger)
        {
            _apiDefinitionParsers = apiDefinitionParsers;
            _testGenerationService = testGenerationService;
            _testExecutionService = testExecutionService;
            _swaggerFileService= swaggerFileService;
            _logger = logger;
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

            return Ok(new 
            {
                Message = "Swagger file received, saved, and parsed.",
                FilePath = filePath,
                Endpoints = endpoints
            });
        }


        /// <summary>
        /// Generates test cases based on a previously uploaded API definition file.
        /// </summary>
        [HttpPost("generate-tests")]
        [ProducesResponseType(typeof(List<TestCase>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GenerateTests([FromBody] GenerateTestsRequest request)
        {
            if (string.IsNullOrEmpty(request?.SwaggerFilePath))
            {
                return BadRequest("API definition file path is required.");
            }

            if (request.SelectedEndpoints == null || !request.SelectedEndpoints.Any())
            {
                _logger.LogInformation(
                    "No specific endpoints selected for test generation from {FilePath}. All parseable endpoints will be considered if parser supports it, or LLM will be prompted generally.",
                    request.SwaggerFilePath);
            }

            var apiInput = new ApiDefinitionInput
            {
                SourceType = ApiDefinitionSourceType.SwaggerFile,
                SourcePathOrUrl = request.SwaggerFilePath,
                SelectedEndpoints = request.SelectedEndpoints?.ToList()
            };

            _logger.LogInformation("Generating tests for {SourceType} at {Path} with {Count} selected endpoints.",
                apiInput.SourceType, apiInput.SourcePathOrUrl, apiInput.SelectedEndpoints?.Count ?? 0);

            var testCases = await _testGenerationService.GenerateTestCasesAsync(request.SwaggerFilePath, request.SelectedEndpoints?.ToList());

            if (!testCases.Any())
            {
                _logger.LogError("Test generation service returned null for {FilePath}.",
                    request.SwaggerFilePath);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An error occurred during test case generation.");
            }
    
            if (!testCases.Any())
            {
                _logger.LogWarning("No test cases were generated for {FilePath} with selected endpoints.",
                    request.SwaggerFilePath);
                return Ok(new List<TestCase>());
            }

            _logger.LogInformation("Successfully generated {Count} test cases for {FilePath}.", testCases.Count,
                request.SwaggerFilePath);
            return Ok(testCases);
        }

        /// <summary>
        /// Executes a set of test cases against a specified base URL.
        /// </summary>
        [HttpPost("execute-tests")]
        [ProducesResponseType(typeof(TestRunResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExecuteTests([FromBody] ExecuteTestsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.BaseUrl))
            {
                return BadRequest("Base URL is required for test execution.");
            }

            if (request.TestCases == null || !request.TestCases.Any())
            {
                return BadRequest("Test cases to execute are required.");
            }

            _logger.LogInformation("Executing {Count} test cases against base URL {BaseUrl}.", request.TestCases.Count,
                request.BaseUrl);

            // Note: TestExecutionService now returns TestRunResult
            var testRunResult = await _testExecutionService.ExecuteTestRunAsync(
                request.TestCases,
                request.BaseUrl,
                request.GlobalVariables,
                request.GlobalHeaders);

            if (testRunResult == null) // Should not happen if ExecuteTestRunAsync is robust
            {
                _logger.LogError("Test execution service returned null unexpectedly.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error executing test cases.");
            }

            _logger.LogInformation("Test execution completed. Run ID: {RunId}, Passed: {Passed}, Failed: {Failed}",
                testRunResult.TestRunId, testRunResult.TestsPassed, testRunResult.TestsFailed);
            return Ok(testRunResult);
        }
    }

    // --- Request/Response Models ---

    public class UploadDefinitionRequest
    {
        [FromForm(Name = "file")] public required IFormFile File { get; set; }

        [FromForm(Name = "definitionType")]
        public ApiDefinitionSourceType DefinitionType { get; set; } = ApiDefinitionSourceType.SwaggerFile; // Default
    }

    public class EndpointSummary
    {
        public required string Id { get; set; }
        public required string Method { get; set; }
        public required string Path { get; set; }
        public string? Summary { get; set; }
    }

    public class UploadDefinitionResponse
    {
        public required string Message { get; set; }
        public required string SavedFilePath { get; set; } // Relative path
        public ApiDefinitionSourceType DefinitionType { get; set; }
        public required List<EndpointSummary> Endpoints { get; set; }
    }

    public class GenerateTestsRequest
    {
        public required string SwaggerFilePath { get; set; }
        public IEnumerable<string>? SelectedEndpoints { get; set; } // Use IDs from UploadDefinitionResponse.Endpoints
    }

    public class ExecuteTestsRequest
    {
        public required List<TestCase> TestCases { get; set; }
        public required string BaseUrl { get; set; }
        public Dictionary<string, string>? GlobalHeaders { get; set; }
        public Dictionary<string, object>? GlobalVariables { get; set; } // Added
    }

    // ExecuteTestsResponse is implicitly TestRunResult
}