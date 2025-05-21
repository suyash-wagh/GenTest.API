using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenTest.Models.ApiDefinition;
using GenTest.Models.Common;
using GenTest.Models.TestGeneration;
using GenTest.Services.ApiParsing;

namespace GenTest.Services
{
    public class TestGenerationService(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmProviderSettings> settings,
        ILogger<TestGenerationService> logger,
        IEnumerable<IApiDefinitionParser> apiParsers,
        ITestCaseExtractionService testCaseExtractionService)
        : ITestGenerationService
    {
        private readonly LlmProviderSettings _settings = settings.Value;

        public async Task<List<TestCase>> GenerateTestCasesAsync(string swaggerFilePath,
            List<string>? selectedEndpoints)
        {
            if (selectedEndpoints == null || selectedEndpoints.Count == 0)
                return new List<TestCase>();
            var input = new ApiDefinitionInput
            {
                SourceType = ApiDefinitionSourceType.SwaggerFile,
                SourcePathOrUrl = swaggerFilePath,
                SelectedEndpoints = selectedEndpoints
            };
            return await GenerateTestCasesAsync(input);
        }

        public async Task<List<TestCase>> GenerateTestCasesAsync(ApiDefinitionInput input)
        {
            var generatedTestCases = new List<TestCase>();

            if (input.SourceType != ApiDefinitionSourceType.SwaggerFile)
            {
                logger.LogError("Only SwaggerFile is supported as API definition source type.");
                return generatedTestCases;
            }

            var parser = apiParsers.FirstOrDefault(p => p.CanParse(ApiDefinitionSourceType.SwaggerFile));
            if (parser == null)
            {
                logger.LogError("No Swagger parser found.");
                return generatedTestCases;
            }

            List<ApiEndpointInfo> apiEndpoints =
                await parser.ParseAsync(input);

            if (!apiEndpoints.Any())
            {
                logger.LogWarning("No API endpoints found or selected from the provided definition: {Source}",
                    input.SourcePathOrUrl);
                return generatedTestCases;
            }

            logger.LogInformation("Found {Count} endpoints to process for test case generation.", apiEndpoints.Count);
            
            foreach (var endpointInfo in apiEndpoints)
            {
                try
                {
                    string prompt = BuildTestCaseGeneratorPrompt(endpointInfo);
                    logger.LogDebug("Generated LLM prompt for endpoint ID {EndpointId}: {Prompt}", endpointInfo.Id,
                        prompt.Substring(0, Math.Min(prompt.Length, 500)) + "...");

                    string llmResponse = await CallLlmAsync(prompt); // Renamed from CallGeminiAsync

                    if (string.IsNullOrWhiteSpace(llmResponse))
                    {
                        logger.LogWarning("LLM returned empty response for endpoint ID {EndpointId}", endpointInfo.Id);
                        continue;
                    }

                    var testCasesForEndpoint = testCaseExtractionService.ExtractTestCasesFromResponse(llmResponse);
                    generatedTestCases.AddRange(testCasesForEndpoint);

                    logger.LogInformation("Generated {Count} test cases for endpoint: {EndpointId} ({Method} {Path})",
                        testCasesForEndpoint.Count, endpointInfo.Id, endpointInfo.Method, endpointInfo.Path);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error generating test cases for endpoint ID {EndpointId} ({Method} {Path})",
                        endpointInfo.Id, endpointInfo.Method, endpointInfo.Path);
                    // Continue with other endpoints
                }
            }

            return generatedTestCases;
        }

