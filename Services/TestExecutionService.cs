using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenTest.Models.Common;
using GenTest.Models.TestExecution;

namespace GenTest.Services
{
    public class TestExecutionService : ITestExecutionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TestExecutionService> _logger;
        private readonly TestExecutorSettings _settings;
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public TestExecutionService(
            IHttpClientFactory httpClientFactory,
            IOptions<TestExecutorSettings> settings,
            ILogger<TestExecutionService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<TestRunResult> ExecuteTestRunAsync(
            List<TestCase> testCases,
            string baseUrl,
            Dictionary<string, object>? globalVariables = null,
            Dictionary<string, string>? globalHeaders = null,
            CancellationToken cancellationToken = default)
        {
            var runResult = new TestRunResult
            {
                StartTime = DateTime.UtcNow,
                BaseUrl = baseUrl,
                TotalTests = testCases.Count,
                GlobalVariables = globalVariables ?? new Dictionary<string, object>()
            };

            _logger.LogInformation("Starting test run {TestRunId} with {Count} test cases against {BaseUrl}",
                runResult.TestRunId, testCases.Count, baseUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogError("Base URL cannot be null or empty.");
                foreach (var tc in testCases)
                {
                    runResult.TestCaseResults.Add(new TestCaseResult
                    {
                        TestCaseId = tc.TestCaseId,
                        TestCaseName = tc.TestCaseName,
                        Status = TestStatus.Error,
                        ErrorMessage = "Base URL not provided."
                    });
                }
                runResult.EndTime = DateTime.UtcNow;
                return runResult;
            }

            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            var testCaseMap = testCases.ToDictionary(tc => tc.TestCaseId);
            var resultsMap = new ConcurrentDictionary<string, TestCaseResult>();
            var executionGraph = BuildExecutionGraph(testCases);

            var completedSuccessfully = new ConcurrentDictionary<string, bool>();

            foreach (var level in executionGraph)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var tasksInLevel = new List<Task>();
                foreach (var testCaseId in level)
                {
                    if (!testCaseMap.TryGetValue(testCaseId, out var testCase) || testCase.Skip)
                    {
                        var skippedResult = new TestCaseResult
                        {
                            TestCaseId = testCaseId,
                            TestCaseName = testCase?.TestCaseName,
                            Status = TestStatus.Skipped,
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow
                        };
                        resultsMap.TryAdd(testCaseId, skippedResult);
                        runResult.TestCaseResults.Add(skippedResult);
                        completedSuccessfully.TryAdd(testCaseId, false); // Skipped is not success for dependency
                        _logger.LogInformation("Skipping test case {TestCaseId}", testCaseId);
                        continue;
                    }

                    // Check prerequisites
                    bool prereqsMet = testCase.Prerequisites == null || testCase.Prerequisites.All(p => completedSuccessfully.ContainsKey(p) && completedSuccessfully[p]);
                    if (!prereqsMet)
                    {
                        var blockedResult = new TestCaseResult
                        {
                            TestCaseId = testCase.TestCaseId,
                            TestCaseName = testCase.TestCaseName,
                            Status = TestStatus.Blocked,
                            ErrorMessage = "One or more prerequisites failed or were not met.",
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow
                        };
                        resultsMap.TryAdd(testCase.TestCaseId, blockedResult);
                        runResult.TestCaseResults.Add(blockedResult);
                        completedSuccessfully.TryAdd(testCase.TestCaseId, false);
                        _logger.LogWarning("Test case {TestCaseId} blocked due to failed prerequisites.", testCase.TestCaseId);
                        continue;
                    }

                    // Create a combined set of variables for the current test case
                    var currentVariables = new Dictionary<string, object>(runResult.GlobalVariables, StringComparer.OrdinalIgnoreCase);
                    if (testCase.Prerequisites != null)
                    {
                        foreach (var prereqId in testCase.Prerequisites)
                        {
                            if (resultsMap.TryGetValue(prereqId, out var prereqResult) && prereqResult.ExtractedVariables != null)
                            {
                                foreach (var extractedVar in prereqResult.ExtractedVariables)
                                {
                                    currentVariables[extractedVar.Key] = extractedVar.Value; // Prereq vars can overwrite global
                                }
                            }
                        }
                    }
                    if (testCase.Variables != null)
                    {
                        foreach (var tcVar in testCase.Variables)
                        {
                            currentVariables[tcVar.Key] = tcVar.Value; // Test case vars can overwrite prereq/global
                        }
                    }


                    tasksInLevel.Add(Task.Run(async () =>
                    {
                        var result = await ExecuteSingleTestCaseAsync(testCase, baseUrl, globalHeaders, currentVariables, cancellationToken);
                        resultsMap.TryAdd(result.TestCaseId, result);
                        completedSuccessfully.TryAdd(result.TestCaseId, result.Status == TestStatus.Passed);
                    }, cancellationToken));
                }

                await Task.WhenAll(tasksInLevel); // Wait for all tasks in the current level to complete

                // Add results from this level to the main runResult list in order
                foreach (var testCaseId in level)
                {
                    if (resultsMap.TryGetValue(testCaseId, out var result) && !runResult.TestCaseResults.Any(r => r.TestCaseId == testCaseId))
                    {
                        runResult.TestCaseResults.Add(result);
                    }
                }
            }

            // Add any remaining results if tests were skipped mid-graph or due to cancellation
            foreach (var tc in testCases)
            {
                if (!runResult.TestCaseResults.Any(r => r.TestCaseId == tc.TestCaseId))
                {
                    if (resultsMap.TryGetValue(tc.TestCaseId, out var result))
                    {
                        runResult.TestCaseResults.Add(result);
                    }
                    else
                    {
                        // This case should ideally not happen if graph processing is correct
                        var missedResult = new TestCaseResult { TestCaseId = tc.TestCaseId, TestCaseName = tc.TestCaseName, Status = TestStatus.Error, ErrorMessage = "Test was not executed." };
                        runResult.TestCaseResults.Add(missedResult);
                        _logger.LogError("Test case {TestCaseId} was missed in execution cycle.", tc.TestCaseId);
                    }
                }
            }


            runResult.EndTime = DateTime.UtcNow;
            _logger.LogInformation("Test run {TestRunId} completed. Passed: {Passed}, Failed: {Failed}, Skipped: {Skipped}, Blocked: {Blocked}, Error: {Error}",
                runResult.TestRunId, runResult.TestsPassed, runResult.TestsFailed, runResult.TestsSkipped, runResult.TestsBlocked, runResult.TestsWithError);

            return runResult;
        }

