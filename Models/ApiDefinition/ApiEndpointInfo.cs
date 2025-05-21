using GenTest.Models.Common;
using Microsoft.OpenApi.Models;

namespace GenTest.Models.ApiDefinition
{
    public class ApiEndpointInfo
    {
        public required string Id { get; set; }
        public required string Path { get; set; }
        public required HttpMethodExtended Method { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? OperationId { get; set; }

        public OpenApiOperation? OpenApiOperation { get; set; }

        public List<ApiParameterInfo>? Parameters { get; set; }
        public ApiRequestBodyInfo? RequestBody { get; set; }
        public Dictionary<string, ApiResponseInfo>? Responses { get; set; } // Key: status code
        public List<ApiSecurityRequirementInfo>? SecurityRequirements { get; set; }

        public virtual string ToPromptString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"- Endpoint ID: {Id}");
            sb.AppendLine($"- Path: {Path}");
            sb.AppendLine($"- Method: {Method}");
            if (!string.IsNullOrEmpty(Summary)) sb.AppendLine($"- Summary: {Summary}");
            if (!string.IsNullOrEmpty(Description)) sb.AppendLine($"- Description: {Description}");
            if (OpenApiOperation != null) // If Swagger based
            {
                sb.AppendLine($"- Operation ID: {OpenApiOperation.OperationId ?? "N/A"}");
                // ... Add parameters, request body, responses from OpenApiOperation details ...
                // This part will be similar to your existing BuildTestCaseGeneratorPrompt
            }
            return sb.ToString();
        }
    }

    public class ApiParameterInfo
    {
        public required string Name { get; set; }
        public required string In { get; set; }
        public string? Description { get; set; }
        public bool Required { get; set; }
        public ApiSchemaInfo? Schema { get; set; }
    }

    public class ApiRequestBodyInfo
    {
        public string? Description { get; set; }
        public bool Required { get; set; }
        public Dictionary<string, ApiMediaTypeInfo>? Content { get; set; }
    }

    public class ApiResponseInfo
    {
        public required string Description { get; set; }
        public Dictionary<string, ApiMediaTypeInfo>? Content { get; set; }
        public Dictionary<string, ApiHeaderInfo>? Headers {get; set;}
    }
     public class ApiHeaderInfo
    {
        public string? Description { get; set; }
        public ApiSchemaInfo? Schema { get; set; }
    }

    public class ApiMediaTypeInfo
    {
        public ApiSchemaInfo? Schema { get; set; }
    }

    public class ApiSchemaInfo
    {
        public string? Type { get; set; }
        public string? Format { get; set; }
        public string? Description { get; set; }
        public ApiSchemaInfo? Items { get; set; }
        public Dictionary<string, ApiSchemaInfo>? Properties { get; set; }
        public List<string>? Required { get; set; }
        public List<object>? Enum { get; set; }
        public object? Default { get; set; }
        public object? Example { get; set; }

        public override string ToString() 
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Type: {Type ?? "any"}");
            if (!string.IsNullOrEmpty(Format)) sb.Append($", Format: {Format}");
            if (Enum?.Any() == true) sb.Append($", Enum: [{string.Join(", ", Enum)}]");
            if (Properties?.Any() == true)
            {
                sb.Append(", Properties: { ");
                sb.Append(string.Join(", ", Properties.Select(p => $"{p.Key}: ({p.Value.ToString()})")));
                sb.Append(" }");
                if (Required?.Any() == true) sb.Append($", Required: [{string.Join(", ", Required)}]");
            }
            if (Items != null) sb.Append($", Items: ({Items.ToString()})");
            return sb.ToString();
        }
    }
    public class ApiSecurityRequirementInfo
    {
        public required string SchemeName { get; set; }
        public List<string>? Scopes { get; set; }
    }
}