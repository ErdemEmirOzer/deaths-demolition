# `Assets/_Art/Characters/` — skinned characters (WP-016)

The AP-006 player and the AP-007 zombie in Unity, on Import Contract **v1.1** (ADR-016-1, `Editor/ArtCharacterContract.cs`) and the zombie look contract (ADR-016-2, `_Prototype/Characters/ZombieModuleLibrary.cs`). Built by `Tools > Deaths Demolition > Art Pipeline > Run Characters (WP-016)` (`ArtCharacterPrefabBuilder.BuildAll`, idempotent). The prototype reads one asset: `Resources/DD_Characters.asset` (`CharacterAssetSet`; `-ddCharacters Procedural|Skinned` / `DD_CHARACTERS` override) and keeps its M1 primitive bodies when the asset is absent or the source is Procedural.

## Files

| Path | What |
|---|---|
| `Player/player_base.fbx` | AP-006 PlayerRig (56 transforms: Root + 52 Humanoid + `socket_weapon_r/l`, `socket_back`) + `Player_LOD0/1/2` (23 978 / 9 590 / 3 390 tris). Avatar source. |
| `Player/Clips/player_rigtest_{idle,walk}.fbx` | Rig-test takes (`RigTest_Idle` 2.5 s, `RigTest_Walk` 1.33 s), Humanoid, looping, root locked — AP-009 replaces them. |
| `Player/Textures/T_player_skin{1,2a,3}_BaseColor.png`, `T_player_Normal.png`, `T_player_Packed.png` | v002 albedos (skin 2 = **2a**, user decision), OpenGL normal, ORM (R occlusion, G roughness, B metallic). `Textures/Derived/T_player_MetallicSmoothness.png` = R metallic, **G occlusion**, A smoothness (+ provenance). |
| `Player/M_player.mat` | URP/Lit: skin-1 albedo, normal, mask as `_MetallicGlossMap` and `_OcclusionMap` (G). Skins swap `_BaseMap` through a property block — one material asset, no instances. |
| `Player/Player_AP006.prefab` | `PlayerSkinnedVisual` + `ProceduralPoseBridge` root → `Model` (Animator: player avatar, `DD_Locomotion_Player.controller`; LODGroup 3 levels, cross-fade; LOD2 casts no shadow). |
| `Player/CharacterAnimationSet_Player.asset` | idle + walk clips, walk reference speed 1.4 m/s, controller. |
| `Zombie/zombie_body_{A,B,C}.fbx` | ZombieRig (= PlayerRig + `anchor_helmet/torso/legs` under Head / Chest / Hips, identity, 1.60 / 1.17 / 0.93 m) + `zombie_body_<fit>_LOD0..3`. Body A is the avatar source. |
| `Zombie/Modules/zombie_<id>.fbx` ×19 | 7 tops, 7 bottoms, 5 heads; meshes `zombie_<id>[_<cw>]_<fit>_LOD<n>` skinned to the same bone names. |
| `Zombie/Textures/T_zombie_atlas_{BaseColor,Normal,Packed,Mask}.png` | one 2K atlas; `Mask` = R skin, G cloth, B accessory, A emissive-permitted (provenance only). Derived: `T_zombie_atlas_MetallicSmoothness.png` (R metal, G AO, A smooth), `T_zombie_atlas_DetailMask.png` (A = cloth). |
| `Zombie/M_zombie_atlas{,_Special,_Elite,_Ultra}.mat` | one shared Lit material for the crowd + three version variants whose `_DetailAlbedoMap` flat tint (4×4 sub-texture in the library asset) reaches **cloth only** through `_DetailMask`.a; no emissive (DEC-067); no per-instance material, no property block on zombies (SRP batching). |
| `Zombie/ZombieModuleLibrary.asset` | 3 bodies × 4 LOD + 19 modules × colourway × fit × 4 LOD = 372 mesh refs with their bone-name orders, tier materials, tint textures. |
| `Zombie/Zombie_AP007.prefab` | `ZombieSkinnedVisual` + `ProceduralPoseBridge` root → `Model` (Animator: zombie avatar, `DD_Locomotion_Zombie.controller`; LODGroup 4 levels; 4 slots per level: Body/Top/Bottom/Head). |
| `Zombie/CharacterAnimationSet_Zombie.asset`, `AP007-modules.json`, `AP007-anchors.json` | clips; the AP-007 assembly contract and anchor table (copies, provenance). |

