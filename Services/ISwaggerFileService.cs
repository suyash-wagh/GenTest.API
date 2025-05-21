using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace gentest.Services
{
    public interface ISwaggerFileService
    {
        Task<string> SaveSwaggerFileAsync(IFormFile file);
        Task<List<string>> ParseSwaggerFileAsync(string filePath);
    }
}