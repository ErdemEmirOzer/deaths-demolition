# Assets/_Prototype/Audio — audio architecture (ADR-021-1/2 · WP-048 · WP-057; unified in WP-060)

One page for the whole audio system as it stands at the M3 close. Asset-side rules (AP-011 files, manifest, catalog build, loudness standard) live in `Assets/_Art/Audio/README.md`; per-package history is in `planning/04-IMPLEMENTATION-PLAN.md` (§WP-021 … §WP-060). Everything below is what the code does today; if a paragraph and the code disagree, the code wins and this page is the bug.

## 1 · Signal flow

```
game event ─► AudioEventMap row (sound id, bus, spatial) ─► AudioEventRouter._play ─► AudioMixPlan placement
           (gain, pitch, ducks, retrigger, max instances)  ─► PrototypeAudioDirector.PlayResolved
           ─► AudioVoicePool (World 16/32 · Signal 8)  ─► AudioClipLibrary (catalog clip, else WP-002 recipe)
           ─► AudioBusMixer (software: Weapon / Impact / Target / Interface / Ambience / Music ducks)
           ─► AudioMixerBridge ─► DD_AudioMixer (Master ← Music · Ambience · SFX-World · SFX-Player · UI)
           ─► Master Compressor (limiter −8 dB / 10 ms / 90 ms) ─► AudioListener (player-anchored)
```

- **Event surfaces** (the only producers the router listens to): `AudioEvents` (Combat cues), `RunAudioEvents` (Run cues), `DamageEvents.EnemyDamaged` (pain grunt only), `EnemyCombatEvents.Raised` (WP-040 beats: telegraph / attack_commit / attack_hit / attack_miss / stagger / armor_break_<region> / death_<tier> / spawn_rise / spawn_land / sw_push / sw_web / sw_pull — the sole owner of zombie vocals), `CharacterAnimationEvents.Any` (WP-018/032C clip events: footsteps, weight shifts, weapon handling, pickup), `BreakableProp.Broke` (WP-038B `EventId` data key — six AP-017 events — refined by root name), `UiEvents.Raised` (WP-022B: 18 bound rows, 21 reserved to their existing owner so nothing plays twice), `SpecialWeaponUsageEvents.Usage` (WP-056: Fired / FieldSpawned / FieldEnded / Hit), `RunEvents` (phase, round, run) and `PlayerHealth` (low health, death).
- **`AudioEventMap`** (ADR-021-1) is the single table: source · game event name · `AudioSoundId` · bus · spatial · note. `Reserved` rows document an event whose sound is owned by another row on the same frame. Helpers resolve parametrised rows: `ZombieWindup/Attack/Hit/Death/Idle/Aggro(tier)`, `ArmorBreakRegion(slot)`, `WeaponFoley(weapon, clipEvent)`, `SpecialUsage(id, kind)`, `UiSoundFor(uiEvent)`, `BreakSoundFor(prop)`.
- **`AudioMixPlan`** owns every per-cue mix decision (gain, pitch ± jitter, `DuckAmbience/DuckCombat/DuckMusic` + `DuckSeconds`, `MinRetrigger`, `MaxInstances`, spatial). Code outside the plan never sets a gain.
- **`AudioClipLibrary`** overlays `DD_AudioCatalog.asset` (built from `AP011-audio-manifest.json`, 181 ids / 419 files at v012, manifest SHA-256 `87c8b073c22f7d4b61a038fe496e175bc473c81263ccfe877df3244be730b943`) on the WP-002 procedural recipes: a sound id with catalog clips plays a clip (round-robin, no immediate repeat); without one it falls back to its recipe — today no live row falls back (procedural live SFX = 0, `EVENT-COVERAGE-MATRIX.md`).

## 2 · Mixer and level standard (ADR-021-2)