        private List<List<string>> BuildExecutionGraph(List<TestCase> testCases)
        {
            var graph = new Dictionary<string, List<string>>();
            var inDegree = new Dictionary<string, int>();
            var testCaseIds = new HashSet<string>(testCases.Select(tc => tc.TestCaseId));

            foreach (var tc in testCases)
            {
                graph[tc.TestCaseId] = new List<string>();
                inDegree[tc.TestCaseId] = 0;
            }

            foreach (var tc in testCases)
            {
                if (tc.Prerequisites != null)
                {
                    foreach (var prereqId in tc.Prerequisites)
                    {
                        if (testCaseIds.Contains(prereqId) && testCaseIds.Contains(tc.TestCaseId) && prereqId != tc.TestCaseId)
                        {
                            // Ensure prereqId exists as a key in graph before adding tc.TestCaseId to its list
                            if (!graph.ContainsKey(prereqId)) graph[prereqId] = new List<string>();
                            graph[prereqId].Add(tc.TestCaseId);

                            // Ensure tc.TestCaseId exists as a key in inDegree before incrementing
                            if (!inDegree.ContainsKey(tc.TestCaseId)) inDegree[tc.TestCaseId] = 0;
                            inDegree[tc.TestCaseId]++;
                        }
                        else if (prereqId == tc.TestCaseId)
                        {
                            _logger.LogWarning("Test case {TestCaseId} has a prerequisite on itself. This will be ignored.", tc.TestCaseId);
                        }
                        else if (!testCaseIds.Contains(prereqId))
                        {
                            _logger.LogWarning("Test case {TestCaseId} has an unknown prerequisite '{PrereqId}'. This prerequisite will be ignored.", tc.TestCaseId, prereqId);
                            // Optionally, you could mark the test case as blocked or error here if strict dependency checking is required.
                        }
                    }
                }
            }

            var levels = new List<List<string>>();
            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

            while (queue.Count > 0)
            {
                var currentLevel = new List<string>();
                int levelSize = queue.Count;
                for (int i = 0; i < levelSize; i++)
                {
                    var u = queue.Dequeue();
                    currentLevel.Add(u);

                    if (graph.TryGetValue(u, out var neighbors))
                    {
                        foreach (var v in neighbors)
                        {
                            if (inDegree.ContainsKey(v))
                            {
                                inDegree[v]--;
                                if (inDegree[v] == 0)
                                {
                                    queue.Enqueue(v);
                                }
                            }
                        }
                    }
                }
                if (currentLevel.Any()) levels.Add(currentLevel);
            }

            // Check for cycles (if not all test cases are in levels)
            int testsInLevels = levels.Sum(l => l.Count);
            if (testsInLevels < testCases.Count(tc => !tc.Skip))
            {
                var testsWithCycles = testCaseIds.Except(levels.SelectMany(l => l));
                _logger.LogError("Circular dependency detected or missing test cases in graph. Tests involved or unreached: {TestIds}", string.Join(", ", testsWithCycles));
                // Handle cycle: either throw error or mark involved tests as Error/Blocked.
                // For now, they simply won't be executed by the level-based approach.
                // Add them to levels as a final error level to ensure they are reported.
                var errorLevel = testsWithCycles.ToList();
                if (errorLevel.Any()) levels.Add(errorLevel); // these will likely be marked as blocked or error
            }

            return levels;
        }


        private async Task<TestCaseResult> ExecuteSingleTestCaseAsync(
            TestCase testCase,
            string baseUrl,
            Dictionary<string, string>? globalHeaders,
            Dictionary<string, object> variables,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new TestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestCaseName = testCase.TestCaseName,
                StartTime = DateTime.UtcNow,
                Status = TestStatus.Running,
                RequestMethod = testCase.Request.Method.ToString()
            };

            _logger.LogInformation("Executing test case {TestCaseId}: {TestCaseName}", testCase.TestCaseId, testCase.TestCaseName);

            if (testCase.MockRequirements?.Any() == true)
            {
                _logger.LogInformation("Test case {TestCaseId} has mock requirements. Ensure mocks are configured externally.", testCase.TestCaseId);
            }

            int attempt = 0;
            bool success = false;
            HttpResponseMessage? response = null;
            string responseContent = string.Empty;

            while (attempt <= _settings.MaxRetries && !success && !cancellationToken.IsCancellationRequested)
            {
                if (attempt > 0)
                {
                    _logger.LogInformation("Retrying test case {TestCaseId} (Attempt {Attempt}) after delay...", testCase.TestCaseId, attempt);
                    await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken);
                }
                attempt++;
                result.RetryAttempts = attempt - 1;

