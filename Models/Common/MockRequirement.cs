using System.Text.Json.Serialization;

namespace gentest.Models.Common
{
    public class MockRequirement
    {
        [JsonPropertyName("service")]
        public string Service { get; set; }
        
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; }
        
        [JsonPropertyName("response")]
        public object Response { get; set; }
    }
}