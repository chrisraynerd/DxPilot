# DX Pilot for JTDX

**DX Pilot for JTDX**, by G1CEC, is a Windows/WPF companion that ranks live
FT8/FT4 decodes and can select wanted DXCC entities, grids, US states and
configured geographical areas.

The next development release is **v4.0.0**, a presentation-only redesign built on
the unchanged v3.9.7 operating engine. It introduces a persistent radio/target/TX
header, designed workspace navigation in place of native Windows tabs, a true Live
Monitor, and distinct visual identities for DX Assist, Wanted Sniper and Location
Hunt. No target-selection, QSO, TX, band-movement, UDP or PSK behaviour is changed.

The current stable release is **v3.9.7**. Long-running sessions now batch Session
History screen refreshes and persist the permanent archive away from the WPF UI thread,
so a large Full Archive no longer causes a complete synchronous JSON rewrite during
normal interaction. Every main screen also carries a compact shared view of the live
Automatic Band Analysis trigger bars, while Dashboard retains the detailed monitor cards.
Band Analysis is now presented as one
clear receive-plus-PSK workflow: every enabled band is sampled for received targets
and then tested with exactly two verified propagation CQs. The receive-only engine
remains available as a collapsed diagnostic/fallback instead of a competing primary
survey. Automatic Conditions Search can use the full workflow after the user grants
the separate automatic-CQ permission; upgraded installations keep that permission
off until it is explicitly selected.

A persistent application-wide banner announces a pending or active automatic Band
Analysis, shows the trigger reason, current band/phase and whether CQ probes are
transmitting, and provides a Stop action. The Dashboard now displays five coloured
bars which empty toward the actual cooldown, minimum-band-time, unanswered-call,
no-useful-target and low/silent-band thresholds. The Band Analysis settings use
plain-language labels and explain precisely what every duration or count controls.
The no-useful-target bar resets only when the active assistance mode accepts a real
target; routine CQ decodes no longer keep a busy but unproductive band at 100%.

The previous stable release was **v3.9.6**. PSK survey cancellation waits for a fresh
post-click JTDX status before deciding whether Enable TX is on or off. This closes
the race where a queued Enable TX click could be processed after survey cleanup and
leave JTDX calling CQ. Malformed decode text can no longer interrupt a survey as a
false New DXCC, and a band move that remains on the old UDP-reported band receives
one guarded retry of the same mapped button before it fails safely.

Band Analysis includes live **PSK Reporter**
propagation measurement. A transmitted survey connects to the callsign-filtered
live feed before visiting enabled bands, records the exact UTC start of both verified
CQ probes, and accepts only FT8 reports whose band and transmission time match one
of those probes. After the final band it allows eight seconds for immediate reports
and performs one rate-conscious official retrieval over the previous 30 minutes to
reconcile reports already stored by PSK Reporter. It never repeatedly polls the
retrieval service or forces a five-minute survey delay. If the live feed is unavailable,
the retrieval remains a fallback; a total service failure is shown as unavailable
rather than incorrectly interpreted as poor propagation.

Each band now shows unique PSK receivers, farthest outward reach, strongest report,
main outward area, a transparent 0-100 propagation score and a detailed summary with
country/continent spread, distant receivers and median SNR. These outward results are
stored in Band Analysis history and contribute modestly to Choice score. Received
targets remain the main input and any New DXCC retains absolute 10,000+ priority.

The final PSK report now includes a world map below the results table. Each unique
receiver-and-band result is plotted with a distinct band colour, repeated reports are
reduced to the strongest report, and selecting a dot shows its receiver, locator,
band, report strength and UTC time. The band legend and the selected-dot panel both
provide a guarded manual **Use band** action. When a PSK survey was started from an
active assistance mode, DX Pilot combines the received target quality, outward PSK
propagation and recent trend, moves to the winning surveyed band, and resumes the
same assistance mode there. Exact ties remain on the current band, while an observed
New DXCC still overrides every numerical score and immediately enters the existing
priority calling path.

The latest successful locator-bearing PSK map is now saved separately and restored
when DX Pilot starts. Beginning another survey retains the previous dots until newer
usable results are ready, while a failed or zero-locator survey cannot erase the last
useful plot. Saved points are deduplicated by receiver and band with the strongest
report retained.

A manually started receive-only Band Analysis now behaves the same way when an
assistance mode is active: it temporarily pauses that mode, uses any genuinely
stronger survey winner, confirms the final JTDX band movement and resumes the same
mode on that band. Standalone surveys still honour **Return to starting band**, and
empty or exactly tied results do not cause an arbitrary move.

PSK transmission is now a strictly standalone sequence. It pauses assistance, runs
the existing verified two-CQ probes, completes PSK reporting, makes and confirms any
final band movement, then physically restores JTDX to a separately mapped Tx1 button
before releasing control. The cleanup requirement is written to settings before the
first Tx6 click, so an application restart cannot forget that JTDX may still be on
Tx6. If Tx1 cannot be restored while receive-only, normal hunting remains stopped.

