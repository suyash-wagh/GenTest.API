using System.Text.Json;
using gentest.Models.Common;

namespace gentest.Services
{
    public class TestCaseExtractionService : ITestCaseExtractionService
    {
        private readonly ILogger<TestCaseExtractionService> _logger;

        public TestCaseExtractionService(ILogger<TestCaseExtractionService> logger)
        {
            _logger = logger;
        }

        public List<TestCase> ExtractTestCasesFromResponse(string response)
        {
            var testCases = new List<TestCase>();

            try
            {
                var cleanedResponse = CleanLLMResponse(response);
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };

                    var directParse = JsonSerializer.Deserialize<List<TestCase>>(cleanedResponse, options);
                    if (directParse != null && directParse.Count > 0)
                    {
                        _logger.LogInformation("Successfully parsed response directly as JSON array with {Count} test cases", directParse.Count);
                        return directParse;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Direct JSON parsing failed, proceeding with regex extraction");
                }

                // Second try: Extract JSON arrays from the response using regex
                var jsonArrayMatches = System.Text.RegularExpressions.Regex.Matches(
                    response,
                    @"\[\s*\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}\s*\]",
                    System.Text.RegularExpressions.RegexOptions.Singleline
                );

                if (jsonArrayMatches.Count > 0)
                {
                    // Try each JSON array found (in case there are multiple)
                    foreach (System.Text.RegularExpressions.Match arrayMatch in jsonArrayMatches)
                    {
                        try
                        {
                            var jsonArray = arrayMatch.Value;
                            var extractedTestCases = JsonSerializer.Deserialize<List<TestCase>>(jsonArray, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                AllowTrailingCommas = true
                            });

                            if (extractedTestCases != null && extractedTestCases.Count > 0)
                            {
                                // Check if these are valid test cases (basic validation)
                                var validTestCases = extractedTestCases.Where(tc =>
                                    !string.IsNullOrEmpty(tc.TestCaseId) &&
                                    !string.IsNullOrEmpty(tc.TestCaseName)).ToList();

                                if (validTestCases.Count > 0)
                                {
                                    testCases.AddRange(validTestCases);
                                    _logger.LogInformation("Extracted {Count} valid test cases from JSON array", validTestCases.Count);

                                    // If we found valid test cases, we can return early
                                    if (testCases.Count > 0)
                                    {
                                        return testCases;
                                    }
                                }
                            }
                        }
                        catch (JsonException arrayEx)
                        {
                            _logger.LogDebug(arrayEx, "Failed to parse JSON array match");
                        }
                    }
                }

                // Third try: Extract individual JSON objects if array extraction failed
                _logger.LogInformation("No valid JSON arrays found or parsing failed, trying to extract individual test cases");
                var jsonObjectMatches = System.Text.RegularExpressions.Regex.Matches(
                    response,
                    @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}",
                    System.Text.RegularExpressions.RegexOptions.Singleline
                );

                foreach (System.Text.RegularExpressions.Match match in jsonObjectMatches)
                {
                    try
                    {
                        var jsonObj = match.Value;
                        // Skip very short matches that are unlikely to be complete test cases
                        if (jsonObj.Length < 50) continue;

                        var testCase = JsonSerializer.Deserialize<TestCase>(jsonObj, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true
                        });

                        if (testCase != null && !string.IsNullOrEmpty(testCase.TestCaseId) && !string.IsNullOrEmpty(testCase.TestCaseName))
                        {
                            testCases.Add(testCase);
                            _logger.LogDebug("Added individual test case: {Id}", testCase.TestCaseId);
                        }
                    }
                    catch (JsonException objEx)
                    {
                        _logger.LogDebug(objEx, "Failed to parse individual JSON object");
                    }
                }

                if (testCases.Count > 0)
                {
                    _logger.LogInformation("Successfully extracted {Count} individual test cases", testCases.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to extract any valid test cases from LLM response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error extracting test cases from LLM response");
            }

            return testCases;
        }

        /// <summary>
        /// Cleans LLM response by removing markdown code blocks and other non-JSON content
        /// </summary>
        private string CleanLLMResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "[]";

            // Remove markdown code block syntax
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                response,
                @"```(?:json|csharp|cs|javascript|js|)?([\s\S]*?)```",
                "$1"
            ).Trim();

            // If we have removed everything or nearly everything, return the original
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length < response.Length / 10)
            {
                cleaned = response;
            }

            // Ensure the response starts with [ and ends with ] for array parsing
            if (!cleaned.StartsWith("["))
            {
                var firstBracket = cleaned.IndexOf('[');
                if (firstBracket >= 0)
                {
                    cleaned = cleaned.Substring(firstBracket);
                }
            }

            // Ensure proper closing of the JSON array
            if (!cleaned.EndsWith("]"))
            {
                var lastBracket = cleaned.LastIndexOf(']');
                if (lastBracket >= 0)
                {
                    cleaned = cleaned.Substring(0, lastBracket + 1);
                }
            }

            return cleaned;
        }
    }
}