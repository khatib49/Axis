using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MenuController : ControllerBase
    {
        private readonly IMenuService _menu;

        public MenuController(IMenuService menu)
        {
            _menu = menu;
        }

        // GET: api/menu/categories?type=CoffeeShop
        [HttpGet("categories")]
        public async Task<ActionResult<IReadOnlyList<CategoryMenuDto>>> GetCategories([FromQuery] string? type, [FromQuery] List<string>? types,
        CancellationToken ct)
        {
            var result = (types is { Count: > 0 })
                ? await _menu.GetCategoriesMenuAsync(types, ct)
                : await _menu.GetCategoriesMenuAsync(type, ct);

            return Ok(result);
        }

        // GET: api/menu/items?categoryId=3&type=CoffeeShop
        [HttpGet("items")]
        public async Task<ActionResult<IReadOnlyList<ItemMenuDto>>> GetItems([FromQuery] int categoryId, [FromQuery] string? type, CancellationToken ct)
        {
            var result = await _menu.GetItemsByCategoryAsync(categoryId, type, ct);
            return Ok(result);
        }
    }
}