## Contract v1.1 (ADR-016-1) in one paragraph

v1 model rules (metres, `bakeAxisConversion`, normals imported, MikkTSpace tangents, no material/texture sub-assets, Read/Write off) plus: `animationType Humanoid` with the avatar created from **every** file (Unity's CopyFromOther also copies the source's mesh transforms and rejects files whose meshes differ — measured); the same explicit 52-bone human mapping written into every importer (`ArtCharacterContract.TemplateHumanBones`: Unity names for the 22 body bones, `<Side><Finger>1..3` → `<Side> <Finger> Proximal/Intermediate/Distal`, Pinky → Little); `optimizeGameObjects` off (bones stay Transforms for re-pointing, sockets, anchors); animation only from `Clips/`, every take looping with root rotation/height/XZ locked; no blend shapes. Textures: class **Character** (2048, streamed), roles `Mask` and `DetailMask` (linear, alpha kept). The gate (`ArtAssetGateTests`, `ArtCharacterTests`) verifies all of it, including that every avatar's human mapping equals its source's.

## How the characters run (M1 rigs stay the drivers)

The M1 primitive rigs keep running, hidden: `ZombieAnimator` (locomotion lean, arm carriage, attacks, stagger, flinch, death) and `PlayerProceduralAnimator` (lean, recoil, reload arm, flinch, death). `ProceduralPoseBridge` (execution order 1000) writes the block joints' rotation deltas — conjugated into each limb's bind frame — onto the Humanoid bones after the Animator, absolutely (rest-relative, no accumulation); the Animator's idle/walk clip keeps the legs, hips bob and (for the player) the left arm. Hitboxes and the armour placeholder roots move onto the bones / anchors at template build (`ZombieSkinnedVisual.RehomeGameplayNodes`): a bullet lands where the body is drawn; `ArmorAnchors` returns the AP-007 anchor transforms (AP-008 parents there in WP-017). The player's weapon mount re-parents to `socket_weapon_r` (barrel +Z); the M1 weapon arm's authored −72° raise is kept through the bridge so the arm reads as aiming until AP-009 delivers clips.

## Zombie look (ADR-016-2)

`ZombieLookSelector.Select(library, spawnSeed)` — pure, deterministic: fit A/B/C, top, bottom, head (H0 bald = no mesh) and colourways from a xorshift stream seeded by the EnemyFactory spawn seed. Weights translate the AP-007 crowd rule into per-spawn probabilities (faded cw2 0.26; mino cape 0.40 vs 1.0; bald 1.15 vs 1.0; fundoshi 0.70 and never with bare straps); 1000-seed test: cw2 ≈ 24 %, T7 ≈ 6 %, H0 ≈ 18 %, T6+B5 = 0, ≥ 700 distinct looks. `ZombieSkinnedVisual.Assemble` re-points the 16 slot renderers by bone name; body armour Present swaps a mino cape out (`WithoutMinoCape`). The version picks the shared tier material (`ZombieAppearance.ApplyVersion`), never an instance.

## Measured (WP-016)

See `planning/04` §WP-016 (E): 40 skinned zombies through the director in ~5 s, 35–40 distinct looks, 1 shared material, LOD0 ≈ 11.3 k tris per zombie; DEC-077 with real characters (zombie crowd/environment 0.10; player skins from the gameplay angle 0.048 / 0.078 / 0.038 — reported, AP-006 handover); perf and art-director verdict in the record.
