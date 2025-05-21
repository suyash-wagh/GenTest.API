using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using GenTest.Models.ApiDefinition;
using GenTest.Models.Common;
using GenTest.Services.ApiParsing;

namespace gentest.Services.ApiParsing
{
    public class SwaggerParser : IApiDefinitionParser
    {
        private readonly ILogger<SwaggerParser> _logger;
        private readonly IHttpClientFactory _httpClientFactory; // For fetching Swagger from URL

        public SwaggerParser(ILogger<SwaggerParser> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public bool CanParse(ApiDefinitionSourceType sourceType)
        {
            return sourceType == ApiDefinitionSourceType.SwaggerFile;
        }

        public Task<List<ApiEndpointInfo>> ParseAsync(ApiDefinitionInput input)
        {
            return ParseAsync(input.SourcePathOrUrl, input.SelectedEndpoints);
        }

        public async Task<List<ApiEndpointInfo>> ParseAsync(string pathOrUrl, List<string>? selectedEndpointIds = null)
        {
            var apiEndpoints = new List<ApiEndpointInfo>();
            OpenApiDocument? openApiDocument = await ParseSwaggerFromFileAsync(pathOrUrl);

            if (openApiDocument == null)
            {
                _logger.LogError("Failed to parse Swagger definition from: {PathOrUrl}", pathOrUrl);
                return apiEndpoints;
            }

            foreach (var pathEntry in openApiDocument.Paths)
            {
                var path = pathEntry.Key;
                var pathItem = pathEntry.Value;

                foreach (var operationEntry in pathItem.Operations)
                {
                    var httpMethod = ConvertOperationTypeToHttpMethodExtended(operationEntry.Key);
                    var operation = operationEntry.Value;
                    string endpointId = $"{httpMethod} {path}";

                    if (selectedEndpointIds == null || selectedEndpointIds.Contains(endpointId, StringComparer.OrdinalIgnoreCase) || selectedEndpointIds.Contains(operation.OperationId, StringComparer.OrdinalIgnoreCase))
                    {
                        apiEndpoints.Add(MapToApiEndpointInfo(endpointId, path, httpMethod, operation));
                    }
                }
            }
            return apiEndpoints;
        }
        
        private ApiEndpointInfo MapToApiEndpointInfo(string id, string path, HttpMethodExtended method, OpenApiOperation operation)
        {
            return new ApiEndpointInfo
            {
                Id = id,
                Path = path,
                Method = method,
                Summary = operation.Summary,
                Description = operation.Description,
                OperationId = operation.OperationId,
                OpenApiOperation = operation, // Keep the raw operation for detailed prompt building
                Parameters = operation.Parameters?.Select(p => new ApiParameterInfo
                {
                    Name = p.Name,
                    In = p.In.ToString()?.ToLowerInvariant() ?? "unknown",
                    Description = p.Description,
                    Required = p.Required,
                    Schema = MapSchema(p.Schema)
                }).ToList(),
                RequestBody = operation.RequestBody == null ? null : new ApiRequestBodyInfo
                {
                    Description = operation.RequestBody.Description,
                    Required = operation.RequestBody.Required,
                    Content = operation.RequestBody.Content?.ToDictionary(
                        c => c.Key,
                        c => new ApiMediaTypeInfo { Schema = MapSchema(c.Value.Schema) }
                    )
                },
                Responses = operation.Responses?.ToDictionary(
                    r => r.Key,
                    r => new ApiResponseInfo
                    {
                        Description = r.Value.Description,
                        Content = r.Value.Content?.ToDictionary(
                            c => c.Key,
                            c => new ApiMediaTypeInfo { Schema = MapSchema(c.Value.Schema) }
                        ),
                        Headers = r.Value.Headers?.ToDictionary(
                            h => h.Key,
                            h => new ApiHeaderInfo { Description = h.Value.Description, Schema = MapSchema(h.Value.Schema)}
                        )
                    }
                ),
                SecurityRequirements = operation.Security?.SelectMany(secReq => 
                    secReq.Select(scheme => new ApiSecurityRequirementInfo {
                        SchemeName = scheme.Key.Reference.Id, // Assuming reference ID is the name
                        Scopes = scheme.Value?.ToList()
                    })).ToList()
            };
        }

        private ApiSchemaInfo? MapSchema(OpenApiSchema? openApiSchema)
        {
            if (openApiSchema == null) return null;
            return new ApiSchemaInfo
            {
                Type = openApiSchema.Type,
                Format = openApiSchema.Format,
                Description = openApiSchema.Description,
                Items = MapSchema(openApiSchema.Items),
                Properties = openApiSchema.Properties?.ToDictionary(p => p.Key, p => MapSchema(p.Value)),
                Required = openApiSchema.Required?.ToList(),
                Enum = openApiSchema.Enum?.Select(e => (object)e).ToList(), // OpenApiAny can be complex
                Default = openApiSchema.Default, // Handle OpenApiAny
                Example = openApiSchema.Example // Handle OpenApiAny
                // Map other properties as needed
            };
        }


        private async Task<OpenApiDocument?> ParseSwaggerFromFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                _logger.LogError("Swagger file not found: {FilePath}", filePath);
                return null;
            }
            try
            {
                using var streamReader = new System.IO.StreamReader(filePath);
                var openApiDocument = new OpenApiStreamReader().Read(streamReader.BaseStream, out var diagnostic);
                LogDiagnostics(diagnostic, filePath);
                return diagnostic.Errors.Any() ? null : openApiDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Swagger file: {FilePath}", filePath);
                return null;
            }
        }

        private async Task<OpenApiDocument?> ParseSwaggerFromUrlAsync(string url)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("SwaggerDownloader");
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync();
                var openApiDocument = new OpenApiStreamReader().Read(stream, out var diagnostic);
                LogDiagnostics(diagnostic, url);
                return diagnostic.Errors.Any() ? null : openApiDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching or parsing Swagger from URL: {Url}", url);
                return null;
            }
        }

        private void LogDiagnostics(OpenApiDiagnostic diagnostic, string source)
        {
            if (diagnostic.Errors.Any())
            {
                _logger.LogError("Swagger parsing errors from {Source}: {Errors}", source,
                    string.Join("; ", diagnostic.Errors.Select(e => $"{e.Pointer}: {e.Message}")));
            }
            if (diagnostic.Warnings.Any())
            {
                _logger.LogWarning("Swagger parsing warnings from {Source}: {Warnings}", source,
                    string.Join("; ", diagnostic.Warnings.Select(w => $"{w.Pointer}: {w.Message}")));
            }
        }

        private HttpMethodExtended ConvertOperationTypeToHttpMethodExtended(OperationType operationType)
        {
            return operationType switch
            {
                OperationType.Get => HttpMethodExtended.GET,
                OperationType.Put => HttpMethodExtended.PUT,
                OperationType.Post => HttpMethodExtended.POST,
                OperationType.Delete => HttpMethodExtended.DELETE,
                OperationType.Options => HttpMethodExtended.OPTIONS,
                OperationType.Head => HttpMethodExtended.HEAD,
                OperationType.Patch => HttpMethodExtended.PATCH,
                // OperationType.Trace not in HttpMethodExtended, map to something or add it
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), $"Unsupported operation type: {operationType}")
            };
        }
    }
}