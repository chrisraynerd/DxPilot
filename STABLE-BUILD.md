# Stable Build

This source tree matches the stable Configurable Rows Fix17 version protected
on 2026-08-06.

Verified executable:

`bin\ConfigurableRows-Fix17-VisibleFailedRowRetry\AutoResume.V3-2E0CCD.exe`

Protected snapshot:

`..\SAFE-STABLE-VERSIONS\AutoResume-ConfigurableRows-Fix17-VisibleFailedRowRetry-STABLE-20260806-185212`

Key behaviour includes:

- Configurable JTDX Band Activity row count with the personal 52-row default.
- Band/frequency and FT8/FT4 context with table reset and row-settling safety.
- Universal rankings and station displays across DX Assist, Wanted and Location.
- New and unconfirmed DXCC priority, including optional calling until stale.
- Worked-call display, suppression controls and CALL NOW override behaviour.
- ALL.TXT monitoring with immediate CQ and wrong-target recovery.
- Bounded GUI selection recovery and confirmed-target race protection.
- Failed sources retry after one receive period when their exact grid row remains
  visible; failed CQ/UDP sources fall back to that physical grid row.
- “Waiting for newer” is reserved for a failed source whose row has disappeared.

Verification:

- Release build completed with zero errors and zero warnings.
- Configurable Rows smoke and regression suite passed.

`PersonalConfigBaseline/` is private local recovery data and is excluded from
Git. Never publish it or include it in a GitHub repository/release.
