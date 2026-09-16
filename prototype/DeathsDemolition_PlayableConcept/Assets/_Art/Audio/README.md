# Assets/_Art/Audio — produced sound set (WP-021 / AP-011)

**What lives here:** the AP-011 delivery — `AP011/*.wav` (48 kHz mono 16-bit, Git LFS) and `AP011-audio-manifest.json` (one entry per `AudioSoundId`: event, files, loop, LUFS, true peak, bus, spatial, provenance). Nothing here is referenced directly by code: the manifest is turned into `Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset` by **DD › Audio › Build Catalog + Region Map** (`AudioCatalogBuilder`, idempotent — the catalog stores the manifest's SHA-256), and the runtime library (`AudioClipLibrary`) overlays the catalog on the WP-002 procedural recipes per sound id. A sound without a catalog entry keeps its recipe; a checkout without LFS content is audible.

## Adding or replacing sounds

1. Produce and process in `asset-production/AP-011/` (see its README: Seed Audio prompts → `tools/ap011_process.py` windows/loops/normalisation → `exports/`), then `tools/ap011_manifest.py <worktree> <version>` copies files and rewrites the manifest.
2. Run **DD › Audio › Build Catalog + Region Map** (or `-executeMethod DD.Prototype.Audio.EditorTools.AudioCatalogBuilder.BuildAllBatch`). Importer settings are pinned by the builder: Vorbis, quality 0.6 (0.7 loops), force mono, preserve sample rate, one-shots decompressed on load, loops ≥ 8 s streamed.
3. EditMode `DD.Prototype.Tests.EditMode.Audio` re-measures the shipped clips (BS.1770 `LoudnessMeter`) against the manifest and the standard: SFX −18 LUFS, ambience −20, music −16, master true peak ≤ −1.5 dBTP (measured Vorbis clip ≤ −0.5), ±1.5 LU — or peak-limited below target (short transients).

## Standards and structure (ADR-021-1/2 in `planning/04-IMPLEMENTATION-PLAN.md`)

- Mixer `Assets/_Prototype/Audio/Resources/DD_AudioMixer.mixer`: Master → Music / Ambience / SFX-World / SFX-Player / UI; snapshots Run / RoundBreak / Pause / Menu; exposed `MasterVolume … UiVolume` (dB). Built by **DD › Audio › Build Mixer** (reflection over the internal controller; an existing valid asset is never rebuilt).
- Event map: `AudioEventMap` (every `CombatAudioEvent`, `RunAudioEvent`, player, zombie vocal, UI, six ambience beds, four music layers; WP-018 rows reserved).
- Regions: `DD_AudioRegionMap.asset` from `AP005_Placement.asset` polygons (center/north/east/south/west); `AmbienceDirector` cross-fades the region beds over 2.5 s.
- Music: `MusicDirector` — base / percussion / tension / accent, driven by `Intensity` (progress, alive enemies, round-start surge) and tier/Special presence; pulls back between rounds.
- Reserved cue: `SwSpawnGong` belongs to `SpecialWeaponSpawn` only (REQ-053).
