# DifRUST — Rust Nights 1.4.1

Client-side lighting mod for 7 Days to Die 3.4.x. Only lighting is changed: fog,
weather, zombies, loot, progression and network logic are untouched.

## Schedule

| Time | Lighting |
|---|---|
| 20:00–22:00 | gradual darkening |
| 22:00–04:00 | full darkness |
| 04:00–06:00 | gradual dawn |
| 06:00–20:00 | normal lighting |

## Menu

Press **NumPad6** or **F8** in game:

- enable / disable the mod;
- darkness strength slider, 2–60% (default 6%);
- "Night now (30 s)" — instant preview;
- reset to defaults.

Settings are saved to `DifRUST.cfg` and restored on the next launch. The menu follows the game language: English by default, Russian when the game runs in Russian.

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
- No dependency on RustNightMod or RustNightClient.
- If another client mod also darkens nights, effects stack — use one at a time.
- Pairs with Silent Fog: that mod softens night fog when DifRUST is installed.

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

# DifRUST — Rust Nights 1.4.1 (RU)

Клиентский мод освещения для 7 Days to Die 3.4.x. Меняется только свет: туман,
погода, зомби, лут, прокачка и сеть не затронуты.

## Расписание

| Время | Освещение |
|---|---|
| 20:00–22:00 | плавное затемнение |
| 22:00–04:00 | полная тьма |
| 04:00–06:00 | плавный рассвет |
| 06:00–20:00 | обычный свет |

## Меню

**NumPad6** или **F8** в игре:

- включить / выключить мод;
- слайдер «Сила тьмы», 2–60% (по умолчанию 6%);
- «Ночь сейчас (30 с)» — быстрый предпросмотр;
- сброс к значениям по умолчанию.

Настройки сохраняются в `DifRUST.cfg` и восстанавливаются при следующем запуске. Меню подстраивается под язык игры: английский по умолчанию, русский при русском языке игры.

## Установка

1. Установите `0_TFP_Harmony`, если его нет.
2. Скопируйте папку `DifRUST` в:

   ```text
   %APPDATA%\7DaysToDie\Mods\DifRUST
   ```

   либо в `<7 Days To Die>\Mods\DifRUST`.
3. Запускайте игру без EAC (DLL/Harmony-мод). Файлы EasyAntiCheat не изменяются.

## Что нового в 1.4.1

- **Холодный оттенок починен.** Раньше он умножал ambient-цвета, которые при 6%
  тьмы уже почти нулевые — разницы не было видно. Теперь синева идёт на сам
  лунный свет плюс маленькая холодная подсветка абсолютной величиной.
- **Фазы луны стали заметными.** Размах ×0.25…×2.5 (было почти незаметно при
  низкой тьме); в строке состояния видно множитель: «луна растёт ×1.75».
- Потолок ночной яркости 0.85, чтобы полнолуние не превращалось в день.

## Что нового в 1.4.0

- **Фазы луны.** Полнолуние светлее, новолуние почти чёрное. Ползунок «Фазы луны»:
  0% — все ночи одинаковые, 100% — максимальный контраст. Считается по номеру
  игрового дня, настройка синхронизируется как `dr_moon`, в `DifRUST.cfg` — `moon`.
- **Три пресета** в один клик: «Мягкая» (30% тьмы), «Стандарт» (6%), «Rust» (2%).
  Пресет меняет силу тьмы, яркость в помещениях и холодный оттенок.
- В строке состояния меню видна текущая фаза: «луна полная / новая / убывает / растёт».

## Совместимость

- Работает на любом сервере, включая ванильный: других игроков мод не затрагивает.
- Не требует RustNightMod или RustNightClient.
- Если другой клиентский мод тоже затемняет ночь, эффекты складываются — используйте один.
- Хорошо сочетается с Silent Fog: тот ослабляет ночной туман, если DifRUST установлен.
