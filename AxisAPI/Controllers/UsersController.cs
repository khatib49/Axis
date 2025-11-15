using Application.DTOs;
using Application.DTOs.RequestDto;
using Application.DTOs.ResponseDto;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUsersService _usersService;

        public UsersController(IUsersService usersService)
        {
            _usersService = usersService;
        }


        /// <summary>
        /// Create or get a client user by phone number.
        /// If the phone already exists, returns the existing user.
        /// </summary>
        [HttpPost("client")]
        public async Task<ActionResult<ClientUserResponse>> CreateClient([FromBody] ClientUserCreateRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                return BadRequest("PhoneNumber is required.");

            var result = await _usersService.CreateClient(request, ct);

            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var user = await _usersService.GetAsync(id, ct);
            if (user is null) return NotFound();
            return Ok(user);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var users = await _usersService.ListAsync(pagination, ct);
            return Ok(users);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, UserUpdateDto dto, CancellationToken ct)
        {
            var success = await _usersService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _usersService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
