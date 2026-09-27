# Achievements band matrices — preview

## Opening this copy

Close your previous DX Pilot instance, then open `DXPilot-for-JTDX-G1CEC.exe` in this preview folder. Existing saved settings are used; you do not need to copy settings into this folder. This build retains the preceding Scavenger improvements and does not change target selection, transmission or band movement logic.

## DXCC countries and USA states

Open **Achievements**, then choose **DXCC countries** or **USA states**. The selected global Achievement Profile applies to both tables. A confirmation made under an old callsign appears only when that identity, or All callsigns, is selected.

The summary cards follow the selected table. The Profile QSOs card always shows the full selected profile's QSO count, not just USA QSOs.

The narrow columns cover 160, 80, 60, 40, 30, 20, 17, 15, 12, 10 and 6 metres:

- **Green:** the number of LoTW-confirmed QSOs on that band.
- **Amber:** the number of worked QSOs on that band when none are LoTW confirmed.
- **White and blank:** no matching worked QSO on that band.

For example, three QSOs with two LoTW confirmations show **2 in green**. Hover over the cell to see all three worked, two confirmed and one awaiting confirmation. Paper QSL and eQSL confirmations do not turn these cells green.

Row totals include every band and mode in the imported log, including bands outside these eleven columns. QSOs means all worked contacts; Awaiting means those without LoTW confirmation; LoTW means those confirmed through LoTW.

Search and status filters apply to the tables. Double-click a country or state row for its complete QSO history in the selected profile. Country QSO counts can also be clicked. Horizontal scrolling reveals additional columns while the country/state identity remains visible.

USA States includes all 50 states, including Alaska and Hawaii. It uses valid US ADIF state codes, not guesses from callsigns. US records lacking a valid 50-state code are excluded from state totals and their count is shown above the table. DC is not included as a state. Foreign records sharing an abbreviation are not credited to a US state.

## Importing an updated log

Use **Import ADIF…** at the top right of Achievements. Choose your updated **full/master ADIF export**. This is the same operation as Settings → Load / Choose Full ADIF: it selects and saves the full-log source, reloads the merged logbook and refreshes Achievements. It is a local file import, not an upload to LoTW or another website. Cancel leaves the existing source unchanged.

Use **Refresh** to recalculate the display from the currently loaded logbook and history. No extra polling or continuous band analysis has been introduced.