        private string BuildTestCaseGeneratorPrompt(ApiEndpointInfo endpointInfo)
        {
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("# API Test Case Generator");
            promptBuilder.AppendLine("## CONTEXT");
            promptBuilder.AppendLine(
                "You are an API testing expert. Generate executable test cases for the provided API endpoint. The test cases should be in JSON format, adhering strictly to the TestCase schema defined below.");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("## INPUT: API Endpoint Details");
            promptBuilder.AppendLine($"- Endpoint ID: {endpointInfo.Id}");
            promptBuilder.AppendLine($"- HTTP Method: {endpointInfo.Method.ToString().ToUpper()}");
            promptBuilder.AppendLine($"- Path: {endpointInfo.Path}");
            if (!string.IsNullOrWhiteSpace(endpointInfo.Summary))
                promptBuilder.AppendLine($"- Summary: {endpointInfo.Summary}");
            if (!string.IsNullOrWhiteSpace(endpointInfo.Description))
                promptBuilder.AppendLine($"- Description: {endpointInfo.Description}");
            if (!string.IsNullOrWhiteSpace(endpointInfo.OperationId))
                promptBuilder.AppendLine($"- Operation ID: {endpointInfo.OperationId}");

            promptBuilder.AppendLine("- Parameters:");
            if (endpointInfo.Parameters?.Any() == true)
            {
                foreach (var param in endpointInfo.Parameters)
                {
                    promptBuilder.AppendLine(
                        $"  - Name: {param.Name}, In: {param.In}, Required: {param.Required}, Schema: ({param.Schema?.ToString() ?? "any"}), Description: {param.Description ?? "N/A"}");
                }
            }
            else
            {
                promptBuilder.AppendLine("  - None");
            }

            promptBuilder.AppendLine("- Request Body:");
            if (endpointInfo.RequestBody?.Content?.Any() == true)
            {
                foreach (var contentEntry in endpointInfo.RequestBody.Content)
                {
                    promptBuilder.AppendLine($"  - Content-Type: {contentEntry.Key}");
                    promptBuilder.AppendLine($"    Schema: ({contentEntry.Value.Schema?.ToString() ?? "any"})");
                    if (endpointInfo.RequestBody.Required) promptBuilder.AppendLine("    (Required)");
                }
            }
            else
            {
                promptBuilder.AppendLine("  - None");
            }

            promptBuilder.AppendLine("- Responses:");
            if (endpointInfo.Responses?.Any() == true)
            {
                foreach (var respEntry in endpointInfo.Responses)
                {
                    promptBuilder.AppendLine(
                        $"  - Status Code: {respEntry.Key}, Description: {respEntry.Value.Description ?? "N/A"}");
                    if (respEntry.Value.Content?.Any() == true)
                    {
                        foreach (var contentEntry in respEntry.Value.Content)
                        {
                            promptBuilder.AppendLine(
                                $"    - Content-Type: {contentEntry.Key}, Schema: ({contentEntry.Value.Schema?.ToString() ?? "any"})");
                        }
                    }

                    if (respEntry.Value.Headers?.Any() == true)
                    {
                        promptBuilder.AppendLine("    - Expected Headers:");
                        foreach (var headerEntry in respEntry.Value.Headers)
                        {
                            promptBuilder.AppendLine(
                                $"      - {headerEntry.Key}: Schema: ({headerEntry.Value.Schema?.ToString() ?? "any"}), Description: {headerEntry.Value.Description ?? "N/A"}");
                        }
                    }
                }
            }
            else
            {
                promptBuilder.AppendLine("  - None defined");
            }

            promptBuilder.AppendLine("- Security Requirements:");
            if (endpointInfo.SecurityRequirements?.Any() == true)
            {
                foreach (var sec in endpointInfo.SecurityRequirements)
                {
                    promptBuilder.AppendLine(
                        $"  - Scheme: {sec.SchemeName}, Scopes: [{(sec.Scopes != null ? string.Join(", ", sec.Scopes) : "N/A")}]");
                }
            }
            else
            {
                promptBuilder.AppendLine("  - None defined (assume public or auth handled globally if applicable)");
            }


            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## TASK");
            promptBuilder.AppendLine(
                "Generate a JSON array of comprehensive and executable test cases. Aim for scenarios covering: happy paths, edge cases (e.g., empty values, min/max lengths, invalid formats for typed fields), error handling (e.g., invalid input, unauthorized), and basic security considerations (e.g., testing without authentication if applicable).");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("## OUTPUT FORMAT: JSON Array of TestCase Objects");
            promptBuilder.AppendLine(
                "Ensure the output is a valid JSON array. Each object in the array must conform to the following C# based schema (use the enum string values where applicable):");
            promptBuilder.AppendLine("```json");
            promptBuilder.AppendLine(GetJsonTestCaseSchema()); // Dynamically generate this from your models
            promptBuilder.AppendLine("```");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("## REQUIREMENTS & INSTRUCTIONS:");
            promptBuilder.AppendLine(
                "1.  **Strict JSON**: Output MUST be a valid JSON array `[{...}, {...}]`. Do NOT include any explanatory text outside this JSON structure.");
            promptBuilder.AppendLine(
                "2.  **Max 2 Test Cases**: Generate a diverse set of 3 to 5 test cases per endpoint.");
            promptBuilder.AppendLine(
                "3.  **TestCaseId**: Generate a unique, descriptive ID (e.g., `TC_GetUser_Success`, `TC_CreateOrder_InvalidItem`).");
            promptBuilder.AppendLine("4.  **Priority**: Use `Lowest`, `Low`, `Medium`, `High`, `Highest`.");
            promptBuilder.AppendLine(
                "5.  **HttpMethodExtended**: Use `GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `HEAD`, `OPTIONS`.");
            promptBuilder.AppendLine(
                "6.  **Authentication**: If security is defined (e.g. Bearer, Basic), include a placeholder `authentication` object. If no auth is specified, set `authentication: null` or omit it. For example: `\"authentication\": {\"type\": \"BearerToken\", \"token\": \"{{bearer_token_variable}}\"}` or `\"authentication\": {\"type\": \"Basic\", \"username\": \"testuser\", \"password\": \"{{test_password}}\"}`. Use variables like `{{...}}` for sensitive or dynamic auth values.");
            promptBuilder.AppendLine(
                "7.  **Request Path**: Use the exact path from input. For path parameters like `/users/{id}`, populate `request.pathParameters`: `{\"id\": \"someValue\"}`.");
            promptBuilder.AppendLine(
                "8.  **Request Body**: For POST/PUT/PATCH, provide realistic `request.body`. If `Content-Type` is `application/x-www-form-urlencoded`, use `request.formParameters`. If `multipart/form-data` (e.g. file upload), use `request.fileParameters` (e.g., `{\"name\": \"file\", \"fileName\": \"test.txt\", \"filePath\": \"./test-files/test.txt\"}`) and also set `request.contentType`.");
            promptBuilder.AppendLine(
                "9.  **Assertions**: Provide meaningful multiple `assertions`. Atlease 3 assertions. Use `AssertionType` and `AssertionCondition` enums. Examples:");
            promptBuilder.AppendLine(
                "    - Status Code: `{\"type\": \"StatusCode\", \"condition\": \"Equals\", \"expectedValue\": 200}`");
            promptBuilder.AppendLine(
                "    - Response Time: `{\"type\": \"ResponseTime\", \"condition\": \"LessThan\", \"expectedValue\": 1000}` (in ms)");
            promptBuilder.AppendLine(
                "    - Header: `{\"type\": \"HeaderValue\", \"target\": \"Content-Type\", \"condition\": \"Contains\", \"expectedValue\": \"application/json\"}`");
            promptBuilder.AppendLine(
                "    - JSON Body Field: `{\"type\": \"JsonPathValue\", \"target\": \"data.user.id\", \"condition\": \"Equals\", \"expectedValue\": 123}`");
            promptBuilder.AppendLine(
                "    - Body Contains: `{\"type\": \"BodyContainsString\", \"condition\": \"Contains\", \"expectedValue\": \"Success\"}`");
            promptBuilder.AppendLine(
                "10. **Variables**: Use `{{variableName}}` for dynamic values (e.g., IDs, tokens) that might be set globally or extracted from previous tests. Define `extractVariables` if this endpoint's response provides data for subsequent tests.");
            promptBuilder.AppendLine(
                "11. **Realistic Data**: Use placeholder values that make sense for the schema types (e.g., `\"test@example.com\"` for email, `123` for integer, `\"Sample String\"` for string). For schemas with examples, try to use or adapt them.");
            promptBuilder.AppendLine("12. **Tags**: Add relevant tags like `[\"functional\", \"smoke\"]`.");
            promptBuilder.AppendLine(
                "13. **Skip**: Set `\"skip\": false` unless there's a reason to generate a skipped test.");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("Provide ONLY the JSON array of test cases.");

            return promptBuilder.ToString();
        }

