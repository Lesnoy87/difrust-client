# DifRUST — Rust Nights 1.5.4

Client-side lighting mod for 7 Days to Die 3.4.x. Only lighting is changed: fog,
weather, zombies, loot, progression and network logic are untouched.

## Schedule

| Time | Lighting |
|---|---|
| 20:00–22:00 | gradual darkening |
| 22:00–04:00 | full darkness |
| 04:00–06:00 | gradual dawn |
| 06:00–20:00 | normal lighting |

## What's new in 1.5.4

- **"Brighter indoors" no longer triggers outdoors.** A room now needs a roof
  *and* at least two of four sides blocked by solid blocks (plants ignored):
  2 sides = 50 %, 3 = 80 %, 4 = 100 %. Balconies, overhangs and tree canopies
  no longer brighten the night outside.
- **Ambient floor (1.5.2).** Each ambient channel is kept at no less than 25 %
  of the brightest one, so a config that zeroes a channel (ProjectZ used
  `ambientGroundScale = .2,0`) cannot make the slider invisible there.
  DifRUST no longer depends on ProjectZ lighting and works on vanilla scales.
- **Raw ambient channels in the menu:** `Ambient: sky .70 · equator .45 ·
  ground .05` — shows what the renderer actually receives.
- **Eye adaptation is exponential (1.5.3):** 2.4 s into darkness, 1.1 s toward
  light, so stepping out of a lit base no longer reads as an instant switch.

## What's new in 1.5.1

- Host lock now needs a fresh heartbeat (`dr_seq`). If the host stops sending
  settings or plays without the mod, clients unlock after 6 seconds instead of
  staying locked forever.
- Client settings are snapshotted while locked and restored afterwards, so the
  host's values are no longer written into the client's `DifRUST.cfg`.
- Harmony patches are applied per class with error logging; one failed patch no
  longer prevents the menu and settings from loading.
- Dawn timer is hidden during dusk instead of showing 00:00.
- Biome ambient tint works even with the cold tint slider at 0.
- Moon multiplier is smoothed when the LunarEvents phase changes.
- `Save()` is debounced (once per second, flushed on menu close), no disk writes
  on every slider frame.
- NVG relief is now host-synced (`dr_nvg`) and has its own menu toggle.
- Roof detection is cached (5 times per second) and window position is clamped
  to the screen.

## What's new in 1.5.0

- **Weather and clouds.** A clear night lets the moon through; overcast, rain,
  snow and storms make it darker, but never pitch-black. Weather is only read,
  never changed. Transitions are smooth over several seconds.
- **LunarEvents link (optional).** If a `lunar_*` night buff is present, DifRUST
  takes the moon phase from it instead of `day % 8`. Works with or without
  LunarEvents installed.
- **Smooth transitions.** Presets and toggles blend over about a second instead
  of snapping.
- **Dawn timer.** The status line shows the remaining game time until 06:00.
- **Eye adaptation.** Walking from a lit interior into the night takes 2–3
  seconds to get used to; returning to the light is faster. Can be disabled.
- **NVG, flashlight and fire.** Night vision raises the darkness floor to about
  35%, a held flashlight or a nearby campfire/torch to about 14%, so rescue
  light actually works instead of staying blind.
- **Biome night tint.** Snow, desert, radiated, wasteland and burnt forest get a
  matching night hue. The palette is the same as Silent Fog's, so light and fog
  agree instead of fighting. Forest, plains and city stay neutral.
- **Host sync and server lock.** The host shares the preset and all settings.
  With **Host lock** enabled, clients cannot override them; without it, every
  player tunes the night to their own monitor.
- **Conflict warning.** If another night-darkening mod is loaded, the menu shows
  a warning that effects stack.

## Menu

Press **NumPad6** or **F8** in game:

- enable / disable the mod;
- presets: **Soft**, **Standard**, **Rust**;
- effect toggles: weather, eye adaptation, flashlight/fire, biome tint,
  LunarEvents, NVG;
- **Host lock** (host only): same night for all players;
- darkness strength slider, 2–60% (default 6%);
- brighter indoors, cold night tint, moon phases;
- "Night now (30 s)" — instant preview;
- reset to defaults.

Settings are saved to `DifRUST.cfg` and restored on the next launch. The menu
follows the game language: English by default, Russian when the game runs in
Russian.

