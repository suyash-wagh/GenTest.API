using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using gentest.Models.Common;
using gentest.Models.TestGeneration;
using Microsoft.OpenApi.Readers;

namespace gentest.Services
{
    public class TestGenerationService : ITestGenerationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TestGenerationService> _logger;
        private readonly LlmProviderSettings _settings;
        private readonly ISwaggerFileService _swaggerFileService;
        private readonly ITestCaseExtractionService _testCaseExtractionService;

        public TestGenerationService(
            IHttpClientFactory httpClientFactory,
            IOptions<LlmProviderSettings> settings,
            ILogger<TestGenerationService> logger,
            ISwaggerFileService swaggerFileService,
            ITestCaseExtractionService testCaseExtractionService)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
            _swaggerFileService = swaggerFileService;
            _testCaseExtractionService = testCaseExtractionService;
        }

        public async Task<List<TestCase>> GenerateTestCasesAsync(string swaggerFilePath, List<string> selectedEndpoints)
        {
            var generatedTestCases = new List<TestCase>();

            try
            {
                // Parse the Swagger file
                var openApiDocument = await ParseSwaggerFileInternalAsync(swaggerFilePath);

                if (openApiDocument == null)
                {
                    _logger.LogError("Failed to parse Swagger file: {SwaggerFilePath}", swaggerFilePath);
                    return generatedTestCases;
                }

                // Iterate through selected endpoints and generate tests for each
                foreach (var endpoint in selectedEndpoints)
                {
                    var parts = endpoint.Split(' ');
                    if (parts.Length != 2)
                    {
                        _logger.LogWarning("Invalid endpoint format: {Endpoint}. Skipping.", endpoint);
                        continue;
                    }

                    var httpMethod = parts[0].ToUpper();
                    var path = parts[1];

                    if (openApiDocument.Paths.TryGetValue(path, out var pathItem))
                    {
                        if (pathItem.Operations.TryGetValue(GetOperationType(httpMethod), out var operation))
                        {
                            // Use the existing logic to build prompt and call Gemini for this operation
                            string prompt = BuildTestCaseGeneratorPrompt(operation, path, httpMethod);
                            _logger.LogDebug("Generated prompt for Gemini for {HttpMethod} {Path}: {Prompt}", httpMethod, path, prompt);

                            string geminiResponse = await CallGeminiAsync(prompt);

                            var testCasesForEndpoint = _testCaseExtractionService.ExtractTestCasesFromResponse(geminiResponse);
                            generatedTestCases.AddRange(testCasesForEndpoint);

                            _logger.LogInformation("Generated {Count} test cases for endpoint: {HttpMethod} {Path}", testCasesForEndpoint.Count, httpMethod, path);
                        }
                        else
                        {
                            _logger.LogWarning("Operation {HttpMethod} not found for path {Path} in Swagger file.", httpMethod, path);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Path {Path} not found in Swagger file.", path);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating test cases for selected endpoints");
                throw;
            }

            return generatedTestCases;
        }

        private async Task<OpenApiDocument> ParseSwaggerFileInternalAsync(string filePath)
        {
             if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                return null;
            }

            try
            {
                using (var streamReader = new System.IO.StreamReader(filePath))
                {
                    var openApiDocument = new OpenApiStreamReader().Read(streamReader.BaseStream, out var diagnostic);

                    if (diagnostic.Errors.Any())
                    {
                        _logger.LogError("Swagger file parsing errors: {Errors}", string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
                        return null;
                    }

                    return openApiDocument;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Swagger file internally");
                return null;
            }
        }


        private string BuildTestCaseGeneratorPrompt(OpenApiOperation operation, string path, string httpMethod)
        {
            var promptBuilder = new StringBuilder();
            
            promptBuilder.AppendLine("# API Test Case Generator");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## CONTEXT");
            promptBuilder.AppendLine("You are an API testing expert tasked with generating comprehensive test cases for REST APIs based on OpenAPI/Swagger specifications. These test cases will be executed automatically against the API endpoints.");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("## INPUT");
            promptBuilder.AppendLine("The following OpenAPI endpoint details have been provided:");
            promptBuilder.AppendLine($"- Endpoint path: {path}");
            promptBuilder.AppendLine($"- HTTP method: {httpMethod}");
            promptBuilder.AppendLine($"- Operation ID: {operation.OperationId ?? "Not specified"}");
            promptBuilder.AppendLine($"- Description: {operation.Description ?? "Not provided"}");
            
            promptBuilder.AppendLine("- Request parameters:");
            if (operation.Parameters != null && operation.Parameters.Count > 0)
            {
                foreach (var param in operation.Parameters)
                {
                    string paramLocation = param.In.ToString().ToLower();
                    string required = param.Required ? "required" : "optional";
                    string schema = param.Schema?.Type ?? "unknown";
                    
                    promptBuilder.AppendLine($"  * {param.Name} ({paramLocation}, {required}, {schema}): {param.Description}");
                }
            }
            else
            {
                promptBuilder.AppendLine("  * None");
            }
            
            promptBuilder.AppendLine("- Request body schema:");
            if (operation.RequestBody != null && operation.RequestBody.Content.Count > 0)
            {
                foreach (var content in operation.RequestBody.Content)
                {
                    promptBuilder.AppendLine($"  * Content Type: {content.Key}");
                    if (content.Value.Schema != null)
                    {
                        promptBuilder.AppendLine($"    Schema: {SerializeSchemaForPrompt(content.Value.Schema)}");
                    }
                }
            }
            else
            {
                promptBuilder.AppendLine("  * None");
            }
            
            promptBuilder.AppendLine("- Response schemas:");
            if (operation.Responses != null && operation.Responses.Count > 0)
            {
                foreach (var response in operation.Responses)
                {
                    promptBuilder.AppendLine($"  * Status {response.Key}: {response.Value.Description}");
                    if (response.Value.Content != null && response.Value.Content.Count > 0)
                    {
                        foreach (var content in response.Value.Content)
                        {
                            promptBuilder.AppendLine($"    Content Type: {content.Key}");
                            if (content.Value.Schema != null)
                            {
                                promptBuilder.AppendLine($"    Schema: {SerializeSchemaForPrompt(content.Value.Schema)}");
                            }
                        }
                    }
                }
            }
            else
            {
                promptBuilder.AppendLine("  * None specified");
            }
            
            promptBuilder.AppendLine("- Security requirements:");
            if (operation.Security != null && operation.Security.Count > 0)
            {
                foreach (var securityRequirement in operation.Security)
                {
                    foreach (var scheme in securityRequirement.Keys)
                    {
                        promptBuilder.AppendLine($"  * {scheme}");
                    }
                }
            }
            else
            {
                promptBuilder.AppendLine("  * None specified");
            }
            
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## TASK");
            promptBuilder.AppendLine("Generate executable test cases for this endpoint covering:");
            promptBuilder.AppendLine("1. Happy path scenarios");
            promptBuilder.AppendLine("2. Edge cases");
            promptBuilder.AppendLine("3. Error cases");
            promptBuilder.AppendLine("4. Security tests");
            promptBuilder.AppendLine("5. Performance considerations");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("## OUTPUT FORMAT");
            promptBuilder.AppendLine("For each test case, provide:");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("```json");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"testCaseId\": \"string\",");
            promptBuilder.AppendLine("  \"testCaseName\": \"string\",");
            promptBuilder.AppendLine("  \"testCaseDescription\": \"string\",");
            promptBuilder.AppendLine("  \"testCaseType\": \"happy_path|edge_case|error_case|security|performance\",");
            promptBuilder.AppendLine("  \"priority\": \"high|medium|low\",");
            promptBuilder.AppendLine("  \"prerequisites\": [\"string\"],");
            promptBuilder.AppendLine("  \"request\": {");
            promptBuilder.AppendLine("    \"method\": \"string\",");
            promptBuilder.AppendLine("    \"path\": \"string\",");
            promptBuilder.AppendLine("    \"headers\": {},");
            promptBuilder.AppendLine("    \"pathParameters\": {},");
            promptBuilder.AppendLine("    \"queryParameters\": {},");
            promptBuilder.AppendLine("    \"body\": {}");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"expectedResponse\": {");
            promptBuilder.AppendLine("    \"statusCode\": 0,");
            promptBuilder.AppendLine("    \"headers\": {},");
            promptBuilder.AppendLine("    \"body\": {}");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"assertions\": [");
            promptBuilder.AppendLine("    {");
            promptBuilder.AppendLine("      \"type\": \"response_code|response_body|response_time|header\",");
            promptBuilder.AppendLine("      \"target\": \"string\",");
            promptBuilder.AppendLine("      \"condition\": \"equals|contains|exists|not_exists|greater_than|less_than\",");
            promptBuilder.AppendLine("      \"expectedValue\": \"any\"");
            promptBuilder.AppendLine("    }");
            promptBuilder.AppendLine("  ],");
            promptBuilder.AppendLine("  \"mockRequirements\": [");
            promptBuilder.AppendLine("    {");
            promptBuilder.AppendLine("      \"service\": \"string\",");
            promptBuilder.AppendLine("      \"endpoint\": \"string\",");
            promptBuilder.AppendLine("      \"response\": {}");
            promptBuilder.AppendLine("    }");
            promptBuilder.AppendLine("  ]");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine("```");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("## REQUIREMENTS");
            promptBuilder.AppendLine("1. Generate at max 3 test cases per endpoint");
            promptBuilder.AppendLine("2. Ensure test data is realistic and properly formatted");
            promptBuilder.AppendLine("3. Include appropriate assertions for each test case");
            promptBuilder.AppendLine("4. Consider dependencies between endpoints if applicable");
            promptBuilder.AppendLine("5. For security tests, include common vulnerability checks (e.g., injection, authentication bypass)");
            promptBuilder.AppendLine("6. Generate test cases that are directly executable");
            promptBuilder.AppendLine("7. Include detailed descriptions explaining the purpose of each test");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("## INSTRUCTIONS");
            promptBuilder.AppendLine("1. Analyze the endpoint details thoroughly");
            promptBuilder.AppendLine("2. Consider the business logic implied by the API design");
            promptBuilder.AppendLine("3. Think about what could go wrong from both technical and business perspectives");
            promptBuilder.AppendLine("4. Ensure coverage of different response codes, including error responses");
            promptBuilder.AppendLine("5. Use realistic but safe test data - no real PII, credentials, or harmful content");
            promptBuilder.AppendLine("6. For required fields, provide valid values; for optional fields, test both with and without values");
            promptBuilder.AppendLine("7. For enums, test all possible values at least once");
            promptBuilder.AppendLine("8. Consider data type constraints, length limits, and format validation");
            promptBuilder.AppendLine("9. For arrays, test empty, single item, and multiple items scenarios");
            promptBuilder.AppendLine("10. For objects, test different combinations of properties");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("Please provide the test cases in a JSON array format, with each test case following the structure above.");

            return promptBuilder.ToString();
        }

        private async Task<string> CallGeminiAsync(string prompt)
        {
            var client = _httpClientFactory.CreateClient();
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    topP = 0.8,
                    maxOutputTokens = 8192  // Increased to handle multiple test cases
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_settings.GeminiApiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
            
            return responseObject?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
        }

        private string SerializeSchemaForPrompt(OpenApiSchema schema)
        {
            var result = new StringBuilder();
            result.Append($"Type: {schema.Type}");
            
            if (schema.Properties?.Count > 0)
            {
                result.AppendLine(", Properties:");
                foreach (var prop in schema.Properties)
                {
                    string required = schema.Required?.Contains(prop.Key) == true ? "required" : "optional";
                    result.AppendLine($"    - {prop.Key} ({prop.Value.Type}, {required}): {prop.Value.Description}");
                    
                    // Handle nested objects
                    if (prop.Value.Type == "object" && prop.Value.Properties?.Count > 0)
                    {
                        result.AppendLine($"      Nested properties:");
                        foreach (var nestedProp in prop.Value.Properties)
                        {
                            string nestedRequired = prop.Value.Required?.Contains(nestedProp.Key) == true ? "required" : "optional";
                            result.AppendLine($"        - {nestedProp.Key} ({nestedProp.Value.Type}, {nestedRequired}): {nestedProp.Value.Description}");
                        }
                    }
                    
                    // Handle enums
                    if (prop.Value.Enum?.Count > 0)
                    {
                        result.Append($"      Allowed values: [");
                        result.Append(string.Join(", ", prop.Value.Enum));
                        result.AppendLine("]");
                    }
                }
            }
            
            // Handle array items
            if (schema.Type == "array" && schema.Items != null)
            {
                result.AppendLine($", Items: {SerializeSchemaForPrompt(schema.Items)}");
            }
            
            return result.ToString();
        }
        private OperationType GetOperationType(string httpMethod)
        {
            return httpMethod.ToUpper() switch
            {
                "GET" => OperationType.Get,
                "PUT" => OperationType.Put,
                "POST" => OperationType.Post,
                "DELETE" => OperationType.Delete,
                "OPTIONS" => OperationType.Options,
                "HEAD" => OperationType.Head,
                "PATCH" => OperationType.Patch,
                "TRACE" => OperationType.Trace,
                _ => OperationType.Get
            };
        }
    }
}