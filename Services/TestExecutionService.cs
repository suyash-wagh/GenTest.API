using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using gentest.Models.Common;
using gentest.Models.TestExecution;

namespace gentest.Services
{
    public class TestExecutionService : ITestExecutionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TestExecutionService> _logger;
        private readonly TestExecutorSettings _settings;

        public TestExecutionService(
            IHttpClientFactory httpClientFactory,
            IOptions<TestExecutorSettings> settings,
            ILogger<TestExecutionService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Executes multiple test cases against a specified base URL
        /// </summary>
        /// <param name="testCases">List of test cases to execute</param>
        /// <param name="baseUrl">Base URL of the API to test</param>
        /// <param name="globalHeaders">Optional global headers to include in all requests</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of test results</returns>
        public async Task<List<TestCaseResult>> ExecuteTestCasesAsync(
            List<TestCase> testCases, 
            string baseUrl, 
            Dictionary<string, string>? globalHeaders = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<TestCaseResult>();
            var testRun = new TestRun
            {
                Id = Guid.NewGuid().ToString(),
                StartTime = DateTime.UtcNow,
                BaseUrl = baseUrl,
                TotalTests = testCases.Count
            };

            _logger.LogInformation("Starting test run {TestRunId} with {Count} test cases against {BaseUrl}", 
                testRun.Id, testCases.Count, baseUrl);

            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            // Execute tests based on priority (High → Medium → Low)
            foreach (var priorityGroup in testCases
                .GroupBy(tc => tc.Priority)
                .OrderBy(g => GetPriorityOrder(g.Key)))
            {
                _logger.LogInformation("Executing {Count} {Priority} priority tests", 
                    priorityGroup.Count(), priorityGroup.Key);

                // For simple implementation, execute tests sequentially
                // Could be enhanced to run tests in parallel with configurable concurrency
                foreach (var testCase in priorityGroup)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Test run {TestRunId} cancelled", testRun.Id);
                        break;
                    }

                    try
                    {
                        var result = await ExecuteTestCaseAsync(testCase, baseUrl, globalHeaders, cancellationToken);
                        results.Add(result);

                        // Update metrics
                        testRun.TestsCompleted++;
                        if (result.Passed)
                        {
                            testRun.TestsPassed++;
                        }
                        else
                        {
                            testRun.TestsFailed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing test case {TestCaseId}", testCase.TestCaseId);
                        results.Add(new TestCaseResult
                        {
                            TestCaseId = testCase.TestCaseId,
                            TestCaseName = testCase.TestCaseName,
                            Passed = false,
                            ExecutionTime = 0,
                            ErrorMessage = $"Test execution error: {ex.Message}",
                            Exception = ex.ToString()
                        });
                        testRun.TestsCompleted++;
                        testRun.TestsFailed++;
                    }
                }
            }

            testRun.EndTime = DateTime.UtcNow;
            testRun.Duration = (testRun.EndTime.HasValue ? (testRun.EndTime.Value - testRun.StartTime).TotalMilliseconds : 0);
            
            _logger.LogInformation("Test run {TestRunId} completed with {PassCount} passed and {FailCount} failed tests in {Duration}ms",
                testRun.Id, testRun.TestsPassed, testRun.TestsFailed, testRun.Duration);

            // TODO: Persist test run results to database if needed
            
            return results;
        }

        /// <summary>
        /// Executes a single test case
        /// </summary>
        /// <param name="testCase">Test case to execute</param>
        /// <param name="baseUrl">Base URL of the API</param>
        /// <param name="globalHeaders">Optional global headers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Test case result</returns>
        private async Task<TestCaseResult> ExecuteTestCaseAsync(
            TestCase testCase, 
            string baseUrl, 
            Dictionary<string, string> globalHeaders = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = new Stopwatch();
            var result = new TestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestCaseName = testCase.TestCaseName,
                StartTime = DateTime.UtcNow
            };

            _logger.LogInformation("Executing test case {TestCaseId}: {TestCaseName}", 
                testCase.TestCaseId, testCase.TestCaseName);

