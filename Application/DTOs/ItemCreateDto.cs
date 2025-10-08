using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ItemCreateDto
    {
        public string Name { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Type { get; set; } = default!;
        public int CategoryId { get; set; }
        public int? StatusId { get; set; }

        public IFormFile? Image { get; set; } // for upload
    }
}
