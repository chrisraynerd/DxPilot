# Next update

## Draggable JTDX message-click guide

The orange vertical grid-overlay line represents the X coordinate used for
GUI row double-clicks.

Required improvement:

- Allow the user to drag the orange line directly onto the decoded-message
  text column in JTDX.
- Moving or resizing the main row overlay must preserve the selected click
  position relative to the calibrated grid.
- Keep the guide constrained inside the calibrated Band Activity rectangle.
- Save the selected position in `JtdxBandMessageClickX`.
- Show a clear cursor/drag affordance so the guide is not confused with a row
  boundary.
- The existing click-time JTDX size and minimised-window safety checks remain
  unchanged.

This is intentionally deferred and is not part of ConfigurableRows-Fix1.

## Settings export and import

Add a safe backup/restore facility to the Settings screen in a future update.

Proposed controls:

- Export Settings
- Import Settings

The exported, versioned JSON bundle should include:

- Application settings and hunting preferences.
- Wanted, Location and confirmation options.
- Permanent callsign suppressions.
- UDP settings.
- Pixel colours and coordinates.
- JTDX visible-row count and grid calibration.
- ADIF paths.
- Band schedule entries.
- Export timestamp, application version and configuration format version.

Do not include:

- ADIF log contents.
- Recent Actions.
- Session history.
- Live decodes.
- QRZ cache data.
- The QRZ password.

Import safety requirements:

- Stop hunting and automatic clicking before importing.
- Validate the file and configuration version before changing anything.
- Automatically back up the current live settings and schedule.
- Import settings and schedule as one operation.
- Preserve the existing local QRZ password.
- Require an application restart after a successful import.
- Remind the user to verify machine-specific ADIF paths, screen/pixel
  coordinates, JTDX calibration, row count and UDP ports.

This is intentionally deferred and is not part of ConfigurableRows-Fix1.