            try
            {
                if (testCase.MockRequirements?.Count > 0)
                {
                    _logger.LogWarning("Test case {TestCaseId} requires mocks which are not implemented", testCase.TestCaseId);
                    foreach (var mock in testCase.MockRequirements)
                    {
                        _logger.LogDebug("Mock required for service {Service}, endpoint {Endpoint}", 
                            mock.Service, mock.Endpoint);
                    }
                }

                // Setup the HTTP client
                var client = _httpClientFactory.CreateClient("TestExecutor");
                client.Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);

                // Prepare the request URI with path parameters
                var path = testCase.Request.Path;
                if (path.StartsWith("/"))
                {
                    path = path.Substring(1);
                }

                // Replace path parameters with values
                if (testCase.Request.PathParameters != null && testCase.Request.PathParameters.Count > 0)
                {
                    foreach (var param in testCase.Request.PathParameters)
                    {
                        path = path.Replace($"{{{param.Key}}}", Uri.EscapeDataString(param.Value));
                    }
                }

                // Add query parameters
                var uriBuilder = new UriBuilder(new Uri(new Uri(baseUrl), path));
                var query = new System.Collections.Specialized.NameValueCollection();
                
                if (testCase.Request.QueryParameters != null && testCase.Request.QueryParameters.Count > 0)
                {
                    foreach (var param in testCase.Request.QueryParameters)
                    {
                        query.Add(param.Key, param.Value);
                    }
                }

                // Convert query parameters to query string
                if (query.Count > 0)
                {
                    var queryString = new StringBuilder();
                    for (int i = 0; i < query.Count; i++)
                    {
                        string key = query.GetKey(i);
                        foreach (string value in query.GetValues(i))
                        {
                            if (queryString.Length > 0)
                                queryString.Append('&');
                            queryString.AppendFormat("{0}={1}", Uri.EscapeDataString(key), Uri.EscapeDataString(value));
                        }
                    }
                    uriBuilder.Query = queryString.ToString();
                }

                // Create the HTTP request message
                var request = new HttpRequestMessage(new HttpMethod(testCase.Request.Method), uriBuilder.Uri);

