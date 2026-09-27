# DX Pilot v4.4.0 — clearer operation, Scavenger and Achievements

This release packages the approved September layout preview. No additional changes
to target acquisition, transmission timing or QSO completion were made during packaging.

## What is new since v4.3.0

- A consistent operating header across all pages, with all four hunting-mode Start
  controls, one Stop All, active target, wanted reason and calling/QSO progress.
- A global achievement-profile selector, visibly locked while operating. TX permission
  is labelled separately from actual transmission status.
- Compact hunting headings and collapsible preferences. Existing filters and
  **Include current band** behaviour are preserved; browsing a tab does not switch modes.
- Receive-only **Scavenger** band searching, with wanted DXCC/grid/state opportunities,
  permitted-band controls, quiet-band rests, configurable target rests and a fresh
  listening period after a completed QSO. New DXCC priority and LoTW-user preference
  remain part of wanted selection. Scavenger does not run PSK propagation probes.
- Smaller Scavenger band cards and clearer wanted opportunities with station colours.
- Grouped, searchable Settings, with setup, save and settings import/export accessible
  at the top. Search finds setting labels, not credentials or entered values.
- Achievements adds narrow, numbered band cells for DXCC and a separate USA States
  area: green for LoTW-confirmed, amber for worked without LoTW confirmation, white
  for not worked. QSO detail lists and direct ADIF import are available on the page.
- Read-only logbook checks show missing identity/grid/state information and configured
  sources. Refreshing these checks does not contact LoTW.

## Download and upgrade

1. Download **DXPilot-for-JTDX-v4.4.0-win-x64.zip** from this release's Assets.
   The GitHub "Source code" downloads are for development, not the ready-to-run app.
2. Finish any QSO, stop assistance and close the old DX Pilot copy.
3. Extract the entire ZIP into a new folder. Keep its Data folder beside the executable.
4. Open **DXPilot-for-JTDX-G1CEC.exe**. Do not run two copies together.

This is a self-contained Windows x64 build. Settings remain in
`%APPDATA%\JtdxAutoResume.V3` and are reused on the same Windows account; extracting a
new release does not reset them. Keep that settings folder and your ADIF logs.
On a different PC, use settings export/import and verify local paths and JTDX mapping.

## Documentation and scope

The ZIP includes the illustrated **v4.1 baseline PDF manual**, plus the layout,
Scavenger and Achievements guides for subsequent features. The baseline PDF has not
been rewritten for v4.4.0. A Word copy remains available as a separate release asset.

The profile still applies globally to all achievement categories. Independent DXCC,
grid and state profiles, saved hunt setups and later roadmap items are not included.

The release workflow checks setup, application regression tests, maps, Scavenger,
Achievements and presentation. The presentation suite verifies that the layout work
preserved 111 protected source files and existing Settings bindings. Automated tests
are not an on-air test of every radio, theme or screen configuration.
