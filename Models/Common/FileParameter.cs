using System.Text.Json.Serialization;

namespace GenTest.Models.Common
{
    public class FileParameter
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; } // Form field name for the file

        [JsonPropertyName("fileName")]
        public required string FileName { get; set; } // Original file name

        [JsonPropertyName("filePath")] // Path to the file on the system running the tests
        public string? FilePath { get; set; } 

        [JsonPropertyName("fileContent")] // Base64 encoded file content, alternative to FilePath
        public string? FileContentBase64 {get; set;}

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; } // e.g., image/jpeg
    }
}