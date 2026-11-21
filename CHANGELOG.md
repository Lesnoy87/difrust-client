# Changelog

## 1.5.1

- Host lock now requires a live heartbeat (`dr_seq`); if the host stops sending
  settings or runs without the mod, clients unlock after 6 seconds.
- Client settings are snapshotted under lock and restored afterwards; host
  values are never saved into the client's `DifRUST.cfg`.
- Harmony patches are applied per class with error logging, so one failed patch
  does not block settings or the menu.
- Dawn timer no longer shows 00:00 during dusk (20:00-22:00).
- Biome ambient tint works independently of the cold tint slider.
- Moon multiplier is smoothed when the LunarEvents phase changes.
- `Save()` is debounced (once per second, flushed on menu close).
- NVG relief is host-synced as `dr_nvg` and has its own menu toggle.
- Roof detection is cached (5 Hz); the menu window is clamped to the screen.
- Network send remains `SetCustomVar(..., false, ...)` + `SetCustomVarNetwork`
  by design: local update plus explicit network broadcast.

## 1.5.0

- Weather and clouds now affect night lighting: clear nights are brighter,
  overcast/rain/snow/storms darker (never fully black). Weather is read only.
- Optional LunarEvents link: the moon phase is taken from `lunar_*` night buffs
  when present; otherwise `day % 8` is used as before.
- Smooth transitions for presets, toggles and settings (about 1 s).
- Eye adaptation: 2.4 s to get used to darkness, 1.1 s back to light.
- NVG raises the darkness floor to 0.35; a held flashlight or nearby
  campfire/torch/lamp to 0.14.
- Biome night tint using the same palette as Silent Fog (snow, desert,
  radiated, wasteland, burnt forest); neutral biomes are untouched.
- Dawn timer in the status line (game time until 06:00).
- Host sync covers all settings; new Host lock option (analog of `sf_force`).
  Without the lock each player keeps their own settings.
- Menu conflict warning when another night-darkening mod is loaded.
- New config keys: `weather`, `eyes`, `light`, `nvg`, `biome`, `biome_amount`,
  `lunar`, `force`; new CVars: `dr_force`, `dr_weather`, `dr_eyes`, `dr_light`,
  `dr_biome`, `dr_biome_amt`, `dr_lunar`.

## 1.4.2

- License changed from MIT to All Rights Reserved / source-available
  (`LICENSE`, `LICENSE-RU.md`). Versions 1.4.1 and earlier remain MIT.
- No gameplay or lighting changes.

## 1.4.1

- Cold night tint fixed: it now applies to the moonlight itself instead of
  multiplying ambient colours that are already almost zero at 6% darkness.
- Moon phases are clearly visible: brightness range x0.25..x2.5; the status
  line shows the current multiplier (e.g. "moon waxing x1.75").
- Night brightness capped at 0.85 so a full moon does not turn into day.

## 1.4.0

- Moon phases: full moon is brighter, new moon is nearly black. The
  "Moon phases" slider controls contrast (0-100%) and syncs as `dr_moon`;
  saved as `moon` in `DifRUST.cfg`.
- Three one-click presets: Soft (30% darkness), Standard (6%), Rust (2%).
- The status line shows the current phase: full / new / waning / waxing.

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