Config keys: `enabled`, `night`, `indoor`, `tint`, `moon`, `weather`, `eyes`,
`light`, `nvg`, `biome`, `biome_amount`, `lunar`, `force`.

## Install

1. Install `0_TFP_Harmony` if it is missing.
2. Copy the `DifRUST` folder into:

   ```text
   %APPDATA%\7DaysToDie\Mods\DifRUST
   ```

   or into `<7 Days To Die>\Mods\DifRUST`.
3. Launch the game without EAC (DLL/Harmony mod). No EasyAntiCheat files are modified.

Expected structure:

```text
Mods\DifRUST\ModInfo.xml
Mods\DifRUST\DifRUST.dll
Mods\DifRUST\README.md
Mods\DifRUST\Source\...
```

## Compatibility

- Works on any server, including vanilla servers: other players are not affected.
- No dependency on RustNightMod, RustNightClient or LunarEvents.
- If another client mod also darkens nights, effects stack — use one at a time;
  the menu warns about this.
- Pairs with Silent Fog: the biome palette is the same, so night fog and night
  lighting match by hue.

## Source

Included in this archive (`Source/`). Public repository:
https://github.com/Lesnoy87/difrust-client

```powershell
cd Source
.\build.ps1
```

## AI disclosure

Code and documentation were created with AI assistance. No AI-generated visual
assets, models, voices or textures are included.

---

# DifRUST — Rust Nights 1.5.4 (RU)

Клиентский мод освещения для 7 Days to Die 3.4.x. Меняется только свет: туман,
погода, зомби, лут, прокачка и сеть не затронуты.

## Расписание

| Время | Освещение |
|---|---|
| 20:00–22:00 | плавное затемнение |
| 22:00–04:00 | полная тьма |
| 04:00–06:00 | плавный рассвет |
| 06:00–20:00 | обычный свет |

## Что нового в 1.5.4

- **«Светлее в помещении» больше не срабатывает на улице.** Комнатой считается
  место с крышей **и** минимум двумя закрытыми из четырёх сторон блоками
  (растения не считаются): 2 стороны = 50 %, 3 = 80 %, 4 = 100 %. Балконы,
  навесы и кроны деревьев больше не подсвечивают ночь снаружи.
- **Пол по ambient (1.5.2).** Каждый канал не опускается ниже 25 % от самого
  яркого, поэтому конфиг, обнуляющий канал (ProjectZ: `ambientGroundScale = .2,0`),
  больше не делает ползунок невидимым. ProjectZ-освещение больше не нужно,
  DifRUST работает на ванильных шкалах.
- **В меню видны сырые каналы:** `Ambient: небо .70 · экватор .45 · земля .05`.
- **Адаптация глаз экспоненциальная (1.5.3):** 2.4 с в темноту, 1.1 с к свету —
  выход из освещённой базы больше не выглядит мгновенным переключением.

## Что нового в 1.5.1

- Замок хоста теперь требует свежий пульс (`dr_seq`). Если хост перестал
  слать настройки или играет без мода, через 6 секунд замок снимается.
- Настройки клиента запоминаются на время замка и возвращаются после него,
  значения хоста больше не попадают в `DifRUST.cfg`.
- Патчи Harmony применяются по классам с логом ошибок; один сломанный патч
  больше не мешает меню и настройкам.
- Таймер рассвета скрыт на закате вместо «00:00».
- Биомный ambient работает даже при «Холодный оттенок = 0».
- Множитель луны сглаживается при смене фазы LunarEvents.
- `Save()` пишет cfg не чаще раза в секунду и сбрасывается при закрытии меню.
- ПНВ синхронизируется (`dr_nvg`) и имеет свой тумблер.
- Проверка крыши кэшируется (5 раз в секунду), окно не уезжает за край.

## Что нового в 1.5.0

- **Погода и облачность.** В ясную ночь луна светит сильнее; облака, дождь,
  снег и гроза делают ночь темнее, но не в чёрный экран. Погода только
  читается, мод её не меняет. Переходы плавные, за несколько секунд.
- **Связка с LunarEvents (необязательная).** Если на игроке висит ночной бафф
  `lunar_*`, фаза луны берётся оттуда, а не из `day % 8`. Работает и без
  LunarEvents.
