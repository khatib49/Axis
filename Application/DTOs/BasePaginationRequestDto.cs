using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public record BasePaginationRequestDto(int Page = 1, int PageSize = 10 , Guid? CategoryId = null , string? search = null , string? createdBy = null);

}