        private string GetJsonTestCaseSchema()
        {
            // This can be manually crafted or, for more robustness,
            // serialize a default instance of TestCase and its sub-objects.
            // For now, a manual string matching your TestCase model and enums.
            // THIS IS CRUCIAL and must exactly match your TestCase structure and enum values.
            return """
                   {
                     "testCaseId": "string (unique, e.g., TC_Method_Path_Scenario)",
                     "testCaseName": "string (descriptive)",
                     "description": "string (detailed explanation of the test)",
                     "priority": "Lowest | Low | Medium | High | Highest",
                     "tags": ["string"],
                     "prerequisites": ["string (testCaseId of a prerequisite test)"],
                     "variables": { "key": "value" }, // Test-case specific variables
                     "authentication": { // Optional, include if auth is needed
                       "type": "None | Basic | BearerToken | ApiKey",
                       "username": "string (for Basic)",
                       "password": "string (for Basic, use {{variable}} for secrets)",
                       "token": "string (for BearerToken, use {{variable}})",
                       "apiKeyHeaderName": "string (for ApiKey)",
                       "apiKeyValue": "string (for ApiKey, use {{variable}})",
                       "apiKeyLocation": "Header | QueryParameter (for ApiKey)"
                     },
                     "request": {
                       "method": "GET | POST | PUT | DELETE | PATCH | HEAD | OPTIONS",
                       "path": "string (e.g., /users/{{userId}})",
                       "headers": { "HeaderName": "HeaderValue {{variable}}" },
                       "pathParameters": { "paramName": "value {{variable}}" },
                       "queryParameters": { "paramName": "value {{variable}}" },
                       "contentType": "string (e.g., application/json, multipart/form-data, application/x-www-form-urlencoded)",
                       "body": { /* JSON object/array, or string for other content types */ },
                       "formParameters": { "key": "value" }, // For application/x-www-form-urlencoded
                       "fileParameters": [ // For multipart/form-data
                         {
                           "name": "string (form field name for file)",
                           "fileName": "string (e.g., document.pdf)",
                           "filePath": "string (e.g., ./testdata/document.pdf, if using local files)",
                           "fileContentBase64": "string (base64 encoded content, alternative to filePath)",
                           "contentType": "string (e.g., application/pdf)"
                         }
                       ]
                     },
                     "expectedResponse": { // Optional, assertions are primary
                       "statusCode": 0, // integer (e.g., 200, 404)
                       "headers": { "HeaderName": "ExpectedHeaderValue" },
                       "body": { /* Expected JSON structure or string */ },
                       "schema": "string (path to schema or schema content - advanced)"
                     },
                     "assertions": [
                       {
                         "description": "string (optional)",
                         "type": "StatusCode | ResponseTime | HeaderExists | HeaderValue | BodyContainsString | BodyEqualsString | BodyMatchesRegex | JsonPathValue | JsonPathExists | JsonPathNotExists | JsonSchemaValidation | XmlPathValue | XmlSchemaValidation | ArrayLength | ArrayContains",
                         "target": "string (e.g., JSONPath 'data.id', header name 'X-RateLimit-Limit', or null for StatusCode/ResponseTime)",
                         "condition": "Equals | NotEquals | Contains | NotContains | GreaterThan | LessThan | GreaterThanOrEquals | LessThanOrEquals | MatchesRegex | NotMatchesRegex | Exists | NotExists | IsEmpty | IsNotEmpty | IsNull | IsNotNull | IsValid | IsNotValid",
                         "expectedValue": "any (string, number, boolean, null, or regex pattern)"
                       }
                     ],
                     "extractVariables": [
                       {
                         "variableName": "string (e.g., newUserId)",
                         "source": "ResponseBody | ResponseHeader | ResponseStatusCode",
                         "path": "string (JSONPath for body, Header name for headers)",
                         "regex": "string (optional regex to apply on extracted value)"
                       }
                     ],
                     "mockRequirements": null, // Or provide example if needed: [{ "service": "...", "endpoint": "..."}]
                     "skip": false
                   }
                   """;
        }

