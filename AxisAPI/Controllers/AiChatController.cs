using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/ai/chat")]
    [Authorize(Roles = "admin")]
    public class AiChatController : ControllerBase
    {
        private readonly IAiChatService _svc;
        public AiChatController(IAiChatService svc) => _svc = svc;

        // GET /api/ai/chat/conversations
        [HttpGet("conversations")]
        public async Task<IActionResult> ListConversations([FromQuery] int take = 50, CancellationToken ct = default)
            => Ok(await _svc.ListConversationsAsync(take, ct));

        // GET /api/ai/chat/conversations/{id}/messages
        [HttpGet("conversations/{id:int}/messages")]
        public async Task<IActionResult> Messages(int id, CancellationToken ct)
            => Ok(await _svc.GetMessagesAsync(id, ct));

        // DELETE /api/ai/chat/conversations/{id}
        [HttpDelete("conversations/{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            await _svc.DeleteConversationAsync(id, ct);
            return NoContent();
        }

        // POST /api/ai/chat/send  { conversationId?, message }
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] AiSendMessageRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req?.Message))
                return BadRequest("Message is required.");
            var actor = User?.Identity?.Name ?? "admin";
            try
            {
                var resp = await _svc.SendAsync(req, actor, ct);
                return Ok(resp);
            }
            catch (Exception ex)
            {
                // Return a JSON body the FE can show — beats a bare 500.
                return StatusCode(500, new
                {
                    error = ex.Message,
                    type  = ex.GetType().Name,
                    inner = ex.InnerException?.Message
                });
            }
        }
    }
}