- `DD_AudioMixer.mixer`: five groups under Master, four snapshots (Run / RoundBreak / Pause / Menu) with the v2.1 table (Run: Music −5 / Amb −4 / World 0 / Player −1 / UI −3; RoundBreak −3/−3/−6/−3/0; Pause −14/−12/−18/−18/−2; Menu −4/−10/−80/−80/−1), exposed `MasterVolume … UiVolume` and `MasterLimiter{Threshold,Attack,Release}`. `AudioMixerBuilder.Verify` (EditMode) requires the limiter and the snapshot table; `Build Mixer` repairs in place, never recreates the asset.
- **Master volume** has one owner: `UiSettings.MasterVolume` → `PrototypeAudio.MasterVolume` → `PrototypeAudioDirector.RequestMasterVolume` → exposed `MasterVolume`, 60 ms linear ramp; `AudioListener.volume` stays 1 while the mixer exists.
- **Software buses** (`AudioBus`, `AudioBusMixer`): sample-accurate cue-level ducking that survives snapshot transitions — weapon fire ducks impacts, HUD threat chips / low health duck ambience 0.7 · combat 0.63 · music 0.7 for 0.35 s (the "taiko band" rule), stingers duck the music layers to 0.55 (1.5 s release), `MusicDirector.CombatTrim` −2 dB at threat layer 3.
- **Loudness standard** (asset side, EditMode re-measured): SFX −18 LUFS (UI −26 / ambience −24 / tier-vocal & foley −22 target sets), music −16, clip true peak ≤ −1.5 dBTP; master output peak ≤ −1 dBFS under the loudest stack (PlayMode `Limiter_EnYuksekYigin_…`). Phase loudness (PlayMode `MixGecisi_FazOlcumu_…`, BS.1770 K-weighted gated): fight ≈ −21, break ≈ −29, menu ≈ −28 LUFS. The WP-060 Player-output report (§6) is the reference for the real run.
- **Listener** (`AudioListenerAnchor`): sits at the player's head with the camera's yaw whenever a rig exists, hands back to the scene listener otherwise; rolloff 8–60 m linear (WP-002), so "8 m plateau" = the player's own space.

## 3 · Ambience (WP-021 · WP-048 Look B)

`AmbienceDirector`: five region beds (`AudioRegionMap` polygons from `AP005_Placement.asset`, 6 m centre capture, 0.25 s poll) + common bed, time-based per-bed cross-fade (2.5 s, rate fixed at target change — WP-039 P1), scatter ring 9–24 s (bamboo knock, metal ring, stone settle, wood creak, slate drip in the wet look), the WP-029 **wet look** layer (`WetLook`: distant thunder on its own 40–90 s clock, 3D pond bed at the pond centroid registered by `Ap005SurfaceTags.RegisterPond`), phase gain (rise 0.45 / fall 0.30 per s), and the Run/Menu snapshot hand-off.

**Surfaces** (ADR-021-1 v2): surface data lives in data, not assets — `DD.Art.Ap005SurfaceTags` tags every placement instance at scene load from its family (`FamilySurface`), the ground stand-in is Dirt, `AudioSurfaceHints.Refine` reads Stone inside the stone routes, WP-038 plates refine by name (`AP015_paving_*` → Pavement, `AP015_ground_center*` → Gravel, AP-015 water → WaterShallow). Footsteps: `anim.foot_l/foot_r` → downward ray → `AudioSurfaceHints.Footstep(collider, point, kind)`; `shift_l/shift_r` = half-gain step. FootstepWaterDeep stays reserved (no wading-depth signal).

## 4 · Music v2 (WP-057)

`MusicDirector`: four run layers (base / percussion / tension / accent; AP-011 stems, lock-step 131.6 s, tension retuned +4 st in WP-058B) driven by `Intensity` (progress, alive enemies, 7 s round-start surge) with the WP-033 **threat layer as a floor** (perc 0/.55/.70/.72, tension 0/0/.50/.66, L3 base 0.70 + accent 0.76); `PlayStinger` for round break / Special pickup / death / run end over the layers; menu and results loops on `Music Phase Loop` cross-faded against the run layers (which leave entirely outside a run, `PullbackSeconds` 3 s); `OnRunEnded(died)` idempotent. Everything above is on `AudioBus.Music` → mixer group Music (F1 slider).