                // Add headers
                if (globalHeaders != null)
                {
                    foreach (var header in globalHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                if (testCase.Request.Headers != null)
                {
                    foreach (var header in testCase.Request.Headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                // Add request body if needed
                if (testCase.Request.Body != null && 
                    (testCase.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) || 
                     testCase.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) || 
                     testCase.Request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
                {
                    string contentType = "application/json";
                    if (testCase.Request.Headers != null && 
                        testCase.Request.Headers.TryGetValue("Content-Type", out string headerContentType))
                    {
                        contentType = headerContentType;
                    }

                    string requestBody;
                    if (testCase.Request.Body is string bodyStr)
                    {
                        requestBody = bodyStr;
                    }
                    else
                    {
                        requestBody = JsonSerializer.Serialize(testCase.Request.Body);
                    }

                    request.Content = new StringContent(requestBody, Encoding.UTF8, contentType);
                }

                // Execute the request and measure time
                stopwatch.Start();
                var response = await client.SendAsync(request, cancellationToken);
                stopwatch.Stop();

                // Record response details
                result.ResponseStatusCode = (int)response.StatusCode;
                result.ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
                
                // Add content headers
                foreach (var header in response.Content.Headers)
                {
                    result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);
                }

                // Get response body
                var responseContent = await response.Content.ReadAsStringAsync();
                result.ResponseBody = responseContent;
                result.ExecutionTime = stopwatch.ElapsedMilliseconds;

                // Process assertions
                bool allAssertionsPassed = true;
                var failedAssertions = new List<AssertionResult>();

                if (testCase.Assertions != null && testCase.Assertions.Count > 0)
                {
                    foreach (var assertion in testCase.Assertions)
                    {
                        var assertionResult = EvaluateAssertion(assertion, response, responseContent, result.ExecutionTime);
                        if (!assertionResult.Passed)
                        {
                            allAssertionsPassed = false;
                            failedAssertions.Add(assertionResult);
                        }
                    }
                }
                else
                {
                    // If no assertions are specified, just check status code
                    var defaultAssertion = new AssertionResult
                    {
                        Type = "response_code",
                        Target = "status",
                        Condition = "equals",
                        ExpectedValue = testCase.ExpectedResponse.StatusCode,
                        ActualValue = result.ResponseStatusCode
                    };

                    if (result.ResponseStatusCode != testCase.ExpectedResponse.StatusCode)
                    {
                        allAssertionsPassed = false;
                        defaultAssertion.Passed = false;
                        defaultAssertion.Message = $"Expected status code {testCase.ExpectedResponse.StatusCode} but got {result.ResponseStatusCode}";
                        failedAssertions.Add(defaultAssertion);
                    }
                }

                result.Passed = allAssertionsPassed;
                result.FailedAssertions = failedAssertions;
                
                _logger.LogInformation(
                    "Test case {TestCaseId} {TestResult} in {ExecutionTime}ms with status code {StatusCode}", 
                    testCase.TestCaseId, 
                    result.Passed ? "PASSED" : "FAILED", 
                    result.ExecutionTime,
                    result.ResponseStatusCode);

                if (!result.Passed && failedAssertions.Count > 0)
                {
                    _logger.LogWarning("Failed assertions for test case {TestCaseId}:", testCase.TestCaseId);
                    foreach (var assertion in failedAssertions)
                    {
                        _logger.LogWarning("  - {Message}", assertion.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = ex.Message;
                result.Exception = ex.ToString();
                result.ExecutionTime = stopwatch.ElapsedMilliseconds;
                
                _logger.LogError(ex, "Exception executing test case {TestCaseId}", testCase.TestCaseId);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
            }
            return result;
        }

        /// <summary>
        /// Evaluates a single assertion against the response
        /// </summary>
        private AssertionResult EvaluateAssertion(
            TestAssertion assertion, 
            HttpResponseMessage response, 
            string responseBody,
            long responseTime)
        {
            var result = new AssertionResult
            {
                Type = assertion.Type,
                Target = assertion.Target,
                Condition = assertion.Condition,
                ExpectedValue = assertion.ExpectedValue,
                Passed = true
            };

            try
            {
                switch (assertion.Type.ToLower())
                {
                    case "response_code":
                        int statusCode = (int)response.StatusCode;
                        result.ActualValue = statusCode;
                        
                        if (!EvaluateCondition(assertion.Condition, statusCode, 
                            assertion.ExpectedValue is string ? int.Parse((string)assertion.ExpectedValue) : Convert.ToInt32(assertion.ExpectedValue)))
                        {
                            result.Passed = false;
                            result.Message = $"Status code assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}, got {statusCode}";
                        }
                        break;

                    case "response_time":
                        result.ActualValue = responseTime;
                        
                        if (!EvaluateCondition(assertion.Condition, responseTime, 
                            assertion.ExpectedValue is string ? long.Parse((string)assertion.ExpectedValue) : Convert.ToInt64(assertion.ExpectedValue)))
                        {
                            result.Passed = false;
                            result.Message = $"Response time assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}ms, got {responseTime}ms";
                        }
                        break;

                    case "header":
                        if (response.Headers.TryGetValues(assertion.Target, out var headerValues) ||
                            response.Content.Headers.TryGetValues(assertion.Target, out headerValues))
                        {
                            string headerValue = string.Join(",", headerValues);
                            result.ActualValue = headerValue;
                            
                            if (!EvaluateStringCondition(assertion.Condition, headerValue, assertion.ExpectedValue?.ToString()))
                            {
                                result.Passed = false;
                                result.Message = $"Header assertion failed for '{assertion.Target}': expected {assertion.Condition} '{assertion.ExpectedValue}', got '{headerValue}'";
                            }
                        }
                        else
                        {
                            result.Passed = assertion.Condition.ToLower() == "not_exists";
                            result.Message = result.Passed 
                                ? $"Header '{assertion.Target}' correctly does not exist" 
                                : $"Header assertion failed: '{assertion.Target}' does not exist";
                        }
                        break;

                    case "response_body":
                        // For simplicity, we handle JSON path with basic regex
                        // In a production app, use a proper JSON path library
                        if (assertion.Target == "length")
                        {
                            int bodyLength = responseBody.Length;
                            result.ActualValue = bodyLength;
                            
                            if (!EvaluateCondition(assertion.Condition, bodyLength, 
                                assertion.ExpectedValue is string ? int.Parse((string)assertion.ExpectedValue) : Convert.ToInt32(assertion.ExpectedValue)))
                            {
                                result.Passed = false;
                                result.Message = $"Body length assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}, got {bodyLength}";
                            }
                        }
                        else if (assertion.Target == "$")
                        {
                            // Match whole body
                            result.ActualValue = responseBody;
                            
                            if (!EvaluateStringCondition(assertion.Condition, responseBody, assertion.ExpectedValue?.ToString()))
                            {
                                result.Passed = false;
                                result.Message = $"Body content assertion failed: expected {assertion.Condition} '{assertion.ExpectedValue}'";
                            }
                        }
                        else if (responseBody.StartsWith("{") || responseBody.StartsWith("["))
                        {
                            // Try to parse as JSON and extract value using the target as a path
                            try
                            {
                                using (JsonDocument doc = JsonDocument.Parse(responseBody))
                                {
                                    JsonElement root = doc.RootElement;
                                    
                                    // Simple JSON path implementation
                                    // In production, use a proper JSON path library
                                    string[] pathParts = assertion.Target.Split('.');
                                    JsonElement current = root;
                                    
                                    // Navigate the JSON path
                                    foreach (var part in pathParts)
                                    {
                                        // Handle array indexing with [x]
                                        if (part.Contains("[") && part.Contains("]"))
                                        {
                                            var match = Regex.Match(part, @"(\w+)\[(\d+)\]");
                                            if (match.Success)
                                            {
                                                string propName = match.Groups[1].Value;
                                                int index = int.Parse(match.Groups[2].Value);
                                                
                                                current = current.GetProperty(propName);
                                                current = current[index];
                                            }
                                            else
                                            {
                                                throw new FormatException($"Invalid JSON path format: {part}");
                                            }
                                        }
                                        else
                                        {
                                            current = current.GetProperty(part);
                                        }
                                    }
                                    
                                    // Compare based on JSON element kind
                                    switch (current.ValueKind)
                                    {
                                        case JsonValueKind.String:
                                            string stringValue = current.GetString();
                                            result.ActualValue = stringValue;
                                            if (!EvaluateStringCondition(assertion.Condition, stringValue, assertion.ExpectedValue?.ToString()))
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: expected {assertion.Condition} '{assertion.ExpectedValue}', got '{stringValue}'";
                                            }
                                            break;
                                            
                                        case JsonValueKind.Number:
                                            if (current.TryGetInt64(out long longValue))
                                            {
                                                result.ActualValue = longValue;
                                                if (!EvaluateCondition(assertion.Condition, longValue, 
                                                    assertion.ExpectedValue is string ? long.Parse((string)assertion.ExpectedValue) : Convert.ToInt64(assertion.ExpectedValue)))
                                                {
                                                    result.Passed = false;
                                                    result.Message = $"JSON path '{assertion.Target}' assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}, got {longValue}";
                                                }
                                            }
                                            else if (current.TryGetDouble(out double doubleValue))
                                            {
                                                result.ActualValue = doubleValue;
                                                if (!EvaluateCondition(assertion.Condition, doubleValue, 
                                                    assertion.ExpectedValue is string ? double.Parse((string)assertion.ExpectedValue) : Convert.ToDouble(assertion.ExpectedValue)))
                                                {
                                                    result.Passed = false;
                                                    result.Message = $"JSON path '{assertion.Target}' assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}, got {doubleValue}";
                                                }
                                            }
                                            else
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: Unsupported number type";
                                            }
                                            break;

                                        case JsonValueKind.True:
                                        case JsonValueKind.False:
                                            bool boolValue = current.GetBoolean();
                                            result.ActualValue = boolValue;
                                            if (!EvaluateCondition(assertion.Condition, boolValue, 
                                                assertion.ExpectedValue is string ? bool.Parse((string)assertion.ExpectedValue) : Convert.ToBoolean(assertion.ExpectedValue)))
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: expected {assertion.Condition} {assertion.ExpectedValue}, got {boolValue}";
                                            }
                                            break;

                                        case JsonValueKind.Null:
                                            result.ActualValue = null;
                                            // Handle null specifically
                                            if (assertion.Condition.ToLower() == "exists")
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: expected value to exist, but got null";
                                            }
                                            else if (assertion.Condition.ToLower() == "not_exists")
                                            {
                                                result.Passed = true;
                                                result.Message = $"JSON path '{assertion.Target}' assertion passed: value correctly does not exist (is null)";
                                            }
                                            else if (assertion.Condition.ToLower() == "equals" && assertion.ExpectedValue != null)
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: expected '{assertion.ExpectedValue}', but got null";
                                            }
                                            else if (assertion.Condition.ToLower() == "equals" && assertion.ExpectedValue == null)
                                            {
                                                result.Passed = true;
                                                result.Message = $"JSON path '{assertion.Target}' assertion passed: expected null, and got null";
                                            }
                                            else
                                            {
                                                result.Passed = false;
                                                result.Message = $"JSON path '{assertion.Target}' assertion failed: Unsupported condition '{assertion.Condition}' for null value";
                                            }
                                            break;

                                        default:
                                            result.Passed = false;
                                            result.Message = $"JSON path '{assertion.Target}' assertion failed: Unsupported JSON element type {current.ValueKind}";
                                            break;
                                    }
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                result.Passed = false;
                                result.Message = $"JSON parsing error for assertion target '{assertion.Target}': {jsonEx.Message}";
                                _logger.LogWarning(jsonEx, "JSON parsing error during assertion evaluation");
                            }
                            catch (FormatException formatEx)
                            {
                                result.Passed = false;
                                result.Message = $"JSON path format error for assertion target '{assertion.Target}': {formatEx.Message}";
                                _logger.LogWarning(formatEx, "JSON path format error during assertion evaluation");
                            }
                            catch (KeyNotFoundException keyEx)
                            {
                                result.Passed = false;
                                result.Message = $"JSON path error: Property '{keyEx.Message}' not found in response body";
                                _logger.LogWarning(keyEx, "JSON path property not found during assertion evaluation");
                            }
                            catch (IndexOutOfRangeException indexEx)
                            {
                                result.Passed = false;
                                result.Message = $"JSON path error: Array index out of bounds";
                                _logger.LogWarning(indexEx, "JSON path array index out of bounds during assertion evaluation");
                            }
                            catch (Exception ex)
                            {
                                result.Passed = false;
                                result.ErrorMessage = $"Unexpected error during JSON path evaluation for '{assertion.Target}': {ex.Message}";
                                result.Exception = ex.ToString();
                                _logger.LogError(ex, "Unexpected error during JSON path evaluation");
                            }
                        }
                        else
                        {
                            result.Passed = false;
                            result.Message = $"Response body is not valid JSON for JSON path assertion target '{assertion.Target}'";
                        }
                        break;

                    default:
                        result.Passed = false;
                        result.Message = $"Unknown assertion type: {assertion.Type}";
                        _logger.LogWarning("Unknown assertion type encountered: {AssertionType}", assertion.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error evaluating assertion: {ex.Message}";
                result.Exception = ex.ToString();
                _logger.LogError(ex, "Error evaluating assertion");
            }

            return result;
        }

        /// <summary>
        /// Evaluates a condition for comparable types
        /// </summary>
        private bool EvaluateCondition<T>(string condition, T actual, T expected) where T : IComparable
        {
            return condition.ToLower() switch
            {
                "equals" => actual.CompareTo(expected) == 0,
                "greater_than" => actual.CompareTo(expected) > 0,
                "less_than" => actual.CompareTo(expected) < 0,
                "exists" => actual != null, // For non-nullable types, this is always true
                "not_exists" => actual == null, // For non-nullable types, this is always false
                _ => false, // Unknown condition
            };
        }

        /// <summary>
        /// Evaluates a condition for string types
        /// </summary>
        private bool EvaluateStringCondition(string condition, string actual, string expected)
        {
            return condition.ToLower() switch
            {
                "equals" => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
                "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                "exists" => !string.IsNullOrEmpty(actual),
                "not_exists" => string.IsNullOrEmpty(actual),
                _ => false, // Unknown condition
            };
        }

        /// <summary>
        /// Helper to get priority order for sorting
        /// </summary>
        private int GetPriorityOrder(string priority)
        {
            return priority.ToLower() switch
            {
                "high" => 1,
                "medium" => 2,
                "low" => 3,
                _ => 4, // Default to lowest priority for unknown values
            };
        }
    }
}