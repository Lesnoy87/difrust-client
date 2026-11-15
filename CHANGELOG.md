# Changelog

## 1.3.0

- Brighter indoors: under a roof the night light is mixed back up, so a house is
  readable while the street stays dark. Own shelter detection (five points,
  blocks 2-8 m above the feet; open sky above the head means outdoors),
  trader safe zone counts as indoors.
- Cold night tint: the night is bluish instead of grey, strength follows how deep
  into the night it is. Slider 0-100%.
- Host settings are shared with all players through vanilla `NetPackageModifyCVar`
  (`dr_on`, `dr_night`, `dr_indoor`, `dr_tint`). Works on a server without the mod too.
- Demo night no longer looks grey and no longer lights rooms like daylight:
  the tint is applied during the demo and indoor brightness is capped.
- New menu rows: "Brighter indoors" and "Cold night tint".

## 1.2.0

- The settings menu now follows the game language: English by default, Russian
  when the game runs in Russian. Language is detected at runtime, no restart needed.
- Wider menu window so the English labels fit.
- Button row re-spaced for both languages.

## 1.1.0

- In-game settings menu (NumPad6 / F8): on/off, darkness slider, night preview, reset.
- Darkness strength adjustable at runtime, 2–60% (default 6%).
- "Night now (30 s)" preview button.
- Settings saved to `DifRUST.cfg` and restored on launch.
- Removed debug logging.

## 1.0.1

- Fixed double night darkening (moon ambient and ambient colours were both applied).

## 1.0.0

- Initial release: time-based dusk, full darkness and dawn.
