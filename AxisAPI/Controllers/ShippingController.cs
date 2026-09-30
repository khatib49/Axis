using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Admin side of delivery: Aramex settings/test, fee zones, the shipments
    /// ledger and COD settlement. Page key "shipments" (Accounting group).
    /// </summary>
    [ApiController]
    [Route("api/shipping")]
    [Authorize(Roles = "admin")]
    public class ShippingController : ControllerBase
    {
        private readonly IShippingService _shipping;
        public ShippingController(IShippingService shipping) { _shipping = shipping; }
        private string Actor => User?.Identity?.Name ?? "admin";

        // ── Settings ─────────────────────────────────────────────────────
        [HttpGet("settings")]
        public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await _shipping.GetSettingsAsync(ct));

        [HttpPost("test")]
        public async Task<IActionResult> Test(CancellationToken ct) => Ok(await _shipping.TestConnectionAsync(ct));

        // ── Zones ────────────────────────────────────────────────────────
        [HttpGet("zones")]
        public async Task<IActionResult> Zones([FromQuery] bool includeInactive = true, CancellationToken ct = default)
            => Ok(await _shipping.ListZonesAsync(includeInactive, ct));

        [HttpPost("zones")]
        public async Task<IActionResult> CreateZone([FromBody] ShippingZoneUpsertDto body, CancellationToken ct)
        {
            try { return Ok(await _shipping.UpsertZoneAsync(null, body, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPut("zones/{id:int}")]
        public async Task<IActionResult> UpdateZone(int id, [FromBody] ShippingZoneUpsertDto body, CancellationToken ct)
        {
            try { return Ok(await _shipping.UpsertZoneAsync(id, body, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpDelete("zones/{id:int}")]
        public async Task<IActionResult> DeleteZone(int id, CancellationToken ct)
            => await _shipping.DeleteZoneAsync(id, ct) ? NoContent() : NotFound();

        // ── Shipments ────────────────────────────────────────────────────
        /// <param name="status">open | unsettled | Created | InTransit | Delivered | Returned | … (empty = all)</param>
        [HttpGet("shipments")]
        public async Task<IActionResult> Shipments([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search, CancellationToken ct)
            => Ok(await _shipping.ListShipmentsAsync(status, from, to, search, ct));

        [HttpGet("shipments/{id:int}")]
        public async Task<IActionResult> Shipment(int id, CancellationToken ct)
        {
            var s = await _shipping.GetShipmentAsync(id, ct);
            return s is null ? NotFound() : Ok(s);
        }

        [HttpPost("shipments/{id:int}/refresh")]
        public async Task<IActionResult> Refresh(int id, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.RefreshTrackingAsync(id, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        [HttpPost("shipments/{id:int}/delivered")]
        public async Task<IActionResult> Delivered(int id, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.MarkDeliveredAsync(id, Actor, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        public record StatusBody(string Status, string? Reason);

        [HttpPost("shipments/{id:int}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] StatusBody body, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.MarkFailedAsync(id, body.Status, body.Reason, Actor, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        /// <summary>Poll Aramex for every open shipment now (same as the Hangfire job).</summary>
        [HttpPost("shipments/poll")]
        public async Task<IActionResult> Poll(CancellationToken ct) => Ok(new { changed = await _shipping.PollOpenShipmentsAsync(ct) });

        [HttpPost("shipments/pickup")]
        public async Task<IActionResult> Pickup([FromBody] CreatePickupRequest? body, CancellationToken ct)
        {
            var r = await _shipping.CreatePickupAsync(body ?? new CreatePickupRequest(null, null, null, null, null, null), Actor, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── COD settlement ───────────────────────────────────────────────
        [HttpPost("cod/settle")]
        public async Task<IActionResult> Settle([FromBody] CodSettlementRequest body, CancellationToken ct)
        {
            var r = await _shipping.SettleCodAsync(body, Actor, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