## 5 · Special Weapons and enemy reactions (WP-058 · WP-058C)

`OnSpecialUsage`: Fired → `SwFire{Kagura,Ofuda,Tide}` over `HeavyWeaponFire`; FieldSpawned → Kagura wave-front one-shot / Ofuda web hum and Tide well as **3D loops** at the field origin (director `Special Field` pool: one Ofuda + two Tide sources, matching shipped `MaxWells`, Impact bus) until FieldEnded (+ `SwFieldEnd*`); Special end/new run/death is the silent teardown belt when field owners cancel without FieldEnded. Hit → per-body touch (`SwHit*`, gain × falloff, 60 ms throttle); Activated / Deactivated keep the WP-002 spin-up / expired cues; `SwSpawnGong` belongs to `SpecialWeaponSpawn` only (REQ-053). The body's side (WP-055 `sw_push / sw_web / sw_pull`) is `ZombieReact{Push,Web,Pull}` from the beat surface (pull throttled 0.25 s per body).

## 6 · Evidence tools

- **Player output capture** (WP-060/WP-065, dev Player / Editor only): `-ddAudioCapture <file.wav>` arms `AudioOutputCapture`; taps on every `AudioListener` forward the enabled listener's final mix (post mixer/limiter) through a lock-free ring into 16-bit PCM. `<file>.markers.txt` adds one-second phase/snapshot, alive tier, action and active voice/loop density rows; `<file>.telemetry.tsv` is the fixed-capacity request/start/retrigger/recycle/drop/steal/end trace plus delivered/suppressed event counts. `python3 -B tools/qa/audio_capture_report.py <wav> --require-alive 40 --require-tier Ultra --require-actions 1 --require-voices 1 --require-phase Fighting` writes JSON with separate LUFS-I, maximum LUFS-M/S, LRA, sample peak dBFS and true peak dBTP, and fails on capture drops, trace overflow, silence, or missing dense-40/tier/action/voice/Fighting evidence.
- **Dense evidence load** (WP-065, dev Player / Editor only): `-ddAudioDense40 -ddAudioDenseRound 12 -ddAudioDenseSeconds 30` waits for the requested evidence round, asks the shipped spawner for its existing safety ceiling, records 30 s after that checkpoint, then exits cleanly. It does not synthesize input or cues; pair it with the existing QA bot so movement, fire, reload, hits and Special usage still travel through their production owners.
- Tests: EditMode `DD.Prototype.Tests.EditMode.Audio` (map ↔ router ↔ UI contract, tier sets, foley/Special id sets, mixer verify, manifest loudness re-measure, surface rules, plate name rule), PlayMode `AudioSliceTests` (UI rows, master ramp, beats, surfaces, footstep variants, foley, music v2, phase meter + ducks, Special usage, reactions, limiter, capture, GC 0).
- Debug: F1 panel sliders (World voices, Music bus), `PrototypeAudioDirector.Describe()`, `MusicDirector.Describe()`.

## 7 · Rules that keep it honest

1. One owner per event: a UI interaction, a beat or a clip event plays from exactly one row; add a `Reserved` row instead of a second binding.
2. No gain outside `AudioMixPlan`; no snapshot value outside `AudioMixerBuilder.SnapshotVolumesDb`.
3. Every new `AudioSoundId` gets a recipe fallback, a manifest entry (or a documented reservation), a matrix row and a test that the row exists.
4. Credits: AP-011 renders only with the real unit cost verified first (Seed Audio 1.9–2.8 cr/render on the record); everything since v005 is derived offline at 0 cr — see `asset-production/AP-011/README.md` §credit request list for what a top-up should buy first.

## 8 · UI v3 (WP-068 slice 11)

