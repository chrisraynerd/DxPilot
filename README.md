# DX Pilot for JTDX

**DX Pilot for JTDX**, by G1CEC, is a Windows/WPF companion that ranks live
FT8/FT4 decodes and can select wanted DXCC entities, grids, US states and
configured geographical areas.

The current release is **v3.4.1**, adding a remembered 5%-50% opacity slider to
the LoTW-confirmed Grid4 overlay, with a more visible 25% default, plus separate
all-time, current-band and current-mode fill scopes. It also automatically clears
live map stations when JTDX changes band. Confirmation shading remains visible across band changes and
updates whenever the merged ADIF logbook is rebuilt. It also includes the v3.3.1
map-marker double-click CALL NOW and disabled unable-to-contact state, plus the
v3.3.0 event-led Current Session history
and permanent searchable Full Archive. Every valid station heard is retained,
whether wanted or ordinary, together with real decode counts, verified transmit
attempts, selection method, outcome and worked status. New/unconfirmed DXCC,
grid and state records use the same semantic colours as the live views. The
release also includes the interactive OpenStreetMap-based
Maidenhead grid map with colour-coded stations, age fading, home paths, pan/zoom
and the existing safety-checked CALL NOW workflow. Cached QRZ coordinates can
refine a four-character locator when both sources agree, using a paced background
refresh for legacy cache entries. Map colours can be viewed by overall, band, mode
or band-plus-mode status, while Grid6 positions are matched to Grid4 worked status
in the same way as Wanted. Detailed squares gain faint, viewport-limited Grid4
labels. The red active-call override is isolated so every other marker retains its
DXCC/grid/state colour throughout TX. Retained, throttled map layers keep live
decode bursts responsive. It also includes the v3.1.1 ranking behaviour, guided
setup wizard and Configurable Rows Fix17 safety baseline.

When all assistance is off, **CALL NOW** now runs as a one-station session and
returns to fully stopped after that station succeeds, fails, times out or is
released. If DX Assist, Wanted Sniper or Location Hunt was already active, its
existing mode is preserved and resumes after the manual station.
Map grid colours now classify the same effective/transmitted/ADIF/QRZ locator
used to plot each station, preventing wanted or unconfirmed grids from reverting
to orange after a later gridless decode. Map confirmation colours also use the
same LoTW confirmation sets as Wanted, so paper/eQSL-only grids remain visibly
unconfirmed rather than incorrectly appearing as ordinary orange stations.

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

- Interactive live OpenStreetMap with Maidenhead field/square overlays, session
  station history, adjustable stale time, colour-coded wanted status and CALL NOW.
  Map tiles require internet access; already fetched tiles are cached by the map engine.
- QRZ latitude/longitude map refinement reuses the existing cached lookup pipeline;
  it cannot override a transmitted six-character locator or a conflicting parent square,
  and fixed QRZ coordinates are ignored for portable/mobile calls.
- Universal ranking shared by DX Assist, Wanted and Location views.
- DX Assist keeps needed DXCC at absolute priority and leaves new-grid promotion off by default. The optional **Give new grids priority** control promotes globally new grids, then automatically falls back to normal DX ranking when none are available.
- Wanted Sniper remains a separate strict wanted-item mode; its grid choices do not change DX Assist ranking.
- Overall New DXCC as the highest-priority target.
- Optional current-band, current-mode and band-plus-mode wanted scopes.
- Configurable JTDX Band Activity row count (5–200 rows).
- Band/frequency detection and FT8/FT4-aware timing.
- Live table reset and row-settling protection after radio-context changes.
- Wanted DXCC, grid and US-state tracking independent of sniper checkboxes.
- LoTW-aware DXCC confirmation handling.
- Worked-callsign display and temporary/permanent suppression controls.
- Manual CALL NOW override, with one-station operation when assistance was off
  and automatic continuation only when a hunting mode was already active.
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
dotnet run -c Release --project Tests\Map.SmokeTests\Map.SmokeTests.csproj
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
