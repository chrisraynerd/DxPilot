# JTDX AutoResume V3

JTDX AutoResume is a Windows/WPF companion for JTDX that ranks live FT8/FT4
decodes and can select wanted DXCC entities, grids, US states and configured
geographical areas.

The current protected version is **Configurable Rows Fix17**. Its local Git tag
is `stable-configurable-rows-fix17-20260806`.

## Important safety note

AutoResume can control JTDX through UDP messages and calibrated screen clicks.
Always verify the JTDX window geometry, visible-row count, message-column click
position and Enable TX coordinates before enabling automatic hunting on another
computer or display configuration.

## Current capabilities

- Universal ranking shared by DX Assist, Wanted and Location views.
- Overall New DXCC as the highest-priority target.
- Optional current-band, current-mode and band-plus-mode wanted scopes.
- Configurable JTDX Band Activity row count (5–200 rows).
- Band/frequency detection and FT8/FT4-aware timing.
- Live table reset and row-settling protection after radio-context changes.
- Wanted DXCC, grid and US-state tracking independent of sniper checkboxes.
- LoTW-aware DXCC confirmation handling.
- Worked-callsign display and temporary/permanent suppression controls.
- Manual CALL NOW override.
- `ALL.TXT` monitoring for transmitted-message and wrong-target recovery.
- Settings export/import with QRZ passwords excluded from portable exports.
- Visible failed-row retry with a receive-period cooldown and GUI fallback.

## Requirements

- Windows 10 or Windows 11.
- .NET 8 Desktop Runtime when using the framework-dependent build.
- JTDX configured to send UDP Status and Decode messages to AutoResume.
- A correctly calibrated JTDX Band Activity grid for GUI row selection.

The personal development configuration listens on UDP port `2237` and forwards
raw packets to `127.0.0.1:2238`. Change these values if they conflict with other
logging or companion applications.

## Build

```powershell
dotnet build -c Release
```

## Verification

```powershell
dotnet run -c Release --project Tests\ConfigurableRows.SmokeTests\JtdxAutoResume.ConfigurableRows.SmokeTests.csproj
```

## Configuration and credentials

Live settings are stored outside the repository under:

`%APPDATA%\JtdxAutoResume.V3`

The `config` directory contains a QRZ-sanitized example of the personal testing
baseline. It intentionally retains the 52-row/test-rig values, so another user
must update paths, callsign, display coordinates and JTDX calibration before use.

Never commit a live `app_settings.json`, QRZ password, protected password value,
ADIF log, `ALL.TXT`, QRZ cache or Recent Actions export.

## Stable builds

Protected local snapshots are maintained outside this repository in the sibling
`SAFE-STABLE-VERSIONS` directory. Build outputs are ignored by Git and should be
attached separately to a GitHub Release if the project is published.

## Project status

This remains a personalized working application. A private GitHub repository is
recommended initially. Public distribution should add end-user setup guidance,
a licence decision and broader testing on different JTDX window layouts.
