using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class TestAssertion
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public AssertionType Type { get; set; }

        [JsonPropertyName("target")] 
        public string? Target { get; set; }

        [JsonPropertyName("condition")]
        public AssertionCondition Condition { get; set; }

        [JsonPropertyName("expectedValue")]
        public object? ExpectedValue { get; set; }
    }

    
    public enum AssertionType
    {
        StatusCode,
        ResponseTime, // in milliseconds
        HeaderExists,
        HeaderValue,
        BodyContainsString,
        BodyEqualsString,
        BodyMatchesRegex,
        JsonPathValue,
        JsonPathExists,
        JsonPathNotExists,
        JsonSchemaValidation, // Placeholder for schema validation logic
        XmlPathValue,         // Placeholder for XML
        XmlSchemaValidation,  // Placeholder for XML
        ArrayLength,
        ArrayContains
    }

    public enum AssertionCondition
    {
        Equals,
        NotEquals,
        Contains,
        NotContains,
        GreaterThan,
        LessThan,
        GreaterThanOrEquals,
        LessThanOrEquals,
        MatchesRegex,
        NotMatchesRegex,
        Exists,
        NotExists,
        IsEmpty,
        IsNotEmpty,
        IsNull,
        IsNotNull,
        IsValid, // For schema validation
        IsNotValid // For schema validation
    }

}