                try
                {
                    // 1. Prepare HttpClient
                    var client = _httpClientFactory.CreateClient("TestExecutor"); // Consider named client configuration for SSL, etc.
                    if (_settings.AllowUntrustedSSL)
                    {
                        // This is a simplified way; proper handler setup is better
                        // This client instance might need specific handler if SSL validation is disabled
                        // For brevity, assuming HttpClientFactory is configured to handle this
                        // or you'd create a new HttpClient with a custom handler here if needed.
                        // Example:
                        // var handler = new HttpClientHandler();
                        // handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                        // client = new HttpClient(handler);
                    }
                    client.Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);

                    // 2. Substitute variables in path, query, headers, body
                    string processedPath = ReplaceVariables(testCase.Request.Path, variables);

                    // Replace path parameters
                    if (testCase.Request.PathParameters != null)
                    {
                        foreach (var param in testCase.Request.PathParameters)
                        {
                            processedPath = processedPath.Replace($"{{{param.Key}}}", Uri.EscapeDataString(ReplaceVariables(param.Value, variables)));
                        }
                    }

                    if (processedPath.StartsWith("/")) processedPath = processedPath.Substring(1);
                    var uriBuilder = new UriBuilder(new Uri(new Uri(baseUrl), processedPath));

                    // Add query parameters
                    var query = System.Web.HttpUtility.ParseQueryString(string.Empty); // Using HttpUtility for robust query building
                    if (testCase.Request.QueryParameters != null)
                    {
                        foreach (var param in testCase.Request.QueryParameters)
                        {
                            query[param.Key] = ReplaceVariables(param.Value, variables);
                        }
                    }
                    uriBuilder.Query = query.ToString();
                    result.RequestUrl = uriBuilder.Uri.ToString();

