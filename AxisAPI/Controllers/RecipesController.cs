using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin,chef,admin_fnb")]
    public class RecipesController : ControllerBase
    {
        private readonly IRecipeService _svc;
        private readonly IHttpContextAccessor _http;

        public RecipesController(IRecipeService svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        private string? Actor => _http.HttpContext?.User?.Identity?.Name;

        // GET /api/recipes/items/42 — recipe lines for one menu item
        [HttpGet("items/{itemId:int}")]
        public async Task<ActionResult<IReadOnlyList<RecipeLineDto>>> ForItem(int itemId, CancellationToken ct)
            => Ok(await _svc.GetForItemAsync(itemId, ct));

        // PUT /api/recipes — full-replacement upsert for one item
        [HttpPut]
        public async Task<ActionResult<IReadOnlyList<RecipeLineDto>>> Upsert([FromBody] RecipeUpsertRequestDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpsertAsync(dto, Actor, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // GET /api/recipes/items-without-recipe — chef report
        [HttpGet("items-without-recipe")]
        public async Task<ActionResult<IReadOnlyList<int>>> ItemsWithoutRecipe(CancellationToken ct)
            => Ok(await _svc.GetItemIdsWithoutRecipeAsync(ct));
    }
}
