using Microsoft.OpenApi.Readers;

namespace gentest.Services
{
    public class SwaggerFileService : ISwaggerFileService
    {
        private readonly string _uploadDirectory = "Uploads"; 

        public SwaggerFileService()
        {
            Directory.CreateDirectory(_uploadDirectory);
        }

        public async Task<string> SaveSwaggerFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            var fileName = Path.Combine(_uploadDirectory, Path.GetRandomFileName() + ".json");

            using (var stream = new FileStream(fileName, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return fileName; 
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
                        return new List<string>();
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
                return new List<string>();
            }
        }
    }
}