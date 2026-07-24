# Axis Print Agent

Small on-site relay that receives print jobs from the cloud API over SignalR and
forwards the raw ESC/POS bytes to the venue's thermal printers. It exists because
the API runs in Azure and cannot open a socket to a `192.168.x` printer on the
venue LAN — this agent, running on a PC at the venue, bridges that gap.

```
Cloud API ──SignalR "PrintJob"──▶ Print Agent ──▶ Kitchen printer (Network TCP:9100)
                                              └──▶ Bar printer     (USB / Network)
```

## Configure

Edit `appsettings.json`:

| Key | Meaning |
| --- | --- |
| `PrintAgent:HubUrl` | The API's printer hub, e.g. `https://<api-host>/hubs/printer`. |
| `PrintAgent:AccessToken` | Optional JWT (only if the hub is later secured). Leave blank. |
| `PrintAgent:Stations` | Optional. Empty = handle every printer. Set `Kitchen` or `Kitchen,Bar` to scope one agent to specific stations (for multi-PC / multi-agent setups). |
| `PrintAgent:NetworkTimeoutMs` | TCP connect/write timeout for network printers (default 5000). |

Printers themselves are configured in the dashboard (**Admin → Printers**), not here.
Each printer there has a `ConnectionType` (`Network` or `Usb`) and an `Address`:

- **Network:** `ip:port` — e.g. `192.168.1.50:9100`.
- **Usb:** the exact Windows printer name — e.g. `EPSON TM-T20II Receipt`.

## Run

```powershell
cd PrintAgent
dotnet run
```

You should see `Connected to print hub.` then `Joined Printers group; ready to print.`
Fire a **Test print** from Admin → Printers to confirm a ticket comes out.

## Install as a Windows service (recommended for POS PCs)

Publish a self-contained exe and register it with the SCM so it runs headless at boot:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o C:\AxisPrintAgent
sc.exe create "AxisPrintAgent" binPath= "C:\AxisPrintAgent\AxisPrintAgent.exe" start= auto
sc.exe start "AxisPrintAgent"
```

Logs go to the console (and the Windows Event Log when running as a service).

## One agent vs many

- **One agent per venue (typical):** leave `Stations` empty. A single agent on the LAN
  can reach every network printer and any USB printer installed on that PC.
- **Multiple agents (e.g. USB printers on different PCs):** run one agent per PC and set
  `Stations` on each so they don't both grab the same job.