- **Contract:** `UiEvent` +15 (ScreenExiting, CurtainClosed/Opened, PressDown, BootLogo, TimelineSkipped, HudNewBestRound, HudEliteDown/UltraDown, CardDealt, CardFlipped, StampLanded, CounterRolling/Settled, RerollHoldStarted) = 54; one map row each (TimelineSkipped is the control row). Sliders raise `UiEvents.Raise(e, label, value01)` → the tick's pitch 0.90–1.10.
- **Controllable UI voices** (director, 2 sources, Interface bus): soroban roll (stops at CounterSettled or 0.6 s), hold rub (Confirm completes, Back cuts in 20 ms), boot seal (skip = 120 ms fade). Card deal +0/+2/+4 % per index; stamp detail rare/epic; hover sweep ×0.8 (floor 0.45); NEW BEST ribbon waits ≤ 3 s behind a threat chip; ±3 % jitter on every interface cue.
- **Pause:** the Interface pool ignores the listener pause and the software mute (pause menu audible); pause/resume play the fusuma short slide / opening. **Death:** ink texture 0.6 s after the stinger.
- **UI SOUNDS (U8):** `UiSettings.UiVolume` → `PrototypeAudio.UiVolume` → `AudioBusMixer.SetPlayerVolume(Interface)` (not the mixer's exposed UiVolume, which would override the snapshot UI trims).

## 9 · Weapon fire v3 (WP-072 B1)

- **Contract:** `AudioEvents.RaiseWeapon(ClassicWeaponFire, audioId, …)` names the weapon (WP-069 `AR1…RL1`; A/B/C → AR1/SMG1/SG1 via `WeaponAudioIdentity.ForClassic`). Plain `Raise` still resolves by intensity (legacy RifleFire/SmgFire/ShotgunFire).
- **`WeaponFireAudio`:** one-shot weapons = dry shot + space tail every shot. Hybrid AR1/SMG1/LMG1 (Auto ∧ RPM ≥ 600): full first shot, then transient per shot over the RPM-locked body loop (rate bends ±15 % to the real cadence); a 1.6-period gap = release → loop fades in 40 ms and the tail rings once.
- **Space:** `AcousticSpaces.Resolver` (S-05 WP-074 `acoustic-spaces-v6.json`), default Open; tail files −20 LUFS, placement −12/−8/−6 dB (Open/Corridor/Hall). **Camera:** `CameraModeService.Current` → spatial 0.35/0.25/0, near-field 0/+1/+2 dB, transient +0/+3/+7 dB, tail +0/+1/+2 dB (STANDARDS §4).
- **Import:** weapon v3 Vorbis q0.9, transients PCM, tails stereo (DEC-093). Only produced ids take the new path; a missing catalog falls back to the legacy sounds.

## 10 · Weapon foley & casings v3 (WP-072 B2)

- **Foley:** clip events on the player rig resolve through `WeaponFoleyV3` for the equipped weapon (`WeaponDefinition.AudioId`, A/B/C → AR1/SMG1/SG1): its own mechanism set (35 ids; S-02 WP-069 §3 under WP-071 names — charge, box_open/belt_seat/box_close, bolt_up/back/fwd/down, shell_in, rocket_in/seat; WP-032C mag/chamber/eject map to the nearest action), else its weight family's handling voice (Light SMG · Medium AR/SG/SR · Heavy LMG/RL) at the weapon's pitch. An action the weapon lacks is silent (an auto rifle's per-shot eject). Without produced v017 clips the WP-048B A/B/C foley plays.
- **Dry fire:** `Foley{Family}DryFire` at the weapon pitch.
- **Casings:** `CasingAudio` hears `WeaponEvents.Mechanism` CasingEjected, lands the casing under the port after its fall (0.25–0.6 s) on the surface there (Stone/Wood/Dirt; water/foliage silent; LMG + belt link); top-down −18 / TP −12 / FP −8 dB against the fire voice.
