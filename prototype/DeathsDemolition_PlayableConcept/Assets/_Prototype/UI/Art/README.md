# UI art (WP-022 · AP-012) — theme, atlases, plates

The imported UI art lives inside the UI module (`Assets/_Prototype/UI/Art/`), not under
`Assets/_Art/`: the environment/character import contract there (`ArtImportContract`,
`T_<role>_<Map>` naming, streaming mipmaps, class sizes, its gate tests) is for world
textures and would reject sprite atlases. UI textures have their own contract, ADR-022-1.

## Files (`Resources/UI/`)

| path | what | import |
|---|---|---|
| `UiTheme.asset` | the one `UiTheme` ScriptableObject: sprite lists by manifest name, plate, marker, skin previews, default UI scale | built by **DD → UI → Build UI Theme From AP-012 Atlases** (`UiThemeBuilder.Build`) |
| `AP012/AP012_icons_1024x768.png` | 32 white-glyph icons × 128² (`Upgrade.*`, `Weapon.*`, `Special.*`, `Hud.*`, `Menu.*`) | Sprite (Multiple), sliced from the manifest |
| `AP012/AP012_frames_1024.png` | 9-slice frames, medallions, divider, reticles, cursor, focus brackets (cream ink) | Sprite (Multiple), borders from the manifest |
| `AP012/AP012_menu_plate_1024x576.png` | main menu backdrop (DEC-076) | Sprite (Single) |
| `AP012/AP012_skin{1,2,3}_512.png` | skin card portraits (AP-006 v002 front views, Adobe cutout) | Sprite (Single) |
| `AP012/AP012_hud_marker_256.png` | AP-010 beacon HUD marker (greyscale, runtime tint) | Sprite (Single) |
| `AP012/AP012-ui-manifest.json` | sprite rects / pivots / 9-slice borders (schema `AP012-ui-manifest-v1`) | TextAsset; `UiArtImporter` depends on it |

`UiArtImporter` (AssetPostprocessor, `Assets/_Prototype/UI/Editor/`) applies the contract to
every PNG under this folder: Sprite, 100 ppu, no mipmaps, clamp, bilinear, sRGB, alpha is
transparency, max 1024 (ADR-015-1 UI class), CompressedHQ (BC7 on Standalone). Atlases named
in the manifest are sliced with its rects; a manifest edit re-slices (source dependency).

## Contract (ADR-022-1)

- Source of truth for what exists on screen: the manifest names. Screens ask `UiArt` for a key
  (`UiArt.Icon("Hud.Gold")`, `UiArt.Frame(UiFrame.HudPanel)`, `UiArt.Medal(UiMedal.Epic)`) and
  always get something drawable: the AP-012 sprite when present, the procedural `UiSprites`
  shape otherwise. No screen depends on the art being imported.
- Colour never lives in the art: icons are white, frames are cream ink; every image is tinted
  with a `UiColor` token, so `UiTokens`' measured contrast pairs stay the only colour authority.
- Budget: UI art ≤ 12 MB source, every texture ≤ 1024² (`UiArtThemeTests.DokuSinifi_1024VeButce`).
- Regenerating: `asset-production/AP-012/tools/build_atlas.py` → copy `exports/atlas/*.png`,
  `exports/skins/*.png`, `exports/AP012-ui-manifest.json` here → run the theme builder.