                    // 3. Create HttpRequestMessage
                    var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(testCase.Request.Method.ToString()), uriBuilder.Uri);
                    result.RequestHeaders = new Dictionary<string, string>();

                    // 4. Apply Authentication
                    ApplyAuthentication(request, testCase.Authentication, variables);

                    // Add global headers
                    if (globalHeaders != null)
                    {
                        foreach (var header in globalHeaders)
                        {
                            var processedValue = ReplaceVariables(header.Value, variables);
                            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                continue; // Content-Type must be set on request.Content.Headers
                            request.Headers.TryAddWithoutValidation(header.Key, processedValue);
                            result.RequestHeaders[header.Key] = processedValue;
                        }
                    }
                    // Add test case specific headers (can override global)
                    if (testCase.Request.Headers != null)
                    {
                        foreach (var header in testCase.Request.Headers)
                        {
                            var processedValue = ReplaceVariables(header.Value, variables);
                            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                continue; // Content-Type must be set on request.Content.Headers
                            request.Headers.Remove(header.Key); // Remove if already added by global
                            request.Headers.TryAddWithoutValidation(header.Key, processedValue);
                            result.RequestHeaders[header.Key] = processedValue;
                        }
                    }

                    // 5. Prepare Request Body
                    string? requestBodyString = null;
                    string effectiveContentType = testCase.Request.ContentType ?? DetermineContentType(testCase.Request);

                    if (testCase.Request.Body != null &&
                        (testCase.Request.Method == HttpMethodExtended.POST ||
                         testCase.Request.Method == HttpMethodExtended.PUT ||
                         testCase.Request.Method == HttpMethodExtended.PATCH))
                    {
                        if (testCase.Request.Body is string strBody)
                        {
                            requestBodyString = ReplaceVariables(strBody, variables);
                        }
                        else // Assume it's an object to be serialized (likely JSON)
                        {
                            // Serialize, then replace variables. This is tricky.
                            // A better approach for objects would be to deserialize to JsonNode, replace, then reserialize.
                            // For simplicity now, assume if Body is object, it's for JSON and variables are already strings.
                            requestBodyString = JsonSerializer.Serialize(testCase.Request.Body, _jsonSerializerOptions);
                            requestBodyString = ReplaceVariables(requestBodyString, variables); // Replace after serialization
                        }
                        request.Content = new StringContent(requestBodyString, Encoding.UTF8, effectiveContentType);
                    }
                    else if (testCase.Request.FormParameters?.Any() == true && effectiveContentType.Contains("application/x-www-form-urlencoded"))
                    {
                        var formContent = testCase.Request.FormParameters.ToDictionary(kvp => kvp.Key, kvp => ReplaceVariables(kvp.Value, variables));
                        request.Content = new FormUrlEncodedContent(formContent);
                        requestBodyString = await request.Content.ReadAsStringAsync(cancellationToken);
                    }
                    else if (testCase.Request.FileParameters?.Any() == true && effectiveContentType.Contains("multipart/form-data"))
                    {
                        var multipartContent = new MultipartFormDataContent();
                        if (testCase.Request.Body is Dictionary<string, string> formFields) // Add other form fields if Body is used for it
                        {
                            foreach (var field in formFields)
                            {
                                multipartContent.Add(new StringContent(ReplaceVariables(field.Value, variables)), field.Key);
                            }
                        }
                        foreach (var fileParam in testCase.Request.FileParameters)
                        {
                            byte[] fileBytes;
                            if (!string.IsNullOrEmpty(fileParam.FileContentBase64))
                            {
                                fileBytes = Convert.FromBase64String(fileParam.FileContentBase64);
                            }
                            else if (!string.IsNullOrEmpty(fileParam.FilePath))
                            {
                                // Ensure file path variables are resolved if any
                                var resolvedFilePath = ReplaceVariables(fileParam.FilePath, variables);
                                if (!File.Exists(resolvedFilePath))
                                {
                                    throw new FileNotFoundException($"File not found for upload: {resolvedFilePath}", resolvedFilePath);
                                }
                                fileBytes = await File.ReadAllBytesAsync(resolvedFilePath, cancellationToken);
                            }
                            else
                            {
                                throw new ArgumentException($"Either FilePath or FileContentBase64 must be provided for file parameter '{fileParam.Name}'.");
                            }
                            var fileContent = new ByteArrayContent(fileBytes);
                            if (!string.IsNullOrEmpty(fileParam.ContentType))
                            {
                                fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileParam.ContentType);
                            }
                            multipartContent.Add(fileContent, fileParam.Name, fileParam.FileName);
                        }
                        request.Content = multipartContent;
                        // requestBodyString for multipart is complex, usually not logged in full
                        requestBodyString = $"Multipart form data with {testCase.Request.FileParameters.Count} file(s).";
                    }
                    result.RequestBody = requestBodyString;
                    if (request.Content?.Headers.ContentType != null)
                    { // Ensure Content-Type header is captured
                        result.RequestHeaders["Content-Type"] = request.Content.Headers.ContentType.ToString();
                    }


                    // 6. Execute Request
                    var requestStopwatch = Stopwatch.StartNew();
                    response = await client.SendAsync(request, cancellationToken);
                    requestStopwatch.Stop();
                    result.DurationMs = requestStopwatch.ElapsedMilliseconds; // This is response time

                    // 7. Record Response Details
                    result.ResponseStatusCode = (int)response.StatusCode;
                    result.ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
                    if (response.Content?.Headers != null)
                    {
                        foreach (var header in response.Content.Headers)
                        {
                            result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);
                        }
                    }
                    responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    result.ResponseBody = responseContent;

                    // 8. Process Assertions
                    result.AssertionResults = EvaluateAssertions(testCase, response, responseContent, result.DurationMs, variables);
                    bool allAssertionsPassed = result.AssertionResults.All(a => a.Passed);

                    if (allAssertionsPassed)
                    {
                        // If no assertions are defined but ExpectedResponse.StatusCode is, check it.
                        if (!testCase.Assertions.Any() && testCase.ExpectedResponse != null)
                        {
                            if (result.ResponseStatusCode == testCase.ExpectedResponse.StatusCode)
                            {
                                result.Status = TestStatus.Passed;
                            }
                            else
                            {
                                result.Status = TestStatus.Failed;
                                result.AssertionResults.Add(new AssertionResult
                                {
                                    Description = "Default status code check",
                                    Type = AssertionType.StatusCode,
                                    Condition = AssertionCondition.Equals,
                                    ExpectedValue = testCase.ExpectedResponse.StatusCode,
                                    ActualValue = result.ResponseStatusCode,
                                    Passed = false,
                                    Message = $"Expected status code {testCase.ExpectedResponse.StatusCode} but got {result.ResponseStatusCode}"
                                });
                            }
                        }
                        else if (!testCase.Assertions.Any() && testCase.ExpectedResponse == null)
                        {
                            // No assertions, no expected response = pass (or define default behavior)
                            result.Status = TestStatus.Passed; // Or perhaps requires at least one assertion?
                        }
                        else
                        {
                            result.Status = TestStatus.Passed;
                        }
                    }
                    else
                    {
                        result.Status = TestStatus.Failed;
                    }

                    success = (result.Status == TestStatus.Passed); // For retry loop condition

                    // 9. Extract Variables
                    if (success && testCase.ExtractVariables?.Any() == true)
                    {
                        result.ExtractedVariables = ExtractVariablesFromResponse(testCase.ExtractVariables, response, responseContent, variables);
                    }

                    _logger.LogInformation(
                        "Test case {TestCaseId} {Status} in {DurationMs}ms with status code {StatusCode}",
                        testCase.TestCaseId, result.Status, result.DurationMs, result.ResponseStatusCode);

                    if (result.Status == TestStatus.Failed)
                    {
                        _logger.LogWarning("Failed assertions for test case {TestCaseId}:", testCase.TestCaseId);
                        foreach (var ar in result.AssertionResults.Where(r => !r.Passed))
                        {
                            _logger.LogWarning("  - {Message} (Actual: {Actual}, Expected: {Expected})", ar.Message, ar.ActualValue, ar.ExpectedValue);
                        }
                    }

                }
                catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Test case {TestCaseId} cancelled.", testCase.TestCaseId);
                    result.Status = TestStatus.Skipped;
                    result.ErrorMessage = "Test execution was cancelled.";
                    result.StackTrace = ex.ToString();
                    break; // Exit retry loop
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "HTTP request exception for test case {TestCaseId} (Attempt {Attempt})", testCase.TestCaseId, attempt);
                    result.Status = TestStatus.Error;
                    result.ErrorMessage = $"HTTP Request Error: {ex.Message} (StatusCode: {ex.StatusCode})";
                    result.StackTrace = ex.ToString();
                    // Potentially retryable, loop will continue if MaxRetries not reached
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception executing test case {TestCaseId} (Attempt {Attempt})", testCase.TestCaseId, attempt);
                    result.Status = TestStatus.Error;
                    result.ErrorMessage = ex.Message;
                    result.StackTrace = ex.ToString();
                    if (attempt > _settings.MaxRetries) break; // Don't retry for non-HTTP exceptions unless configured
                }
            } // End of retry loop

            stopwatch.Stop();
            //result.DurationMs = stopwatch.ElapsedMilliseconds; // This is total execution time including retries
            result.EndTime = DateTime.UtcNow;
            return result;
        }

        private string DetermineContentType(TestRequest request)
        {
            if (!string.IsNullOrEmpty(request.ContentType)) return request.ContentType;
            if (request.FileParameters?.Any() == true) return "multipart/form-data";
            if (request.FormParameters?.Any() == true) return "application/x-www-form-urlencoded";
            if (request.Body != null) return "application/json"; // Default for body content
            return "application/json"; // Overall default
        }

        private void ApplyAuthentication(HttpRequestMessage request, AuthenticationDetails? auth, Dictionary<string, object> variables)
        {
            if (auth == null) return;

            switch (auth.Type)
            {
                case AuthenticationType.Basic:
                    if (!string.IsNullOrEmpty(auth.Username) && auth.Password != null) // Password can be empty string
                    {
                        var username = ReplaceVariables(auth.Username, variables);
                        var password = ReplaceVariables(auth.Password, variables);
                        var basicAuthHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuthHeader);
                    }
                    break;
                case AuthenticationType.BearerToken:
                    if (!string.IsNullOrEmpty(auth.Token))
                    {
                        var token = ReplaceVariables(auth.Token, variables);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    break;
                case AuthenticationType.ApiKey:
                    if (!string.IsNullOrEmpty(auth.ApiKeyHeaderName) && !string.IsNullOrEmpty(auth.ApiKeyValue))
                    {
                        var keyName = ReplaceVariables(auth.ApiKeyHeaderName, variables);
                        var keyValue = ReplaceVariables(auth.ApiKeyValue, variables);
                        if (auth.ApiKeyLocation == ApiKeyLocation.Header)
                        {
                            request.Headers.TryAddWithoutValidation(keyName, keyValue);
                        }
                        else // QueryParameter
                        {
                            var uriBuilder = new UriBuilder(request.RequestUri!);
                            var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                            query[keyName] = keyValue;
                            uriBuilder.Query = query.ToString();
                            request.RequestUri = uriBuilder.Uri;
                        }
                    }
                    break;
            }
        }

        private string ReplaceVariables(string? template, IDictionary<string, object> variables)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (variables == null || !variables.Any()) return template;

            // Simple {{variable_name}} replacement
            return Regex.Replace(template, @"\{\{(.+?)\}\}", match =>
            {
                string key = match.Groups[1].Value.Trim();
                if (variables.TryGetValue(key, out object? value))
                {
                    return value?.ToString() ?? string.Empty;
                }
                _logger.LogWarning("Variable '{{{{{Key}}}}}' not found, replaced with empty string.", key);
                return string.Empty; // Or return match.Value to leave it as is, or throw
            });
        }

        private List<AssertionResult> EvaluateAssertions(
    TestCase testCase,
    HttpResponseMessage response,
    string responseBody,
    long responseTimeMs,
    IDictionary<string, object> variables)
        {
            var results = new List<AssertionResult>();
            if (testCase.Assertions == null || !testCase.Assertions.Any())
            {
                return results;
            }

            foreach (var assertion in testCase.Assertions)
            {
                var ar = new AssertionResult
                {
                    Description = assertion.Description,
                    Type = assertion.Type,
                    Target = assertion.Target,
                    Condition = assertion.Condition,
                    ExpectedValue = assertion.ExpectedValue is string s ? ReplaceVariables(s, variables) : assertion.ExpectedValue,
                    Passed = false // Default to false
                };

                try
                {
                    object? actualValue = null;
                    bool evaluationResult = false;

                    var expectedValue = UnwrapJsonElement(ar.ExpectedValue);

                    switch (assertion.Type)
                    {
                        case AssertionType.StatusCode:
                            actualValue = (int)response.StatusCode;
                            evaluationResult = EvaluateNumericCondition(
                                assertion.Condition,
                                Convert.ToInt64(actualValue),
                                Convert.ToInt64(expectedValue)
                            );
                            break;

                        case AssertionType.ResponseTime:
                            actualValue = responseTimeMs;
                            evaluationResult = EvaluateNumericCondition(assertion.Condition, responseTimeMs, Convert.ToInt64(expectedValue));
                            break;

                        case AssertionType.HeaderExists:
                            actualValue = response.Headers.Concat(response.Content.Headers)
                                .Any(h => h.Key.Equals(assertion.Target, StringComparison.OrdinalIgnoreCase));
                            evaluationResult = EvaluateBooleanCondition(assertion.Condition, (bool)actualValue, true);
                            break;

                        case AssertionType.HeaderValue:
                            var headers = response.Headers.Concat(response.Content.Headers)
                                .Where(h => h.Key.Equals(assertion.Target, StringComparison.OrdinalIgnoreCase))
                                .Select(h => string.Join(",", h.Value));
                            actualValue = headers.FirstOrDefault();
                            evaluationResult = EvaluateStringCondition(
                                assertion.Condition,
                                (string?)actualValue,
                                expectedValue?.ToString()
                            );
                            break;

                        case AssertionType.BodyContainsString:
                            actualValue = responseBody;
                            evaluationResult = EvaluateStringCondition(
                                assertion.Condition,
                                responseBody,
                                expectedValue?.ToString() // Use unwrapped value as string
                            );
                            break;

                        case AssertionType.BodyEqualsString:
                            actualValue = responseBody;
                            evaluationResult = EvaluateStringCondition(
                                assertion.Condition,
                                responseBody,
                                expectedValue?.ToString()
                            );
                            break;

                        case AssertionType.BodyMatchesRegex:
                            actualValue = responseBody;
                            evaluationResult = EvaluateStringCondition(
                                assertion.Condition,
                                responseBody,
                                expectedValue?.ToString(),
                                isRegex: true
                            );
                            break;

                        case AssertionType.JsonPathValue:
                        case AssertionType.JsonPathExists:
                        case AssertionType.JsonPathNotExists:
                            if (string.IsNullOrWhiteSpace(responseBody) || (!responseBody.TrimStart().StartsWith("{") && !responseBody.TrimStart().StartsWith("[")))
                            {
                                ar.Message = "Response body is not valid JSON or is empty.";
                                actualValue = null; evaluationResult = false;
                            }
                            else
                            {
                                try
                                {
                                    var jsonNode = JsonNode.Parse(responseBody);
                                    var selectedNode = SelectJsonNode(jsonNode, assertion.Target);

                                    if (assertion.Type == AssertionType.JsonPathExists)
                                    {
                                        actualValue = selectedNode != null;
                                        evaluationResult = EvaluateBooleanCondition(assertion.Condition, (bool)actualValue, true);
                                    }
                                    else if (assertion.Type == AssertionType.JsonPathNotExists)
                                    {
                                        actualValue = selectedNode == null;
                                        evaluationResult = EvaluateBooleanCondition(assertion.Condition, (bool)actualValue, true);
                                    }
                                    else // JsonPathValue
                                    {
                                        if (selectedNode == null)
                                        {
                                            ar.Message = $"JSON Path '{assertion.Target}' not found in response.";
                                            actualValue = null; evaluationResult = false;
                                        }
                                        else
                                        {
                                            actualValue = GetJsonNodeValue(selectedNode);
                                            evaluationResult = EvaluateGeneralCondition(assertion.Condition, actualValue, ar.ExpectedValue);
                                        }
                                    }
                                }
                                catch (JsonException jsonEx)
                                {
                                    ar.Message = $"JSON parsing error for target '{assertion.Target}': {jsonEx.Message}";
                                    _logger.LogWarning(jsonEx, "JSON parsing error during assertion evaluation for {Target}", assertion.Target);
                                    actualValue = null; evaluationResult = false;
                                }
                                catch (Exception pathEx)
                                {
                                    ar.Message = $"Error processing JSONPath '{assertion.Target}': {pathEx.Message}";
                                    _logger.LogWarning(pathEx, "Error processing JSONPath {Target}", assertion.Target);
                                    actualValue = null; evaluationResult = false;
                                }
                            }
                            break;

                        case AssertionType.ArrayLength:
                            try
                            {
                                var jsonNode = JsonNode.Parse(responseBody);
                                JsonNode? targetNode = jsonNode;
                                if (!string.IsNullOrEmpty(assertion.Target) && assertion.Target != "$")
                                {
                                    targetNode = SelectJsonNode(jsonNode, assertion.Target);
                                }

                                if (targetNode is JsonArray jsonArray)
                                {
                                    actualValue = jsonArray.Count;
                                    evaluationResult = EvaluateNumericCondition(assertion.Condition, (int)actualValue, Convert.ToInt32(expectedValue));
                                }
                                else
                                {
                                    ar.Message = $"Target '{assertion.Target}' for ArrayLength assertion is not a JSON array.";
                                    actualValue = null; evaluationResult = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                ar.Message = $"Error processing ArrayLength assertion: {ex.Message}";
                                _logger.LogWarning(ex, "Error processing ArrayLength assertion for {Target}", assertion.Target);
                                actualValue = null; evaluationResult = false;
                            }
                            break;

                        case AssertionType.ArrayContains:
                            try
                            {
                                var jsonNode = JsonNode.Parse(responseBody);
                                JsonNode? targetNode = jsonNode;
                                if (!string.IsNullOrEmpty(assertion.Target) && assertion.Target != "$")
                                {
                                    targetNode = SelectJsonNode(jsonNode, assertion.Target);
                                }

                                if (targetNode is JsonArray jsonArray)
                                {
                                    bool found = false;
                                    foreach (var item in jsonArray)
                                    {
                                        var itemValue = GetJsonNodeValue(item);
                                        if (object.Equals(itemValue, ConvertType(ar.ExpectedValue, itemValue?.GetType() ?? typeof(string))))
                                        {
                                            found = true;
                                            break;
                                        }
                                    }
                                    actualValue = found;
                                    evaluationResult = EvaluateBooleanCondition(assertion.Condition, found, true);
                                }
                                else
                                {
                                    ar.Message = $"Target '{assertion.Target}' for ArrayContains assertion is not a JSON array.";
                                    actualValue = null; evaluationResult = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                ar.Message = $"Error processing ArrayContains assertion: {ex.Message}";
                                _logger.LogWarning(ex, "Error processing ArrayContains assertion for {Target}", assertion.Target);
                                actualValue = null; evaluationResult = false;
                            }
                            break;

                        case AssertionType.JsonSchemaValidation:
                            ar.Message = "JsonSchemaValidation not yet implemented.";
                            _logger.LogWarning("JsonSchemaValidation for assertion on {Target} is not yet implemented.", assertion.Target);
                            actualValue = responseBody; evaluationResult = false;
                            break;

                        case AssertionType.XmlPathValue:
                        case AssertionType.XmlSchemaValidation:
                            ar.Message = "XML assertions not yet implemented.";
                            _logger.LogWarning("XML assertion type {AssertionType} for target {Target} is not yet implemented.", assertion.Type, assertion.Target);
                            actualValue = responseBody; evaluationResult = false;
                            break;

                        default:
                            ar.Message = $"Unknown assertion type: {assertion.Type}";
                            _logger.LogWarning("Unknown assertion type encountered: {AssertionType}", assertion.Type);
                            actualValue = null; evaluationResult = false;
                            break;
                    }

                    ar.ActualValue = actualValue;
                    ar.Passed = evaluationResult;
                    if (!ar.Passed && string.IsNullOrEmpty(ar.Message))
                    {
                        ar.Message = $"Assertion failed. Expected: '{ar.ExpectedValue}' ({assertion.Condition}), Actual: '{ar.ActualValue}'.";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error evaluating assertion '{AssertionDescription}' (Type: {AssertionType}, Target: {AssertionTarget})", assertion.Description, assertion.Type, assertion.Target);
                    ar.Passed = false;
                    ar.Message = $"Error during assertion evaluation: {ex.Message}";
                    ar.ActualValue = "Error occurred during evaluation";
                }
                results.Add(ar);
            }
            return results;
        }

        // Simplified JsonNode selector. For robust JsonPath, use a library like JsonPath.Net or Manatee.Json.
        private JsonNode? SelectJsonNode(JsonNode? root, string? path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path) || path == "$")
            {
                return root;
            }

            // Remove "$.", "$." , or "$[" from the beginning if present
            if (path.StartsWith("$.")) path = path.Substring(2);
            else if (path.StartsWith("$[")) path = path.Substring(1); // Keep the first [ for array indexing

            var segments = path.Split(new[] { '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            JsonNode? current = root;

            foreach (var segment in segments)
            {
                if (current == null) return null;

                if (int.TryParse(segment, out int index)) // Array index
                {
                    if (current is JsonArray arr)
                    {
                        current = index < arr.Count ? arr[index] : null;
                    }
                    else return null; // Not an array or index out of bounds
                }
                else // Object property
                {
                    if (current is JsonObject obj)
                    {
                        current = obj.TryGetPropertyValue(segment, out var node) ? node : null;
                    }
                    else return null; // Not an object
                }
            }
            return current;
        }

        private object? GetJsonNodeValue(JsonNode? node)
        {
            if (node == null) return null;
            if (node is JsonValue jsonValue)
            {
                // Try to get the underlying value correctly
                if (jsonValue.TryGetValue<string>(out var strVal)) return strVal;
                if (jsonValue.TryGetValue<int>(out var intVal)) return intVal;
                if (jsonValue.TryGetValue<long>(out var longVal)) return longVal; // Important for larger numbers
                if (jsonValue.TryGetValue<double>(out var dblVal)) return dblVal;
                if (jsonValue.TryGetValue<bool>(out var boolVal)) return boolVal;
                if (jsonValue.TryGetValue<decimal>(out var decVal)) return decVal;
                // If it's a JsonValue but doesn't match any above, it might be explicitly null
                if (jsonValue.ToJsonString() == "null") return null;
            }
            // For JsonObject or JsonArray, return their string representation or handle as complex type
            return node.ToJsonString(); // Fallback to string representation for complex types or unhandled JsonValue types
        }

        private object? ConvertType(object? value, Type targetType)
        {
            if (value == null) return null;
            if (targetType == null) return value; // Cannot convert if target type is unknown

            try
            {
                if (value.GetType() == targetType) return value;

                if (targetType.IsEnum)
                    return Enum.Parse(targetType, value.ToString()!, true);

                if (targetType == typeof(Guid))
                    return Guid.Parse(value.ToString()!);

                return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Type conversion failed for value '{Value}' to type '{TargetType}'. Returning original value.", value, targetType.Name);
                return value; // Or throw, or return null, depending on desired strictness
            }
        }


        private bool EvaluateNumericCondition(AssertionCondition condition, IComparable actual, object? expectedObj)
        {
            if (expectedObj == null) return false; // Cannot compare with null for numeric conditions typically

            IComparable expected;
            try
            {
                // Attempt to convert expectedObj to the type of actual, or a common numeric type like double or long
                if (actual is double || actual is float || actual is decimal)
                    expected = Convert.ToDouble(expectedObj, System.Globalization.CultureInfo.InvariantCulture);
                else if (actual is long || actual is int || actual is short || actual is byte)
                    expected = Convert.ToInt64(expectedObj, System.Globalization.CultureInfo.InvariantCulture);
                else // Fallback or throw if types are not compatible numeric types
                    expected = (IComparable)Convert.ChangeType(expectedObj, actual.GetType(), System.Globalization.CultureInfo.InvariantCulture);

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not convert expected value '{ExpectedObj}' to compare with actual numeric value '{Actual}'.", expectedObj, actual);
                return false;
            }


            int comparisonResult = actual.CompareTo(expected);
            return condition switch
            {
                AssertionCondition.Equals => comparisonResult == 0,
                AssertionCondition.NotEquals => comparisonResult != 0,
                AssertionCondition.GreaterThan => comparisonResult > 0,
                AssertionCondition.LessThan => comparisonResult < 0,
                AssertionCondition.GreaterThanOrEquals => comparisonResult >= 0,
                AssertionCondition.LessThanOrEquals => comparisonResult <= 0,
                _ => false,
            };
        }

        private bool EvaluateStringCondition(AssertionCondition condition, string? actual, string? expected, bool isRegex = false)
        {
            expected ??= string.Empty; // Treat null expected as empty for some conditions

            return condition switch
            {
                AssertionCondition.Equals => string.Equals(actual, expected, StringComparison.Ordinal), // Case-sensitive typically desired for API tests
                AssertionCondition.NotEquals => !string.Equals(actual, expected, StringComparison.Ordinal),
                AssertionCondition.Contains => actual?.Contains(expected, StringComparison.Ordinal) ?? false,
                AssertionCondition.NotContains => !(actual?.Contains(expected, StringComparison.Ordinal) ?? false),
                AssertionCondition.MatchesRegex => actual != null && Regex.IsMatch(actual, expected),
                AssertionCondition.NotMatchesRegex => actual == null || !Regex.IsMatch(actual, expected),
                AssertionCondition.IsEmpty => string.IsNullOrEmpty(actual),
                AssertionCondition.IsNotEmpty => !string.IsNullOrEmpty(actual),
                AssertionCondition.IsNull => actual == null, // Distinct from IsEmpty
                AssertionCondition.IsNotNull => actual != null,
                _ => false,
            };
        }

        private bool EvaluateBooleanCondition(AssertionCondition condition, bool actual, bool expected)
        {
            // For Exists, NotExists, IsValid, IsNotValid, 'expected' is usually implicitly true.
            // e.g., HeaderExists means we expect `actual` (header_found_flag) to be true.
            return condition switch
            {
                AssertionCondition.Equals => actual == expected,
                AssertionCondition.NotEquals => actual != expected,
                // These often map directly to the boolean 'actual' value when 'expected' is true.
                AssertionCondition.Exists => actual,    // e.g. If actual is true (it exists), and we assert Exists, it passes.
                AssertionCondition.NotExists => !actual, // e.g. If actual is false (it doesn't exist), and we assert NotExists, it passes.
                AssertionCondition.IsValid => actual,
                AssertionCondition.IsNotValid => !actual,
                _ => false,
            };
        }

        private bool EvaluateGeneralCondition(AssertionCondition condition, object? actual, object? expected)
        {
            if (actual is IComparable actualComparable && expected is IComparable) // Try numeric/comparable first
            {
                // Ensure expected is compatible with actual for comparison
                object? convertedExpected = null;
                try
                {
                    convertedExpected = ConvertType(expected, actualComparable.GetType());
                }
                catch
                {
                    // If conversion fails, fallback to string comparison or handle as error
                }
                if (convertedExpected is IComparable compExpected)
                {
                    return EvaluateNumericCondition(condition, actualComparable, compExpected);
                }
            }
            if (actual is string || expected is string) // Fallback to string comparison
            {
                return EvaluateStringCondition(condition, actual?.ToString(), expected?.ToString());
            }
            if (actual is bool actualBool && expected is bool expectedBool)
            {
                return EvaluateBooleanCondition(condition, actualBool, expectedBool);
            }
            if (condition == AssertionCondition.IsNull) return actual == null;
            if (condition == AssertionCondition.IsNotNull) return actual != null;

            // Default object equality (reference or overridden .Equals)
            if (condition == AssertionCondition.Equals) return object.Equals(actual, expected);
            if (condition == AssertionCondition.NotEquals) return !object.Equals(actual, expected);

            _logger.LogWarning("Could not evaluate condition {Condition} for actual type {ActualType} and expected type {ExpectedType}",
                condition, actual?.GetType().Name ?? "null", expected?.GetType().Name ?? "null");
            return false;
        }


        private Dictionary<string, object?> ExtractVariablesFromResponse(
            List<VariableExtractionRule> extractionRules,
            HttpResponseMessage response,
            string responseBody,
            IDictionary<string, object> currentVariablesContext) // For potential chained extractions, though not used directly here yet
        {
            var extracted = new Dictionary<string, object?>();
            if (extractionRules == null) return extracted;

            foreach (var rule in extractionRules)
            {
                object? value = null;
                try
                {
                    switch (rule.Source)
                    {
                        case ExtractionSource.ResponseBody:
                            if (!string.IsNullOrWhiteSpace(responseBody) && (responseBody.TrimStart().StartsWith("{") || responseBody.TrimStart().StartsWith("[")))
                            {
                                try
                                {
                                    var jsonNode = JsonNode.Parse(responseBody);
                                    var selectedNode = SelectJsonNode(jsonNode, rule.Path);
                                    value = GetJsonNodeValue(selectedNode);
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse response body as JSON for variable extraction '{VariableName}'. Path: {Path}", rule.VariableName, rule.Path);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to extract variable '{VariableName}' from JSON response body. Path: {Path}", rule.VariableName, rule.Path);
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(responseBody)) // Treat as plain text if not JSON
                            {
                                // For plain text, Path might be less relevant unless used for regex group or similar
                                value = responseBody;
                            }
                            break;

                        case ExtractionSource.ResponseHeader:
                            if (response.Headers.TryGetValues(rule.Path, out var headerValues) ||
                                response.Content.Headers.TryGetValues(rule.Path, out headerValues))
                            {
                                value = string.Join(",", headerValues); // Could be multiple values for a header
                            }
                            break;
                        case ExtractionSource.ResponseStatusCode:
                            value = (int)response.StatusCode;
                            break;
                    }

                    if (value != null && !string.IsNullOrEmpty(rule.Regex))
                    {
                        var match = Regex.Match(value.ToString()!, rule.Regex);
                        if (match.Success)
                        {
                            value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value; // Prefer first capture group if exists
                        }
                        else
                        {
                            _logger.LogWarning("Regex '{RegexPattern}' did not match for variable '{VariableName}' on value '{Value}'.", rule.Regex, rule.VariableName, value);
                            value = null; // Or keep original value if regex is optional?
                        }
                    }
                    extracted[rule.VariableName] = value;
                    _logger.LogInformation("Extracted variable '{VariableName}' with value '{VariableValue}'", rule.VariableName, value ?? "null");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting variable '{VariableName}'. Rule: {@Rule}", rule.VariableName, rule);
                    extracted[rule.VariableName] = null; // Store null if extraction failed
                }
            }
            return extracted;
        }

        private object? UnwrapJsonElement(object? value)
        {
            if (value is JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (je.TryGetInt32(out int i)) return i;
                        if (je.TryGetInt64(out long l)) return l;
                        if (je.TryGetDouble(out double d)) return d;
                        break;
                    case JsonValueKind.String:
                        return je.GetString();
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return je.GetBoolean();
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        return null;
                    default:
                        return je.ToString();
                }
            }
            return value;
        }
    }
}