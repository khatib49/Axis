using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Services
{
    public interface IImageStorageService
    {
        Task<string> SaveImageAsync(IFormFile file, CancellationToken ct = default);
        Task DeleteImageAsync(string? relativePath);
    }
}
