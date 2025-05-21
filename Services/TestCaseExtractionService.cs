using System.Text.Json;
using System.Text.Json.Serialization; // For JsonStringEnumConverter
using System.Text.RegularExpressions;
using GenTest.Models.Common; // Assuming TestCase is here
using Microsoft.Extensions.Logging;

namespace GenTest.Services
{
    public class TestCaseExtractionService : ITestCaseExtractionService
    {
        private readonly ILogger<TestCaseExtractionService> _logger;
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } 
        };

        // Regex for matching a JSON object (handles nested braces)
        private static readonly Regex JsonObjectRegex = new Regex(
            @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        // Regex for matching a JSON array of objects
        private static readonly Regex JsonArrayOfObjectsRegex = new Regex(
            @"\[\s*(" + JsonObjectRegex.ToString() + @"(?:\s*,\s*" + JsonObjectRegex.ToString() + @")*\s*)?\]",
            RegexOptions.Singleline | RegexOptions.Compiled);


        public TestCaseExtractionService(ILogger<TestCaseExtractionService> logger)
        {
            _logger = logger;
        }

        public List<TestCase> ExtractTestCasesFromResponse(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                _logger.LogWarning("LLM response is null or empty.");
                return new List<TestCase>();
            }

            var testCases = new List<TestCase>();
            string cleanedResponse = CleanLLMResponse(llmResponse);

            if (string.IsNullOrWhiteSpace(cleanedResponse) || cleanedResponse.Length < 10) // Basic sanity check
            {
                _logger.LogWarning("Cleaned LLM response is too short or empty. Original: {Original}", llmResponse.Substring(0, Math.Min(500, llmResponse.Length)));
                return testCases;
            }

            try
            {
                // Attempt 1: Try to parse as a direct List<TestCase>
                try
                {
                    var directParse = JsonSerializer.Deserialize<List<TestCase>>(cleanedResponse, _jsonSerializerOptions);
                    if (directParse != null)
                    {
                        var validDirectParse = directParse.Where(IsValidTestCase).ToList();
                        if (validDirectParse.Any())
                        {
                            _logger.LogInformation("Successfully parsed response directly as JSON array with {Count} valid test cases.", validDirectParse.Count);
                            return validDirectParse;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Direct JSON array parsing failed. Cleaned response (first 500 chars): {CleanedResponsePreview}", cleanedResponse.Substring(0, Math.Min(500, cleanedResponse.Length)));
                    // Continue to other attempts
                }
                
                Match arrayMatch = JsonArrayOfObjectsRegex.Match(cleanedResponse);
                if (arrayMatch.Success)
                {
                    string potentialJsonArray = arrayMatch.Value;
                    try
                    {
                        var extractedTestCases = JsonSerializer.Deserialize<List<TestCase>>(potentialJsonArray, _jsonSerializerOptions);
                        if (extractedTestCases != null)
                        {
                            var validExtractedTestCases = extractedTestCases.Where(IsValidTestCase).ToList();
                            if (validExtractedTestCases.Any())
                            {
                                _logger.LogInformation("Successfully parsed JSON array extracted by regex with {Count} valid test cases.", validExtractedTestCases.Count);
                                return validExtractedTestCases;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogDebug(ex, "Parsing JSON array extracted by regex failed. Array (first 500 chars): {JsonArrayPreview}", potentialJsonArray.Substring(0, Math.Min(500, potentialJsonArray.Length)));
                        // Continue to individual object parsing
                    }
                }
                
                _logger.LogInformation("No valid JSON array found or parsing failed, attempting to extract individual test case objects.");
                var objectMatches = JsonObjectRegex.Matches(cleanedResponse);
                var individualTestCases = new List<TestCase>();

                foreach (Match match in objectMatches)
                {
                    string potentialJsonObj = match.Value;
                    if (potentialJsonObj.Length < 50) continue;

                    try
                    {
                        var testCase = JsonSerializer.Deserialize<TestCase>(potentialJsonObj, _jsonSerializerOptions);
                        if (IsValidTestCase(testCase))
                        {
                            individualTestCases.Add(testCase!);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse individual JSON object. Object (first 500 chars): {JsonObjectPreview}", potentialJsonObj.Substring(0, Math.Min(500, potentialJsonObj.Length)));
                    }
                }

                if (individualTestCases.Any())
                {
                    _logger.LogInformation("Successfully extracted {Count} valid individual test cases after array parsing failed.", individualTestCases.Count);
                    return individualTestCases;
                }
                else
                {
                    _logger.LogWarning("Failed to extract any valid test cases from LLM response after all attempts. Cleaned response (first 500 chars): {CleanedResponsePreview}", cleanedResponse.Substring(0, Math.Min(500, cleanedResponse.Length)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during test case extraction process. Cleaned response (first 500 chars): {CleanedResponsePreview}", cleanedResponse.Substring(0, Math.Min(500, cleanedResponse.Length)));
            }

            return testCases;
        }

        private bool IsValidTestCase(TestCase? tc)
        {
            if (tc == null) return false;

            // Core requirements
            if (string.IsNullOrWhiteSpace(tc.TestCaseId) ||
                string.IsNullOrWhiteSpace(tc.TestCaseName) || // Name is also important
                tc.Request == null ||
                string.IsNullOrWhiteSpace(tc.Request.Path)) // Request and Path are fundamental
            {
                _logger.LogDebug("TestCase marked invalid due to missing Id, Name, Request, or Request.Path. ID: {Id}, Name: {Name}", tc.TestCaseId, tc.TestCaseName);
                return false;
            }

            if (tc.Assertions != null)
            {
                foreach (var assertion in tc.Assertions)
                {
                    if (assertion == null)
                    {
                        _logger.LogDebug("TestCase {Id} has a null or potentially invalid assertion.", tc.TestCaseId);
                    }
                }
            }

            return true;
        }

        private string CleanLLMResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            string cleaned = response;

            cleaned = Regex.Replace(cleaned, @"^\s*```(?:[a-zA-Z0-9]*)?\s*([\s\S]*?)\s*```\s*$", "$1", RegexOptions.Multiline);
            cleaned = cleaned.Trim();
            
            var firstBrace = cleaned.IndexOf('{');
            var firstBracket = cleaned.IndexOf('[');

            if (firstBrace == -1 && firstBracket == -1)
            {
                _logger.LogDebug("No JSON start characters ('{{' or '[') found in cleaned response: {CleanedPreview}", cleaned.Substring(0, Math.Min(100, cleaned.Length)));
                return string.Empty;
            }

            int startIndex = -1;
            char startChar = ' ';
            char endChar = ' ';

            if (firstBracket != -1 && (firstBrace == -1 || firstBracket < firstBrace))
            {
                // Starts with an array
                startIndex = firstBracket;
                startChar = '[';
                endChar = ']';
            }
            else if (firstBrace != -1 && (firstBracket == -1 || firstBrace < firstBracket))
            {
                // Starts with an object
                startIndex = firstBrace;
                startChar = '{';
                endChar = '}';
            }

            if (startIndex != -1)
            {
                int balance = 0;
                int endIndex = -1;
                for (int i = startIndex; i < cleaned.Length; i++)
                {
                    if (cleaned[i] == startChar) balance++;
                    else if (cleaned[i] == endChar) balance--;

                    if (balance == 0)
                    {
                        endIndex = i;
                        break;
                    }
                }

                if (endIndex != -1)
                {
                    cleaned = cleaned.Substring(startIndex, endIndex - startIndex + 1);
                }
                else
                {
                     _logger.LogDebug("Could not find balanced end for JSON structure starting with '{StartChar}'. Original (first 100): {CleanedPreview}", startChar, cleaned.Substring(0, Math.Min(100, cleaned.Length)));
                }
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogDebug("Response became empty after cleaning. Original (first 100): {OriginalPreview}", response.Substring(0, Math.Min(100, response.Length)));
            }

            return cleaned;
        }
    }
}