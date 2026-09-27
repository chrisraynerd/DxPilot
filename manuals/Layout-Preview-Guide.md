# DX Pilot layout preview — 24 September 2026

## Opening the preview

Finish any current QSO, stop assistance and close the existing DX Pilot window before opening `DXPilot-for-JTDX-G1CEC.exe` in this folder. Do not run both copies together. Your saved settings continue to be used.

The prior Achievements preview is unchanged. A separate backup, including the previous executable, source and a private settings snapshot, is stored in `SAFE-STABLE-VERSIONS/Before-UI-Tidy-20260924` beside the working project.

## What changed

- All four Start buttons, including Scavenger, are in the global header. There is one global Stop All control. The active mode's button is labelled accordingly.
- The same operating panel remains visible on every page: operating mode, band, active target, reason, calling/QSO progress, DX Pilot TX permission and JTDX target. Browsing a page does not start or change a mode.
- **TX permission is not actual transmission status.** Band Analysis retains its separate warning banner and Stop analysis control.
- Hunting pages have smaller headings and Hunt preferences expanders. The existing filters remain independent; they have not been silently shared across modes. **Include current band** is unchanged.
- Scavenger's band strip shows compact cards for enabled bands only. Expand Choose bands to see all twelve checkboxes. The existing lock while Scavenger runs remains in place. Hover a card for visit details.
- DX Assist's best observed opportunity is clearly identified as a ranked suggestion; the global header identifies the active target. Technical target details remain available in their expander.
- Settings is organised into sections. Search by a label such as LoTW, ADIF, grid, port or tolerance. Search opens matching sections, including advanced sections. Clear restores the previous expansion state. Search does not change settings or operate JTDX.
- Setup Wizard, Save Settings, Export Settings and Import Settings are always at the top of Settings. Existing setting bindings and commands were preserved.
- Achievements has an expandable Logbook checks and sources panel. It shows loaded QSO totals, missing station callsigns/grids/states, callsign credit counts and configured log paths. Refresh recalculates this information; it does not contact LoTW.

## What did not change

No target acquisition, scoring, transmission, QSO completion, suppression, Scavenger timing, band movement or confirmation-rule changes are included. Separate profiles for DXCC/grids/states are intentionally deferred to their own tested functional stage.

The full next-stage plan is included as `NEXT-STAGE-PLAN.md` and retained in the working source's `manuals` folder. Saved hunt setups, decision-time explanations, personal watchlists and further achievement analysis are not yet implemented.

## Checks

The presentation tests verify 111 protected source files against the pre-change backup, preserve existing Settings bindings, check global command routing and test settings search and band-control locks. Synthetic data was used for rendering; the live radio and saved operator settings were not exercised or changed by these tests.
