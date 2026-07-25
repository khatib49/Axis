using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrintAgent;

/// <summary>
/// The print job the API pushes over SignalR. Field names/casing must match
/// Application.DTOs.PrintJobDto on the server.
/// </summary>
public record PrintJob(
    int PrinterId,
    string PrinterName,
    string Station,
    string ConnectionType,
    string Address,
    int TransactionId,
    int Copies,
    string PayloadBase64);

public class PrintWorker : BackgroundService
{
    private readonly ILogger<PrintWorker> _logger;
    private readonly IConfiguration _config;

    private HubConnection? _connection;
    private HashSet<string> _stationFilter = new(StringComparer.OrdinalIgnoreCase);
    private int _networkTimeoutMs = 5000;

    public PrintWorker(ILogger<PrintWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _config["PrintAgent:HubUrl"];
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            _logger.LogError("PrintAgent:HubUrl is not configured in appsettings.json. Nothing to do.");
            return;
        }

        var token = _config["PrintAgent:AccessToken"];
        _networkTimeoutMs = int.TryParse(_config["PrintAgent:NetworkTimeoutMs"], out var t) ? t : 5000;

        var stations = _config["PrintAgent:Stations"];
        if (!string.IsNullOrWhiteSpace(stations))
        {
            _stationFilter = stations
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("Station filter active: only handling {Stations}", string.Join(", ", _stationFilter));
        }
        else
        {
            _logger.LogInformation("No station filter — this agent handles jobs for every printer.");
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl!, options =>
            {
                if (!string.IsNullOrWhiteSpace(token))
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        _connection.On<PrintJob>("PrintJob", async job => await HandleJobAsync(job));

        _connection.Reconnected += async _ =>
        {
            _logger.LogInformation("Reconnected to hub. Re-joining Printers group.");
            await SafeJoinAsync();
        };

        _connection.Closed += async error =>
        {
            _logger.LogWarning(error, "Hub connection closed. Attempting to reconnect...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await ConnectWithRetryAsync(stoppingToken);
        };

        await ConnectWithRetryAsync(stoppingToken);

        // Stay alive until the host shuts down.
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }

        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _connection!.State == HubConnectionState.Disconnected)
        {
            try
            {
                _logger.LogInformation("Connecting to print hub...");
                await _connection.StartAsync(ct);
                _logger.LogInformation("Connected to print hub.");
                await SafeJoinAsync();
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Connect failed; retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task SafeJoinAsync()
    {
        try
        {
            await _connection!.InvokeAsync("JoinPrinters");
            _logger.LogInformation("Joined Printers group; ready to print.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to join Printers group.");
        }
    }

    private async Task HandleJobAsync(PrintJob job)
    {
        if (_stationFilter.Count > 0 && !_stationFilter.Contains(job.Station))
        {
            _logger.LogDebug("Ignoring job for station {Station} (not in this agent's filter).", job.Station);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(job.PayloadBase64);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job for printer {Printer} had an invalid payload.", job.PrinterName);
            return;
        }

        var copies = Math.Max(1, job.Copies);
        for (var i = 0; i < copies; i++)
        {
            try
            {
                if (string.Equals(job.ConnectionType, "Network", StringComparison.OrdinalIgnoreCase))
                    await SendNetworkAsync(job.Address, bytes);
                else if (string.Equals(job.ConnectionType, "Usb", StringComparison.OrdinalIgnoreCase))
                    SendUsb(job.Address, bytes);
                else
                {
                    _logger.LogError("Unknown ConnectionType '{Type}' for printer {Printer}.", job.ConnectionType, job.PrinterName);
                    return;
                }

                _logger.LogInformation(
                    "Printed Trx {Trx} to {Printer} ({Station}) copy {Copy}/{Copies}.",
                    job.TransactionId, job.PrinterName, job.Station, i + 1, copies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to print Trx {Trx} to {Printer} at {Address}.",
                    job.TransactionId, job.PrinterName, job.Address);
                break; // don't hammer a broken printer with the remaining copies
            }
        }
    }

    /// <summary>Sends raw ESC/POS to a network thermal printer over TCP (typically port 9100).</summary>
    private async Task SendNetworkAsync(string address, byte[] bytes)
    {
        var (host, port) = ParseHostPort(address);

        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(_networkTimeoutMs);

        await client.ConnectAsync(host, port, cts.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(bytes, cts.Token);
        await stream.FlushAsync(cts.Token);
    }

    private static void SendUsb(string printerName, byte[] bytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("USB/Windows printing is only supported on Windows.");
        RawPrinterHelper.SendBytes(printerName, bytes);
    }

    private static (string host, int port) ParseHostPort(string address)
    {
        var idx = address.LastIndexOf(':');
        if (idx <= 0 || idx == address.Length - 1 || !int.TryParse(address[(idx + 1)..], out var port))
            throw new FormatException($"Network address '{address}' must be 'host:port', e.g. '192.168.1.50:9100'.");
        return (address[..idx], port);
    }
}

/// <summary>Reconnects forever with a capped backoff so a venue PC recovers on its own.</summary>
internal sealed class InfiniteRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var seconds = Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
        return TimeSpan.FromSeconds(seconds);
    }
}
