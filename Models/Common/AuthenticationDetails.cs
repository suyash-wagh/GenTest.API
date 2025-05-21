using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class AuthenticationDetails
    {
        [JsonPropertyName("type")]
        public AuthenticationType Type { get; set; } = AuthenticationType.None;

        [JsonPropertyName("username")]
        public string? Username { get; set; } // For Basic Auth

        [JsonPropertyName("password")]
        public string? Password { get; set; } // For Basic Auth

        [JsonPropertyName("token")]
        public string? Token { get; set; } // For Bearer Token

        [JsonPropertyName("apiKeyHeaderName")]
        public string? ApiKeyHeaderName { get; set; } // For API Key

        [JsonPropertyName("apiKeyValue")]
        public string? ApiKeyValue { get; set; } // For API Key

        [JsonPropertyName("apiKeyLocation")]
        public ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header; // For API Key
    }

    public enum ApiKeyLocation
    {
        Header,
        QueryParameter
    }

    public enum AuthenticationType
    {
        None,
        Basic,
        BearerToken,
        ApiKey
    }
}