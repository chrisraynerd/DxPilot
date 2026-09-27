# DX Pilot — staged improvements agreed 24 September 2026

## Scope and safety

The user authorised as much as is safe in one pass and asked that the remaining work be remembered. Preserve stable calling, target locks, final-reply/QSO-completion protection, Stop All monitor-only behaviour, New DXCC priority and LoTW-user preference. Do not merge functional changes into a cosmetic release without separate tests and an explicit stage.

The user explicitly withdrew the request to rename **Include current band**. Keep that label.

## Stage 1: presentation-first preview (this update)

- One global set of Start controls, including Scavenger, with Stop All always available.
- One shared operating header on every page: active mode/band, target, reason, calling/QSO progress, DX Pilot TX permission and reported JTDX target. TX permission is explicitly not an indication of actual RF transmission.
- Remove redundant page-level status panels and Scavenger's local Start/Stop buttons.
- Compact hunting headings, consistent Hunt preferences expanders, retain existing independent filters and bindings.
- Scavenger shows compact enabled-band status cards; all twelve band choices remain in Choose bands, locked by the existing configuration guard while running.
- Group Settings into searchable sections; retain all existing setting bindings, setup wizard, commands and backup/import/export. Search reveals matching advanced sections and never searches credentials or entered values.
- Read-only Achievements logbook checks and configured-source paths, calculated only on the existing refresh path. No continuous monitoring added.

Backup before any edits: `../SAFE-STABLE-VERSIONS/Before-UI-Tidy-20260924`. Contains previous runnable build, source snapshot including uncommitted work, and private saved-settings snapshot. Do not distribute the settings snapshot.

## Stage 2: independent achievement identities — highest functional priority

Release checkpoint, 27 September 2026: the user approved the Stage 1 layout and requested
its publication as v4.4.0. Stage 2 has not been implemented and is not included in that
release. The approved preview and source were backed up locally before packaging.

Example requested: DXCC credit for **G1CEC**, but grids and USA states for **All callsigns**.

- Default to one identity for everything; allow explicit per-category identities.
- Migrate an existing single profile by assigning it to all categories, preserving current classifications exactly until the operator opts in.
- Keep identity (whose QSO counts), confirmation rule (LoTW by default), and scope (overall/band/mode/band+mode) separate.
- Show a compact global summary such as DXCC: G1CEC · Grids: All · States: All.
- Decide and document the unchanged/fallback rule for IOTA; do not silently apply a different identity.
- Make wanted classification, colour, reason, map and Achievements agree. Worked-before tooltips must retain the actual station callsign used, rather than confusing old-call credit with current-call credit.
- Preserve profile locking during hunting/analysis and keep transmission/QSO state machines untouched.
- Test old/current/all/unknown identities, mixed-category targets, no credit leaking between categories, LoTW vs other confirmations, every achievement scope and New DXCC/LoTW-user priority.

## Stage 3: clearer decision explanations and saved hunt setups

- Extend existing reasons into a consistent Why this station? / Why not this station? explanation, based on actual selection and eligibility decisions. Do not invent explanations from the currently displayed row.
- Preserve decision-time mode, band, profiles, filters, reason and outcome in Session History; later profile/import changes must not rewrite the explanation of a past decision.
- Shared wanted preferences for Wanted and Scavenger where semantics match, with any override clearly identified. DX Assist must remain capable of general DX; Location retains its region choices. Viewing a tab must never switch the running mode.
- Distinguish display filters from hunt eligibility controls.
- Saved hunt setups: mode + relevant goals/filters/bands/preferences, excluding calibration, ports and credentials. Loading a setup must not start transmission; apply only at a safe stopped boundary.

## Later additions to evaluate

- Personal callsign watchlist: alert-only default; opt-in wanted treatment; optional expiry; no automation while stopped; preserve active-QSO protection and New DXCC priority.
- Achievements: needed-on-current-band filter, per-band country/state totals, grids overview, and new confirmations since a previous successful import. Store a reliable comparison baseline and distinguish new QSOs from newly imported confirmations. Keep previous baselines on failed/cancelled imports.
- Expand logbook checks into inspectable affected-record lists and import summaries, using already merged data without changing deduplication rules.
- True current-radio TX/receiving status in the common header only if supported by fresh existing status; never infer actual transmission from permission or stale data.
- Additional readiness feedback, using evidence from existing connections/mapping validation; do not add test clicks or CQ probes automatically.

## Intentionally not included now

No new hunting mode, timing changes, target-scoring changes, automatic retesting or band-selection redesign. No GitHub release was requested for this stage; provide a separate local preview first.
