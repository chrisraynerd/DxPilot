# DX Pilot for JTDX

**DX Pilot for JTDX**, by G1CEC, is a Windows/WPF companion that ranks live
FT8/FT4 decodes and can select wanted DXCC entities, grids, US states and
configured geographical areas.

The current release is **v3.1.0**, adding the guided setup wizard while retaining
the Configurable Rows Fix17 safety baseline.

## Download

Open the [DX Pilot Releases page](https://github.com/chrisraynerd/DxPilot/releases),
choose the latest release, expand **Assets**, and download the Windows `win-x64.zip`
file. Extract the complete ZIP to a normal folder, then run
`DXPilot-for-JTDX-G1CEC.exe`.

The downloadable package is self-contained, so users do not need to install the
.NET Desktop Runtime separately. Windows may display a SmartScreen warning until
the application is code-signed.

The setup wizard does not open automatically. Open **Settings** in DX Pilot and
choose **Run Setup Wizard** at the top of the page.

## Important safety note

DX Pilot can control JTDX through UDP messages and calibrated screen clicks.
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
- Guided setup for station identity, adaptive JTDX/GridTracker/logger UDP paths,
  log files, Enable TX safety calibration and the JTDX Band Activity grid.
- Visible failed-row retry with a receive-period cooldown and GUI fallback.

## Requirements

- Windows 10 or Windows 11.
- .NET 8 Desktop Runtime when using the framework-dependent build.
- JTDX configured to send UDP Status and Decode messages to DX Pilot.
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
dotnet run -c Release --project Tests\SetupWizard.SmokeTests\SetupWizard.SmokeTests.csproj
```

## Configuration and credentials

Live settings are stored outside the repository under:

`%APPDATA%\JtdxAutoResume.V3`

That legacy folder name and the `JtdxAutoResume.V3` UDP identity are deliberately
retained so existing installations keep their settings and JTDX integration
after the product rename.

The `config` directory contains a QRZ-sanitized example of the personal testing
baseline. It intentionally retains the 52-row/test-rig values, so another user
must update paths, callsign, display coordinates and JTDX calibration before use.

Never commit a live `app_settings.json`, QRZ password, protected password value,
ADIF log, `ALL.TXT`, QRZ cache or Recent Actions export.

## Stable builds

Protected local snapshots are maintained outside this repository in the sibling
`SAFE-STABLE-VERSIONS` directory. Pushing a version tag such as `v3.1.0` runs the
release workflow, builds the self-contained Windows package and attaches its ZIP
to a GitHub Release automatically.

## Project status

This remains a developing application. Wider public distribution would still
benefit from code signing, a documented licence decision and broader testing on
different JTDX window layouts.
