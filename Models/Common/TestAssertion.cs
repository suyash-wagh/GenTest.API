using System.Text.Json.Serialization;

namespace gentest.Models.Common
{
    public class TestAssertion
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        
        [JsonPropertyName("target")]
        public string Target { get; set; }
        
        [JsonPropertyName("condition")]
        public string Condition { get; set; }
        
        [JsonPropertyName("expectedValue")]
        public object ExpectedValue { get; set; }
    }
}