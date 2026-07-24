using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class PrinterService : IPrinterService
    {
        private static readonly string[] AllowedStations = { "Kitchen", "Bar" };
        private static readonly string[] AllowedConnectionTypes = { "Network", "Usb" };

        private readonly IBaseRepository<Printer> _repo;
        private readonly IUnitOfWork _uow;
        private readonly IPrintDispatchService _dispatch;

        public PrinterService(IBaseRepository<Printer> repo, IUnitOfWork uow, IPrintDispatchService dispatch)
        {
            _repo = repo;
            _uow = uow;
            _dispatch = dispatch;
        }

        public async Task<PrinterDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: true, ct);
            return e is null ? null : ToDto(e);
        }

        public async Task<List<PrinterDto>> ListAsync(PrinterListFilterDto filter, CancellationToken ct = default)
        {
            var q = _repo.Query();

            if (!string.IsNullOrWhiteSpace(filter.Station))
            {
                var station = NormalizeStation(filter.Station);
                q = q.Where(p => p.Station == station);
            }

            if (!filter.IncludeDisabled)
                q = q.Where(p => p.IsEnabled);

            var data = await q.OrderBy(p => p.Station).ThenBy(p => p.Name)
                .Select(p => ToDto(p))
                .ToListAsync(ct);

            return data;
        }

        public async Task<PrinterDto> CreateAsync(PrinterCreateDto dto, CancellationToken ct = default)
        {
            var name = (dto.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Printer name is required.");

            var station = NormalizeStation(dto.Station);
            var connectionType = NormalizeConnectionType(dto.ConnectionType);
            var address = (dto.Address ?? "").Trim();
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Printer address is required.");

            ValidateAddress(connectionType, address);

            var exists = await _repo.Query()
                .AnyAsync(p => p.Name.ToLower() == name.ToLower(), ct);
            if (exists)
                throw new InvalidOperationException($"A printer named '{name}' already exists.");

            var e = new Printer
            {
                Name = name,
                Station = station,
                ConnectionType = connectionType,
                Address = address,
                CopyCount = dto.CopyCount < 1 ? 1 : dto.CopyCount,
                IsEnabled = true,
                CreatedOn = DateTime.UtcNow
            };

            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return ToDto(e);
        }

        public async Task<bool> UpdateAsync(int id, PrinterUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            if (dto.Name is not null)
            {
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Printer name cannot be empty.");

                var conflict = await _repo.Query()
                    .AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), ct);
                if (conflict)
                    throw new InvalidOperationException($"A printer named '{name}' already exists.");

                e.Name = name;
            }

            if (dto.Station is not null)
                e.Station = NormalizeStation(dto.Station);

            if (dto.ConnectionType is not null)
                e.ConnectionType = NormalizeConnectionType(dto.ConnectionType);

            if (dto.Address is not null)
            {
                var address = dto.Address.Trim();
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("Printer address cannot be empty.");
                e.Address = address;
            }

            if (dto.CopyCount is not null)
                e.CopyCount = dto.CopyCount.Value < 1 ? 1 : dto.CopyCount.Value;

            if (dto.IsEnabled is not null)
                e.IsEnabled = dto.IsEnabled.Value;

            // Re-validate the (possibly new) address against the (possibly new) connection type.
            ValidateAddress(e.ConnectionType, e.Address);

            e.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public Task<bool> TestPrintAsync(int id, CancellationToken ct = default)
            => _dispatch.DispatchTestAsync(id, ct);

        private static PrinterDto ToDto(Printer p) => new(
            p.Id, p.Name, p.Station, p.ConnectionType, p.Address, p.CopyCount, p.IsEnabled, p.CreatedOn, p.ModifiedOn);

        private static string NormalizeStation(string? station)
        {
            var s = (station ?? "").Trim();
            var match = AllowedStations.FirstOrDefault(a => a.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ArgumentException($"Invalid station '{station}'. Allowed: {string.Join(", ", AllowedStations)}.");
            return match;
        }

        private static string NormalizeConnectionType(string? connectionType)
        {
            var c = (connectionType ?? "").Trim();
            var match = AllowedConnectionTypes.FirstOrDefault(a => a.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ArgumentException($"Invalid connection type '{connectionType}'. Allowed: {string.Join(", ", AllowedConnectionTypes)}.");
            return match;
        }

        private static void ValidateAddress(string connectionType, string address)
        {
            if (connectionType != "Network") return;

            // Expect host:port for network printers.
            var idx = address.LastIndexOf(':');
            if (idx <= 0 || idx == address.Length - 1
                || !int.TryParse(address[(idx + 1)..], out var port)
                || port is < 1 or > 65535)
            {
                throw new ArgumentException(
                    "Network printer address must be in 'host:port' form, e.g. '192.168.1.50:9100'.");
            }
        }
    }
}
