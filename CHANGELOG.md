# Changelog

## 1.7.0 — second moon + rebuilt stars

- **The custom moon no longer replaces the vanilla moon.** It is now a
  separate, smaller companion sprite (58 % size) on its own orbit: about
  29° higher and 20° to the side, same 45 000 distance, with its own material;
  the vanilla `_ColorMap` is never touched.
- **Every night, brighter on events.** The second moon is visible every night
  when the toggle is on. On an ordinary night it is calm and slightly cooler;
  on a special LunarEvents night it burns brighter and takes that event's tint
  (harvest gold, wild amber, fatal red, …). The event is read through the new
  public `LunarEventsApi.SpecialForNight(seed, day)`; without LunarEvents the
  moon simply stays calm. No hard dependency.
- On by default (`sky_moon=1`), client-side only, not synced with the host.
  The menu shows which lunar event is tonight, or that the night is ordinary.
- The companion is pinned to a **full phase**: the vanilla shader shades the
  disc from `_LightDir`/`_Color`, and the copy does not receive the game's
  updates, so those are fixed to full and tinted — no black new-moon disc.
- **Star field rebuilt.** 223 000 stars on 2048² faces instead of 42 000 on
  1024², realistic magnitude distribution, blue-white to orange colours,
  density concentrated toward the galactic plane, and dust lanes that thin
  out the stars. Drawn with a tight PSF at 2048, so a scope shows pinpoints,
  not fuzzy blobs.
- **Layered bake, runtime layers disabled.** The deep-space base, the Milky
  Way glow (`glow_*` content) and the stars (`star_*` content) are generated
  separately and baked into the 2048² `mw_*.png` cubemap. A runtime
  three-submesh pass on the atmosphere sphere was built and tested, but the
  game's sky compositor (`ZTest Always` + separate sky pass) sends the extra
  passes below the horizon; it is disabled in 1.7.0. The baked sky is the
  stable result.
- **Layered sky bake.** A deep blue-violet background, a broad warped band,
  a warm core, granular star clouds, sharp dust rifts and faint nebulae are
  kept as separate content; the sky no longer reads as a flat blue ring.
- **Sharper moon:** the second-moon texture is now 1024² (the original AI
  plate) instead of 512².

## 1.6.2 — true spherical sky (no stretching)

- The AI sky plate is a normal square photo, not a 360×180 panorama, so
  mapping it over the sphere caused the smeared band, skewed seams and the
  low-res look. It was the wrong source, not the shader.
- The sky is now generated as a real sphere: 3D value noise (seamless, no
  pole pinching), a tilted galactic band, warm core, cool arms, brown dust
  lanes and 42 000 procedural stars.
- Same loader, settings and the dense sky mesh from 1.6.1.

## 1.6.1 — dense sky mesh

- The vanilla atmosphere sphere is low-poly, so the cubemap was stretched
  across huge triangles: when zoomed (e.g. through a scope) the sky showed
  hard diagonal facets and a strong skew. The mod now swaps that mesh for a
  128×64 UV sphere (double-sided) and restores the original when disabled.
- New hidden cfg key `sky_mesh` (default 1) turns the mesh swap off without
  rebuilding.
- The sky dump now logs atmosphere/clouds mesh vertex and triangle counts.

## 1.6.0 — night sky: Milky Way + LunarEvents moon

- The sky cubemap (`_OuterSpaceCube`) is replaced with a custom Milky Way:
  six 1024² faces loaded at runtime from `Assets\Sky\mw_*.png`, tilted
  galactic band, warm core, dust lanes and procedural stars.
- `_StarStrength` follows the new **Sky brightness** slider (30–150 %);
  the daylight gate stays vanilla — the cubemap is invisible at noon even
  at full strength, so no extra clock logic is needed.
- The moon (`_ColorMap`) is replaced with a 512² disc in the LunarEvents
  medallion palette (warm gold surface, navy maria, soft limb). It is a
  **soft dependency**: the custom moon only appears when the LunarEvents
  mod is loaded; without it the vanilla moon stays.
- New client settings in `DifRUST.cfg` and the menu: `sky`, `sky_amt`,
  `sky_moon`. They are cosmetic and are **not** synced with the host and
  not affected by the host lock.
- Everything is reversible: disabling the feature restores the vanilla
  cubemap, its strength and the vanilla moon texture.