- **Плавные переходы.** Пресеты и тумблеры меняются примерно за секунду, без
  щелчка.
- **Таймер до рассвета.** В строке состояния видно, сколько игрового времени
  осталось до 06:00.
- **Адаптация глаз.** Выход из освещённого дома в ночь занимает 2–3 секунды,
  возвращение к свету — быстрее. Можно отключить.
- **ПНВ, фонарь и огонь.** ПНВ поднимает пол тьмы примерно до 35%, фонарь в
  руке или костёр/факел рядом — примерно до 14%, чтобы спасательный свет
  реально работал, а не оставался слепым.
- **Биомный оттенок ночи.** Снег, пустыня, радиация, пустошь и горелый лес
  получают свой оттенок. Палитра та же, что у Silent Fog, поэтому свет и туман
  совпадают, а не спорят. Лес, равнины и город остаются нейтральными.
- **Синхронизация от хоста и серверный замок.** Хост раздаёт пресет и все
  настройки. С включённым **замком хоста** клиенты не могут их перебить; без
  замка каждый крутит ночь под свой монитор.
- **Предупреждение о конфликте.** Если загружен другой мод, который тоже
  затемняет ночь, меню предупреждает, что эффекты складываются.

## Меню

**NumPad6** или **F8** в игре:

- включить / выключить мод;
- пресеты: **Мягкая**, **Стандарт**, **Rust**;
- тумблеры эффектов: погода, адаптация глаз, фонарь/огонь, биомы, LunarEvents,
  ПНВ;
- **Замок хоста** (только у хоста): одна ночь для всех игроков;
- слайдер «Сила тьмы», 2–60% (по умолчанию 6%);
- ярче в помещениях, холодный оттенок, фазы луны;
- «Ночь сейчас (30 с)» — быстрый предпросмотр;
- сброс к значениям по умолчанию.

Настройки сохраняются в `DifRUST.cfg` и восстанавливаются при следующем запуске.
Меню подстраивается под язык игры: английский по умолчанию, русский при русском
языке игры.

Ключи конфига: `enabled`, `night`, `indoor`, `tint`, `moon`, `weather`, `eyes`,
`light`, `nvg`, `biome`, `biome_amount`, `lunar`, `force`.

## Установка

1. Установите `0_TFP_Harmony`, если его нет.
2. Скопируйте папку `DifRUST` в:

   ```text
   %APPDATA%\7DaysToDie\Mods\DifRUST
   ```

   либо в `<7 Days To Die>\Mods\DifRUST`.
3. Запускайте игру без EAC (DLL/Harmony-мод). Файлы EasyAntiCheat не изменяются.

## Совместимость

- Работает на любом сервере, включая ванильный: других игроков мод не затрагивает.
- Не требует RustNightMod, RustNightClient или LunarEvents.
- Если другой клиентский мод тоже затемняет ночь, эффекты складываются —
  используйте один; меню об этом предупреждает.
- Хорошо сочетается с Silent Fog: палитра биомов та же, поэтому ночной туман и
  свет совпадают по оттенку.

## Что было в 1.4.2

- Лицензия MIT → **All Rights Reserved / source-available**; 1.4.1 и раньше —
  MIT. Код и свет не менялись.

## Что было в 1.4.1

- **Холодный оттенок починен.** Раньше он умножал ambient-цвета, которые при 6%
  тьмы уже почти нулевые — разницы не было видно. Теперь синева идёт на сам
  лунный свет плюс маленькая холодная подсветка абсолютной величиной.
- **Фазы луны стали заметными.** Размах ×0.25…×2.5; в строке состояния видно
  множитель: «луна растёт ×1.75».
- Потолок ночной яркости 0.85, чтобы полнолуние не превращалось в день.

## Лицензия

С версии **1.4.2** DifRUST распространяется как **All Rights Reserved /
source-available**, не MIT. Бесплатно играть и включать немодифицированный
мод в бесплатные модпаки с указанием автора «Lesnoy87 / DifRUST» можно;
продажа, перезалив и изменение — нельзя. Полный текст: `LICENSE`, кратко
по-русски: `LICENSE-RU.md`. Версии **1.4.1 и раньше** остаются под MIT.
