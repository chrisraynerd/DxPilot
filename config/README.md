# Configuration examples

`app_settings.chris-baseline.example.json` is a sanitized copy of the stable
52-row personal testing baseline. The QRZ protected-password field is blank.

It is safe to retain as a reference/default example, but it is not portable:

- ADIF, `ALL.TXT` and rarity-file paths refer to Chris's computer.
- Pixel coordinates and JTDX grid calibration match a 1936×1048 JTDX window.
- Callsigns and lookup test calls are personal examples.

Other users must import or copy it only after adjusting those values. Never put
a real QRZ password or `QrzPasswordProtected` value in this directory.

Files ending in `.local.json` or containing `private` in their filename are
ignored by Git.
