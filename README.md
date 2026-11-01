# Zam's Collectibles

A mod for [Probably Stolen](https://store.steampowered.com/app/4348910/) that brings Zamco Electronics' Aug Warriors trading card game to the station: open packs, chase rares, grade your best pulls, seal complete sets, and play the game at the back table of a card shop that only opens after hours.

**Get it on the Steam Workshop:** [Zam's Collectibles](https://steamcommunity.com/sharedfiles/filedetails/?id=3829948361)

## What's in it

- **Ten card sets in three tiers**, over 120 cards, each set with its own packs, booster boxes and art. Higher tiers are licensed from Zamco through Wild's Network.
- **Hidden card condition and grading**: judge a card by its wear, or send it to the Card Grader to be sealed in a graded slab.
- **Collector's Boxes** to seal complete sets (with the ultra rare, a Master Set), binders, deck boxes and card sleeves. The card containers fit in your backpack.
- **The Downside Den**, a card shop in the lower section that opens after hours, with packs, singles and a back table.
- **Aug Warriors**, a full card game against the Den's regulars, each with a hidden play style, and against customers who challenge you at your counter.
- **Multiplayer(-ish) deck codes**: copy your deck as a short code, send it to a friend, and they play against it at the Den.
- **New customers**: pack flippers, deck builders, the Card Grader and the Set Collector. Collectibles in your display case draw card buyers, the way the game's own goods do.
- **A new start**, Card Dealer, with the Den already open.

Text is in English and Simplified Chinese.

## Building

```
dotnet build -c Release
```

The build installs the mod into `<game>\Mods\ZamsCollectibles\` (DLL, `manifest.xml`, `preview.png`, `Textures\`, `Localization\`, `Data\`). The game path is `GameDir` in `ZamsCollectibles.csproj`. The game loads mods at startup, so restart it after a build, and close it first: a running game locks the DLL and the copy fails.

The F8 console and F9 dev menu need an empty `dev.txt` next to `Probably Stolen.exe`. The mod's debug tools are under F9 > Special > **Zam's Collectibles**. With `dev.txt` the game runs mods in developer mode, which also loads a mod its scanner would refuse for everyone else (see Publishing).

Modding reference: [probably-stolen-modding](https://github.com/dragonpan2/probably-stolen-modding).

## Content

- **Cards and sets**: `Data/sets.json` holds the rarities, sets, pack contents and cards. Adding cards needs no code change.
- **Card art**: 63 x 46 pixel art in `Textures/Items/`, named `art_<set>_<nn>.png` (e.g. `art_rustwave_04.png`), or any name set in the card's `"art"` field. Cards without art show a placeholder. `tools/card-art.cmd` imports art from any image: it crops, reduces it to pixel art and saves it under the right name.
- **Workshop images**: `tools/make-workshop-images.py` draws `preview.png` and the gallery images in `workshop/` from the mod's own sprites. It needs Python with Pillow (`pip install pillow`).

### Contributing card art

Five sets still use placeholder art. Art should be:

- 63 x 46 pixels, PNG, pixel art drawn 1:1 (no upscaling). The game shows it at 3x with no smoothing.
- Art only: the game draws the frame, name, cost, stats and text.
- Transparency is fine; it shows the card's dark background (`#1A1620`).
- Keep the subject in the middle 52 x 44 pixels: the small cards on the game board show only that area, at half size.
- Most existing art uses about 16 to 24 colours. Larger or painted art at a 63:46 landscape ratio also works; it gets converted.

Name the set and card number when you send art (e.g. Neural Tap #04). The card list is in `Data/sets.json`.

## Translations

Player-facing text is in `Localization/<locale>.csv`; `en.csv` lists every key, and `zh.csv` is a machine-assisted draft that would benefit from a native speaker's review. Card content is written in English in `sets.json`. A locale file can translate it with these keys, falling back to English:

| Key | Translates |
|---|---|
| `set_<set>_name` | set name (`set_rustwave_name`) |
| `rarity_<rarity>` | rarity name (`rarity_ultra`) |
| `card_<set>_<nn>_name`, `_text`, `_flavor` | a card's name, rules and flavour (`card_rustwave_04_name`) |

To add a language, copy `en.csv` to the game's locale code (for example `fr.csv`) and translate the text after the first comma on each line.

## Publishing to the Steam Workshop

The item is [3829948361](https://steamcommunity.com/sharedfiles/filedetails/?id=3829948361), described by `tools/workshop.vdf`.

1. Bump `<ModVersion>` in `manifest.xml`, and update `changenote` (and the description if needed) in `tools/workshop.vdf`. Never use a straight double quote inside those values: it ends the value and Steam cuts the text off there.
2. Build with the game closed, then launch it and check `Player.log` (`%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\`). The line `[ModLoader] audit: zams-collectibles ... caps=none` must say `caps=none`: the mod declares no capabilities, so anything else gets it refused for players. Developer mode hides this, so a working local test isn't enough.
3. Run `tools/publish-workshop.ps1` in its own PowerShell window. It asks for the Steam login in a sign-in box, then steamcmd (in `C:\steamcmd`) asks for the Steam Guard code. It uploads the installed mod folder and `preview.png`.
4. Gallery images are added by hand on the Workshop page; steamcmd only uploads the preview.

A local copy in `Mods\` overrides a subscribed Workshop copy, so development can continue alongside the published version.

## License

The code is under the [MIT License](LICENSE): reuse it freely in your own mods. The art and game content (card art, sprites, card text and the guide) are all rights reserved: please don't re-upload the mod or its art. See [LICENSE](LICENSE) for exactly which files are which.