        private async Task<string> CallLlmAsync(string prompt)
        {
            // Your existing CallGeminiAsync logic can be used here
            // Ensure it's robust, handles API limits, retries, etc.
            var client = httpClientFactory.CreateClient("LlmClient"); // Use a named client

            // Make sure _settings.GeminiApiKey is correctly loaded
            if (string.IsNullOrEmpty(_settings.GeminiApiKey))
            {
                logger.LogError("LLM API Key is not configured.");
                return string.Empty;
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    temperature = 0.2, // Lower for more deterministic/schema-adherent output
                    topP = 0.8,
                    maxOutputTokens = 8192,
                    // Potentially add response_mime_type = "application/json" if the LLM API supports it
                    // to encourage JSON output directly. Some models have specific JSON modes.
                }
                // safetySettings can be added if needed
            };

            var requestJson = JsonSerializer.Serialize(requestBody,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            logger.LogTrace("LLM Request Body: {RequestBody}", requestJson);

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_settings.GeminiApiKey}"); // Updated model
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("LLM API call failed with status {StatusCode}. Response: {Response}",
                        response.StatusCode, responseContent);
                    return string.Empty;
                }

                logger.LogTrace("LLM Raw Response: {ResponseContent}", responseContent);

                // Assuming GeminiResponse model is defined as you had before or similar
                var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseContent);

                string? textResponse = responseObject?.Candidates?[0]?.Content?.Parts?[0]?.Text;

                if (string.IsNullOrWhiteSpace(textResponse))
                {
                    logger.LogWarning("LLM returned a candidate but the text part is empty.");
                    return string.Empty;
                }

                // Clean the response: LLMs sometimes wrap JSON in ```json ... ```
                textResponse = textResponse.Trim();
                if (textResponse.StartsWith("```json"))
                {
                    textResponse = textResponse.Substring(7);
                }

                if (textResponse.StartsWith("```")) // Handle cases with just ```
                {
                    textResponse = textResponse.Substring(3);
                }

                if (textResponse.EndsWith("```"))
                {
                    textResponse = textResponse.Substring(0, textResponse.Length - 3);
                }

                textResponse = textResponse.Trim();


                return textResponse;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception during LLM API call.");
                return string.Empty;
            }
        }
    }

    // Assuming GeminiResponse model (you had this before)
    public class GeminiResponse
    {
        /* ... structure from your previous code or API doc ... */
        [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
        // finishReason, safetyRatings etc.
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")] public List<GeminiPart>? Parts { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }


    // --- gentest.Services/ITestCaseExtractionService.cs (Interface - likely exists) ---
    // namespace gentest.Services { public interface ITestCaseExtractionService { List<TestCase> ExtractTestCasesFromResponse(string llmResponse); } }

    // --- gentest.Services/TestCaseExtractionService.cs (Implementation needs to be robust) ---
    // using System.Text.Json;
    // using gentest.Models.Common;
    // namespace gentest.Services 
    // {
    //     public class TestCaseExtractionService : ITestCaseExtractionService
    //     {
    //         private readonly ILogger<TestCaseExtractionService> _logger;
    //         private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    //         {
    //             PropertyNameCaseInsensitive = true, // Important
    //             // Add converters for your enums if needed, though string enums should deserialize ok
    //         };
    //
    //         public TestCaseExtractionService(ILogger<TestCaseExtractionService> logger)
    //         {
    //             _logger = logger;
    //         }
    //
    //         public List<TestCase> ExtractTestCasesFromResponse(string llmResponse)
    //         {
    //             if (string.IsNullOrWhiteSpace(llmResponse)) return new List<TestCase>();
    //             try
    //             {
    //                 var testCases = JsonSerializer.Deserialize<List<TestCase>>(llmResponse, _jsonOptions);
    //                 return testCases ?? new List<TestCase>();
    //             }
    //             catch (JsonException ex)
    //             {
    //                 _logger.LogError(ex, "Failed to deserialize LLM response into List<TestCase>. Response was: {LLMResponse}", llmResponse.Substring(0, Math.Min(llmResponse.Length, 1000)));
    //                 // Attempt to find JSON array within potentially messy output
    //                 // (simple approach, more robust regex might be needed)
    //                 var startIndex = llmResponse.IndexOf("[");
    //                 var endIndex = llmResponse.LastIndexOf("]");
    //                 if (startIndex != -1 && endIndex != -1 && endIndex > startIndex) {
    //                     var potentialJsonArray = llmResponse.Substring(startIndex, endIndex - startIndex + 1);
    //                     try {
    //                         var testCases = JsonSerializer.Deserialize<List<TestCase>>(potentialJsonArray, _jsonOptions);
    //                         _logger.LogWarning("Successfully parsed a JSON array after initial deserialization failure from substring.");
    //                         return testCases ?? new List<TestCase>();
    //                     } catch (JsonException innerEx) {
    //                         _logger.LogError(innerEx, "Failed to deserialize even the extracted JSON array substring: {JsonSubstring}", potentialJsonArray.Substring(0, Math.Min(potentialJsonArray.Length, 1000)));
    //                     }
    //                 }
    //                 return new List<TestCase>();
    //             }
    //         }
    //     }
    // }
}