- No render pass, no AssetBundle, no changes to fog; Silent Fog keeps
  painting over the sky in `OnRenderImage` as before.
- `Assets\Sky` is optional: if the files are missing or broken, the mod
  logs one line and keeps the vanilla sky.

## 1.5.7 — night sky probe (temporary)

- Added a probe for the planned 1.6.0 night sky. Nothing changes until a key
  is pressed; all changes are restored with NumPad0.
- The probe answers the questions that cannot be settled from logs: whether
  the sky cubemap is the actual night sky image, how `_StarStrength` scales
  it, which cubemap face orientation is correct, whether the cubemap shows
  up during the day, and how the moon sprite blends alpha.
- Keys: **NumPad1** test cubemap in `_OuterSpaceCube` (vanilla texture is
  remembered), **NumPad2 / NumPad3** `_StarStrength` ×2 / ÷2,
  **NumPad4** test moon in `_ColorMap`, **NumPad5** cycle 4 flip
  combinations, **NumPad8** switch cube pattern, **NumPad0** restore,
  **NumPad9** dump.
- The dump now also logs color space, graphics API/GPU, the vanilla cubemap
  format (size, mips, compression, readability), sky sphere angles and the
  component list on the main camera.
- Test patterns are procedural: the cube maps direction to RGB (smooth
  colours reveal face seams) or paints each face a distinct colour with
  white/black edge stripes; the moon has alpha quadrants, rings and an "F".

## 1.5.6 — diagnostics, second pass (no visual change)

- Log now dumps the sky material (`SkyManager.atmosphereMtrl`) and the clouds
  material in full: shader, render queue, keywords, every exposed property
  (textures, colors, vectors, floats). The first pass showed that
  `GetMaterialAndTransform("SkySystem(Clone)")` throws on the root object, so
  the root is taken from `SkyManager.parent` instead.
- Lists every camera in the scene (`Camera.allCameras`): name, tag, depth,
  near/far, FOV, clear flags, culling mask with layer names, target texture,
  viewport, depth texture mode.
- Adds the moon sprite mesh (name, vertices, bounds) and its layer, plus
  `starIntensity`, `SkyColor`, `fogColor`, `dayPercent`, `worldRotation`,
  `cloudTransition`, cloud texture names/sizes and `SkyManager.mainCamera`.
- Still read-only; no lighting, fog, menu or settings changed.

## 1.5.5 — diagnostics only (no visual change)

- Added a one-off sky diagnostic for the planned 1.6.0 night sky. It only
  reads and logs: `SkyManager.moonSpriteMat` and the `SkySystem(Clone)`
  material (shader, properties, textures), `MoonSprite` / `CloudsSphere` /
  `AtmosphereSphere` transforms and renderers, `GetMoonDirection()` /
  `GetSunDirection()`, moon brightness/phases, the main camera
  (`farClipPlane`, `depthTextureMode`, rendering path) and `RenderSettings`.
- Writes `DifRUST-sky.log` to the game's persistent data folder and one line
  per dump to the game log. Dumps on world entry, at 00:00 and 12:00 in-game,
  and manually with **NumPad9**.
- Nothing in lighting, fog, menu or settings changed.

## 1.5.4

- "Brighter indoors" no longer triggers outdoors. Roof detection used to look
  only up, so a balcony, an overhang or a tree canopy counted as a room and
  the night outside was boosted to ~50 %. Now a room needs a roof **and** at
  least two of four sides blocked by solid blocks (plants ignored):
  2 sides = 50 %, 3 = 80 %, 4 = 100 %.

## 1.5.3

- Eye adaptation is exponential instead of linear. With the ambient floor in
  place the linear ramp burned most of the visible difference in the first
  ~0.4 s, so stepping out of a lit base read as an instant switch. Now the
  eye keeps settling (2.4 s into darkness, 1.1 s toward light).

## 1.5.2

- Ambient darkening now has an absolute floor per channel: each channel is
  kept at no less than 25% of the brightest channel, scaled by the same
  darkness factor. A config that zeroes a channel (ProjectZ used
  `ambientGroundScale = .2,0`) can no longer make the slider invisible there.
- Removed the dependency on ProjectZ lighting: ProjectZ's `worldglobal.xml`
  is disabled, so DifRUST works on vanilla ambient scales.
- Menu shows the raw vanilla ambient channels
  (`Ambient: sky .70 · equator .45 · ground .05`) to verify what a mod or
  config actually feeds the renderer.

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
