using System.Text.Json.Serialization;
using gentest.Models.Common;

namespace gentest.Models.Common
{
    public class TestCase
    {
        [JsonPropertyName("testCaseId")]
        public string TestCaseId { get; set; }
        
        [JsonPropertyName("testCaseName")]
        public string TestCaseName { get; set; }
        
        [JsonPropertyName("testCaseDescription")]
        public string TestCaseDescription { get; set; }
        
        [JsonPropertyName("testCaseType")]
        public string TestCaseType { get; set; }
        
        [JsonPropertyName("priority")]
        public string Priority { get; set; }
        
        [JsonPropertyName("prerequisites")]
        public List<string> Prerequisites { get; set; }
        
        [JsonPropertyName("request")]
        public TestRequest Request { get; set; }
        
        [JsonPropertyName("expectedResponse")]
        public TestResponse ExpectedResponse { get; set; }
        
        [JsonPropertyName("assertions")]
        public List<TestAssertion> Assertions { get; set; }
        
        [JsonPropertyName("mockRequirements")]
        public List<MockRequirement> MockRequirements { get; set; }
    }
}