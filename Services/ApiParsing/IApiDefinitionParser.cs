using GenTest.Models.ApiDefinition;

namespace GenTest.Services.ApiParsing
{
    public enum ApiDefinitionSourceType
    {
        SwaggerFile,
        PostmanCollectionFile
    }

    public class ApiDefinitionInput
    {
        public required ApiDefinitionSourceType SourceType { get; set; }
        public required string SourcePathOrUrl { get; set; }
        public List<string>? SelectedEndpoints { get; set; }
    }

    /// <summary>
    /// Interface for API definition parsers (Swagger only).
    /// </summary>
    public interface IApiDefinitionParser
    {
        /// <summary>
        /// Determines if this parser can handle the given API definition source type (Swagger).
        /// </summary>
        /// <param name="sourceType">The type of API definition (Swagger only).</param>
        /// <returns>True if this parser supports the source type.</returns>
        bool CanParse(ApiDefinitionSourceType sourceType);

        /// <summary>
        /// Parses the API definition and returns endpoint information.
        /// </summary>
        /// <param name="input">The input describing the API definition source and options.</param>
        /// <returns>List of parsed API endpoints.</returns>
        Task<List<ApiEndpointInfo>> ParseAsync(ApiDefinitionInput input);
    }
}