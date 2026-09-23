# POS Agent

`PosAgent.Api` runs locally on every Windows POS terminal.

Its responsibilities are intentionally local:

- enumerate printers installed in Windows on that POS;
- send a heartbeat and discovered queue names to Restaurant Node;
- cache the POS receipt-printer assignment locally;
- expose local-only HTTP endpoints on `127.0.0.1:8791` for the Flutter POS client;
- print locally even when Restaurant Node is temporarily unavailable.

## Printing modes

Configured printers use two different paths:

- `WindowsQueue` -> Windows printer driver / GDI. This is the normal path for printers installed in Windows, including laser printers and receipt printers with a Windows driver. Unicode text is rendered by Windows.
- `Network` -> direct TCP/IP RAW printing, default port `9100`. The current test payload uses ESC/POS and is intended for compatible thermal receipt/kitchen printers.

Windows queues are not sent ESC/POS RAW bytes anymore.

## Development run

Create a POS device in BackOffice first and copy its Device ID.

```powershell
cd C:\Restaurant_pos_system\Restaurant_pos_system\services\pos-agent\src\PosAgent.Api

$env:RestaurantNode__BaseUrl="http://127.0.0.1:8180"
$env:Restaurant__Id="11111111-1111-1111-1111-111111111111"
$env:Device__Id="PASTE-POS-DEVICE-ID-HERE"
$env:Agent__SharedKey="restaurant-pos-development-agent-key-change-me-2026"

dotnet run
```

Restaurant Node must use the same shared key:

```powershell
$env:Agent__SharedKey="restaurant-pos-development-agent-key-change-me-2026"
```

For a real restaurant where Restaurant Node is on another machine, replace `127.0.0.1` in `RestaurantNode__BaseUrl` with the LAN address of Restaurant Node.

## Local endpoints

- `GET http://127.0.0.1:8791/health`
- `GET http://127.0.0.1:8791/api/v1/printers`
- `GET http://127.0.0.1:8791/api/v1/config`
- `GET http://127.0.0.1:8791/api/v1/status`
- `POST http://127.0.0.1:8791/api/v1/print/test-receipt`

The agent only listens on localhost. The BackOffice never connects directly to a POS Agent; the agent reports its state to Restaurant Node instead.

## Offline behavior

The most recent receipt-printer assignment is cached locally. The local test-print endpoint reads that cache, so after a successful sync the print step does not require internet or a live Restaurant Node connection.

Full order/payment operation while Restaurant Node is down is a separate future feature and is not implied by local printing.
