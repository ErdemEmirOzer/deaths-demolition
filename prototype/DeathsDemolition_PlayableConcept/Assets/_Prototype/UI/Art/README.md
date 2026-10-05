# UI art (WP-022 · AP-012) — theme, atlases, plates

The imported UI art lives inside the UI module (`Assets/_Prototype/UI/Art/`), not under
`Assets/_Art/`: the environment/character import contract there (`ArtImportContract`,
`T_<role>_<Map>` naming, streaming mipmaps, class sizes, its gate tests) is for world
textures and would reject sprite atlases. UI textures have their own contract, ADR-022-1.

## Files (`Resources/UI/`)

| path | what | import |
|---|---|---|
| `UiTheme.asset` | the one `UiTheme` ScriptableObject: sprite lists by manifest name, plate, marker, skin previews, default UI scale | built by **DD → UI → Build UI Theme From AP-012 Atlases** (`UiThemeBuilder.Build`) |
| `G1/DD_G1_glyphs_sdf_1024.png` | WP-068 §7.1 G1: every single-colour glyph (148) as a signed distance field — 64² cells, spread 6 px, 0.5 = edge. The former AP-012 icons + status, AP-013 weapons, AP-014 Specials, the reticle pair and divider, and AP-012 v004 A1–A3 / V1 / M1 + the seal | Sprite (Multiple) from `G1/AP012-ui-manifest.json`, **Alpha8**; drawn through DD/UI/Fx SdfGlyph (`UiArt.ApplyGlyph`, automatic in `UiFactory.NewImage`, `UiArt.SetGlyph` on a swap) |
| `A4/DD_A4_frames_v3_1024.png` | WP-068 A4 Frames v3 (2× authored, `pixelsPerUnitMultiplier` 2) + the V1 key caps `Key.Cap` / `Key.Wide` | Sprite (Multiple), borders from `A4/AP012-ui-manifest.json`, **Alpha8** (uGUI whitens Alpha8) |
| `Cursor/Cursor.{Arrow,Press}_{32,64}.png` | V1 hardware cursor (Cream100 fill, Sumi900 outline — the one colour exception) | Cursor, RGBA32, readable; `UiCursor` |
| `Tiles/AP012_tile_kumiko_asanoha_256.png` | A6 kumiko lattice (curtain doors only) | single channel BC4, repeat; `UiPatternTile` (DD/UI/MaskTint) |
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
- Budget: UI art ≤ 12 MB source, every texture ≤ 1024² (`UiArtThemeTests.DokuSinifi_1024VeButce`); `Wallpapers/`
  (the loading key arts and the round-end wallpaper) is its own class, ≤ 10 MB source (WP-081, C-00 05.10).
- Regenerating G1 / A4: `wp-068/tools/g1/build_g1.py <out>` (sources: AP-012 v004 `exports/`, SHA-checked, and the
  retired RGBA atlases snapshotted in `wp-068/g1-src/`) → copy `G1/`, `A4/`, `Cursor/`, `Tiles/` here → run the theme
  builder. Plates and skins: `asset-production/AP-012/tools/build_atlas.py` as before.

## Icon intake (WP-081 B2 · menu v4, AP-028)

Menu v4 trades words for icons (R2 U1). Every new icon goes through G1 so it stays one draw path, one colour authority and one budget:

- **Delivery** (AP-028 / Codex): one single-colour glyph per key, white on transparent, drawn on a 24 px grid
  (2 px stroke at 24; no detail or gap under 1.5 px at 24 — it has to read at `UiIconSize.S`). SVG, or PNG ≥ 256²,
  in the AP-012 `exports/` layout; no colour, no shading, no baked outline or glow (the shader does those).
- **Build**: `build_g1.py` turns it into a 64² signed-distance cell (spread 6 px, 0.5 = edge) on the 1024² G1 page,
  **Alpha8**, no mips, sliced by the manifest. A key is `Area.Name` (`Menu.Settings`, `Menu.Profile`, …).
- **Use**: only through `UiIconControls.Button` / `Tab` (`UiIconButton`): a string key for the reader name (never a
  literal), the word on hover and focus (tooltip) or under the glyph (tab), a 44 px hit, glyph 24 / 32 / 40 px on the
  8 px grid (`UiIconTokens`). A key the atlas lacks hides the glyph (the name and word stay) and lands in
  `UiIconButton.MissingIconKeys` — the screen tests require it empty.
- **Colour**: tinted by token only — TextPrimary for a plain button or the chosen tab, TextSecondary for the others
  (≥ 3:1 on their fills, WCAG 1.4.11). A state is never hue alone: the chosen tab also gets the raised fill, the cream
  outline and the bar under its word (colour-blind safe, `UiIconKitTests`).
- **Budget**: the G1 page counts toward the 12 MB UI source budget above; a second page needs C-00's budget decision.
