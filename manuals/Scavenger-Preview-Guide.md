# Scavenger — preview 6

## After a successful QSO

A successfully logged contact now starts a fresh listening period on the same band, using **Listen (minutes)**. It starts after the existing completion pause and receive-only checks, aligned to a full receive slot. The time spent calling does not count towards this new sample. Another eligible wanted station can be called during the listen; there is no requirement to wait until it ends. If no target is found, normal rotation resumes when the fresh sample finishes. Failed/unlogged completion and the unanswered calling limit do not gain this extra listening visit. Stop All cancels the pending listen, and a manual band change is never reversed.

## LoTW preference and row colours

LoTW users are preferred within the same wanted-priority tier, ahead of distance/score. This existing rule is now covered by dedicated regression checks. Never-worked overall DXCC remains first, followed by overall unconfirmed DXCC and the other configured needs; a LoTW user in a lower tier does not displace a higher-priority DXCC.

Wanted Opportunities now uses the shared application colour scheme: purple shades for new/unconfirmed DXCC, blue for grids and teal for states. The active target keeps its category colour with the usual orange calling or green QSO marker. Resting/suppressed and unavailable rows use their existing status colours. The global station-colour legend applies here too.

## Wanted opportunities, not raw band decodes

The main Scavenger display now has **Wanted DXCC**, **Wanted Grids** and **Wanted States** tables. These are read-only opportunity views for the current band, using the global Achievement Profile and Scavenger's own scopes. They do not use Wanted Sniper's separate category switches.

The **Scavenger target & reason** summary identifies the locked target or a station being watched for return. Highlighted rows identify the active target. A row marked **Eligible** is an opportunity, not a promise that it is next in the calling queue. Other statuses explain category-off, rest rounds, permanent/temporary suppression, recent QSOs or the need for a selectable decode. Switched-off categories remain visible without being enabled for calling. Never-worked overall DXCC retains its existing priority override.

Hover a row for the full wanted reason, availability status and decoded message. Right-click **QRZ** still opens the station lookup. The live tables clear on band changes; Session History remains the place to review stations across the whole session. Raw decodes remain available in Live Monitor. Band cards, filter controls, calling, rest limits and rotation behavior have not changed in this preview.

## Unanswered calling limit and target rest

On Scavenger, open **Targets, scopes and search settings** while stopped. **Maximum unanswered calling (minutes)** defaults to **10** and can be selected from 1–60. **Then rest that callsign (search rounds)** defaults to **2** and can be selected from 1–12. Settings are saved with your other Scavenger choices.

For overall new/unconfirmed DXCC targets, timing starts at the first recorded calling transmission. Further calls or hearing the station talking to others do not restart it. At the limit, the current transmission and final reply window are allowed to finish before search resumes. Replies to you and QSO completion remain protected. Other categories retain their normal attempt limits.

The callsign then rests across all bands for two complete subsequent search rounds (or your selected number). The unfinished remainder of the current round does not count. A summary above the live results shows resting calls and rounds remaining. Other calls from the same country remain eligible. This is not permanent suppression; a later eligible attempt gets a fresh allowance. Stopping/restarting Scavenger starts a new search and clears these temporary rests.

## Partial-grid selection correction

All hunting modes and manual grid selection now support a partially filled Band Activity pane after DX Pilot observes a confirmed band change. The row model starts from the cleared pane's top, counts cycle separators, and returns to the existing scrolled-grid coordinates when the pane fills. There is no need to accumulate 52 rows first. Existing batch-settling, window-size, target-presence and JTDX callsign-confirmation checks remain.

If DX Pilot starts while JTDX already has an unknown amount of text on screen, it cannot assume the pane was cleared. A confirmed band change establishes the origin. A discarded transition decode also invalidates the top origin because it may occupy an untracked row. No speculative double-click is made in those uncertain cases.

Scavenger now distinguishes selecting a target from a target confirmed in JTDX; it no longer prematurely labels a failed acquisition as calling.

Scavenger listens across your permitted bands for wanted stations. It does not run Band Analysis or transmit PSK/CQ probes. Calling uses DX Pilot's existing target-selection and QSO engine.

