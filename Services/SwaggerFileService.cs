using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;
using System.Linq;

namespace gentest.Services
{
    public class SwaggerFileService : ISwaggerFileService
    {
        private readonly string _uploadDirectory = "Uploads"; // Define an upload directory

        public SwaggerFileService()
        {
            // Ensure the upload directory exists
            Directory.CreateDirectory(_uploadDirectory);
        }

        public async Task<string> SaveSwaggerFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return null; // Or throw an exception
            }

            // Generate a unique file name to avoid conflicts
            var fileName = Path.Combine(_uploadDirectory, Path.GetRandomFileName() + ".json");

            using (var stream = new FileStream(fileName, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return fileName; // Return the path to the saved file
        }

        public async Task<List<string>> ParseSwaggerFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return new List<string>(); // Or throw an exception
            }

            try
            {
                using (var streamReader = new StreamReader(filePath))
                {
                    var openApiDocument = new OpenApiStreamReader().Read(streamReader.BaseStream, out var diagnostic);

                    if (diagnostic.Errors.Any())
                    {
                        // Log errors if any
                        // _logger.LogError("Swagger file parsing errors: {Errors}", string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
                        return new List<string>(); // Or throw an exception
                    }

                    var endpoints = new List<string>();
                    foreach (var pathItem in openApiDocument.Paths)
                    {
                        foreach (var operation in pathItem.Value.Operations)
                        {
                            endpoints.Add($"{operation.Key.ToString().ToUpper()} {pathItem.Key}");
                        }
                    }
                    return endpoints;
                }
            }
            catch (Exception ex)
            {
                // Log exception
                // _logger.LogError(ex, "Error parsing Swagger file");
                return new List<string>(); // Or throw an exception
            }
        }
    }
}