Once that handover completes, normal target acquisition and TX recovery use the exact
v3.4.3 methods again: CQ targets use UDP Reply, directed sources use the calibrated
Band Activity grid, and no post-PSK forced-grid or temporary-message gate remains.
Continuous
between-survey quality scoring was also removed: Conditions Search retains only its
lightweight trigger counters, while full distance/geographical scoring runs during
an actual Band Analysis survey.

The guarded FT8 transmission sequence collects a complete passive sample across both
FT8 periods and sends exactly two consecutive CQ probes. It arms JTDX immediately
before a real UTC boundary, waits for each complete transmission, changes the mapped
**Tx 15/45** or **Tx 00/30** timing only in the inter-period gap, and restores the
original selection before moving. The configured one-to-five-minute measurement
changes passive listening time only; it never adds more than two CQs per band. Manual
PSK surveys can start with all hunting modes off and return to the stopped state when
complete; if hunting was active, its previous mode resumes.

**Automatic Conditions Search** continuously
maintains a rolling view of the current band and can start a guarded Band Analysis
after persistent silence, low station numbers, no useful targets, poor replies across
several called stations, application startup, or configured UTC times. A cooldown,
minimum time on the chosen band and meaningful-improvement margin prevent repeated
or marginal band changes. It preserves and resumes the active hunting mode, while
an observed New DXCC immediately stops the survey and hands the station to the
normal absolute-priority calling path.

Band Analysis history is retained for 60 days and labels each band as building
history, stable, improving, emerging, easing or declining. Trend is a small
tie-breaker rather than an opaque prediction, and each result records the trigger
that caused it, the starting band, selected destination and decision. A readable
`band_analysis_history.csv` audit log is stored in `%APPDATA%\JtdxAutoResume.V3`
and can be opened directly from Band Analysis. The receive-only **Band Analysis** survey
maps JTDX's fixed 12-button band strip, visits only user-enabled bands for an
adjustable one-to-three minutes, confirms each move from a fresh JTDX UDP status,
discards the partial receive cycle before starting each fair timed sample, and
compares unique stations, CQ callers, main geographic activity, DX reach,
wanted opportunities and confidence-oriented quality labels. It stops all hunting
and requires JTDX to report TX disabled before any survey band click.

The release retains the v3.4.3 map behaviour. The map's red active-station marker now follows
the actual locked hunting/QSO target in Wanted Sniper, DX Assist, Location Hunt,
CALL NOW and adopted inbound QSOs. Clearing live map dots no longer forgets an
active target, so its next plotted decode immediately returns as red.

It retains the v3.4.2 remembered, key-free OpenStreetMap and Esri basemap choices,
the world-scale raster-cached LoTW-confirmed Grid4 overlay, and the QRZ-result
stability fix. QRZ request volume and enrichment remain unchanged.

The release retains the remembered 5%-50% confirmation-opacity slider, its 25%
default, separate all-time/current-band/current-mode fill scopes, and automatic
live-station clearing when JTDX changes band. Confirmation shading updates whenever
the merged ADIF logbook is rebuilt. It also includes the v3.3.1
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
Band Analysis additionally requires its 160m-to-2m button strip to be mapped and
each intended band movement to be tested before a survey.
The PSK propagation survey also requires JTDX's Tx 15/45 / Tx 00/30 timing button to be
mapped. It displays a transmission warning and requires confirmation every time it
is started manually.

## Current capabilities

- Receive-only Band Analysis with a guided 12-button strip calibration, permitted-band
  checkboxes, adjustable dwell time and cycles, fresh UDP band confirmation, live
  per-band results and a plain-language survey overview. Activity and DX reach are
  kept separate so a quiet long-DX opening can outrank a crowded regional band.
- Guarded FT8 PSK propagation probe sequencing with one-to-five-minute passive
  measurements, exactly two opposite-period CQs per band, positive transmission
  confirmation of immediately adjacent FT8 slots, and restored timing selection.
- Automatic Conditions Search with continuous rolling monitoring, safe deferred
  triggers, quick and full confirmation surveys, cooldown/residence protection,
  scheduled UTC checks, automatic best-band movement, absolute New DXCC interruption,
  and persistent emerging/declining band history.
- Interactive live map with remembered OpenStreetMap, Esri World Street,
  World Topographic, Light Gray and Dark Gray public cached basemaps.
  Maidenhead overlays, session station history, adjustable stale time,
  colour-coded wanted status and CALL NOW remain independent of the basemap.
  The Esri choices require no account or API key.
  LoTW-confirmed Grid4 polygons are built in the background and raster-cached,
  so the confirmation overlay remains visible from regional to world scale.
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