## Open the test build

Close the previous DX Pilot copy before starting this one. Run DXPilot-for-JTDX-G1CEC.exe from the new preview folder; existing saved settings load normally. Do not run two copies against JTDX. This is a local preview, not a published stable release.

## One Achievement Profile for the application

The selector is now in the main header, immediately before Start DX Assist. Select All callsigns or a station identity while stopped. The selection supplies the logbook credit used by the hunting modes, wanted displays, maps and Achievements. Achievements refreshes when the profile changes.

During hunting or Band Analysis, the selector is replaced by the selected profile's name and a PROFILE LOCKED label. Stop the operation before changing it. Changing a profile does not change your transmitted callsign or delete/rewrite historical session evidence.

## Scavenger targets and scopes

Open Scavenger and expand **Targets, scopes and search settings**.

- **New / unconfirmed DXCC** is enabled by default. Unlike preview 1, a country already worked but awaiting LoTW confirmation is eligible.
- **Wanted grids** and **Wanted USA states** are optional.
- Overall needs are always considered. Enable **Current band**, **Current mode**, or **Band + mode** to include additional needs in those scopes. For example, a state confirmed overall may still be wanted on 20m.
- These target/scope choices are independent of Wanted Sniper's checkboxes.
- Confirmation follows the existing global confirmation settings, which default to LoTW. Selecting a different supported confirmation policy in Settings also changes the corresponding wanted classification here.
- A never-worked overall DXCC retains global priority even if the DXCC category is unchecked. Eligible overall unconfirmed DXCCs come next, ahead of optional band-specific DXCC, state and grid opportunities.
- LoTW users are preferred within the same priority level, without displacing a higher-priority new DXCC.

An overall new/unconfirmed DXCC is protected until stale, including during an exchange. After it goes stale, Scavenger stays on the band receiving for five more minutes to catch its return. Fresh selectable evidence allows reacquisition. The existing New DXCC last-heard threshold defines stale.

Optional grid, state and band-specific targets use the normal calling limits. They do not receive an unlimited DXCC hold or a five-minute return watch. A wanted overall DXCC can take priority before a lower-priority target has replied; an exchange already in progress retains its protection.

## Band cards and wanted tables

All twelve band buttons are represented by compact tick cards, in two rows. Tick only the bands your station may visit. These permissions are shared with Band Analysis and use the same mapped band-button strip.

The current band is highlighted. Each card shows visit state, heard count when available, and return status. Hover for received activity and full revisit detail.

The three wanted tables below use current-band observations and separate DXCC, grid and USA state needs. Callsign/LoTW markers and signal strength are retained, with decoded messages available on hover. Row status distinguishes an opportunity from the actual target. Band changes clear these live-band tables; Session History retains the session evidence.

## Listening and skipped rounds

- Listening defaults to one minute per band, adjustable from one to five.
- Start on a full receive slot; actual visit time includes synchronisation and movement confirmation.
- Silent bands skip four subsequent rounds by default.
- Bands with at most five stations, all at known distances within 1,500 miles, skip two subsequent rounds by default. Both skip settings are adjustable.
- One distant station keeps a band in rotation. Unknown distances are not assumed local.
- A round means one pass through permitted bands, not a fixed number of minutes.
- If all bands are resting, the earliest-due band still gets a real visit rather than spinning empty rounds.
- Interrupted receive samples are not scored as silence. Normal gaps between status packets do not continually restart the sample.

## Safety and first test

Use **Stop All** to end Scavenger. Band Analysis, PSK-map band movement and manual CALL NOW cannot take control concurrently. Unsolicited incoming calls do not override its selected target. Movement requires receive-only status and fresh JTDX confirmation; repeated movement failures stop the search for inspection.

Start with already-tested bands and supervise the first rotation and QSO. Check that an unconfirmed DXCC is now eligible, your optional filters select the intended needs, no CQ probes occur, and final-73/logging protection remains intact. Automated and visual checks cannot replace an on-air test.
