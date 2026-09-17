# Secondary-motion chains — Contract v1.1 addendum (AP-006C, 2026-09-17, P-06)

**What:** optional deform bones `sec_<chain>_<i>` (i = 1..n from the root) added to the character rigs for costume / module parts that should swing: parented to a core bone (Hips / Head / UpperChest), connected in a chain, rest pose = hanging direction. The **56 core bones are untouched** (names, hierarchy, rest transforms; AP-007 rig check 14/14 with the chains present; Z02/Z05 now count `sec_*` separately, ≤ 24). Not in the Unity Humanoid map (import them as extra transforms); **never keyed by the AP-009 clips** (the 85 FBX carry the 56-bone rig) — the runtime drives them with a spring / verlet script (S-06) using the table below. Meshes without chain weights are unaffected; a skinned mesh that carries chain weights (player LOD0–2, zombie T7 / H2 / H3 / H5 / H7) needs the rig that has the bones (files below).

**Runtime model (what Blender verified):** per chain, particles p₀..pₙ (p₀ = root, kinematic, follows the parent bone); each frame `v = (p − p_prev)·(1 − damping)`, `a = gravity·(0,0,−1) + stiffness·400·(rest_i − p)` (rest_i = the rest direction in the parent's current frame), `p += v + a·dt²`; then 3 iterations of: segment length constraint → angle clamp (`max_angle_deg` from the rest direction, per segment) → push-out from the parent-bone capsule (radius `body_radius + collision_radius`). Bones aim at their next particle. dt = 1/30 s in the verification; S-06's script may use its own integrator — keep the parameter meaning (stiffness 0..1 → 0..400 /s², damping per frame at 30 fps).

## Player (`AP-006/exports/secondary/player_base_sec.fbx` — 63 bones, LOD0–2; source `05-blender/source/character-master-sec.blend`; spec `05-blender/validation/AP006C-chains-player.json`)

| chain | parent | segments | stiffness / damping | max angle | collision | vertices | note |
|---|---|---|---|---|---|---|---|
| `sash` | Hips | 3 × 0.080 m = 0.24 m | 0.45 / 0.15 | 70° | 0.02 m on a 0.17 m capsule along the parent bone | 126 | obi tails hanging behind the right hip (red sash ends) |
| `pouch` | Hips | 1 × 0.140 m = 0.14 m | 0.6 / 0.2 | 45° | 0.03 m on a 0.15 m capsule along the parent bone | 73 | belt pouch, right hip front — single pendulum bone |
| `gourd` | Hips | 1 × 0.180 m = 0.18 m | 0.6 / 0.2 | 45° | 0.04 m on a 0.15 m capsule along the parent bone | 182 | gourd flask, left hip front — single pendulum bone |
| `band` | Head | 2 × 0.070 m = 0.14 m | 0.5 / 0.15 | 80° | 0.01 m on a 0.1 m capsule along the parent bone | 274 | hachimaki tails behind the head |

Blender verification (AP-009 v006 clips, settled cycle; max segment angle / tip travel / min clearance to the body capsule after push-out):

| clip | per chain |
|---|---|
| `player_walk_fwd` | sash 51.5° / tip 0.064 m / clr +0.020 · pouch 12.6° / tip 0.031 m / clr +0.079 · gourd 9.5° / tip 0.03 m / clr +0.085 · band 23.7° / tip 0.027 m / clr +0.011 |
| `player_run_strafe_left` | sash 14.5° / tip 0.041 m / clr +0.020 · pouch 5.1° / tip 0.013 m / clr +0.095 · gourd 4.6° / tip 0.015 m / clr +0.100 · band 19.9° / tip 0.038 m / clr +0.012 |
| `player_run_fwd` | sash 23.4° / tip 0.064 m / clr +0.020 · pouch 9.9° / tip 0.024 m / clr +0.092 · gourd 8.4° / tip 0.026 m / clr +0.097 · band 19.8° / tip 0.039 m / clr +0.011 |
| `player_hit_light` | sash 19.3° / tip 0.034 m / clr +0.020 · pouch 10.0° / tip 0.024 m / clr +0.088 · gourd 8.1° / tip 0.025 m / clr +0.089 · band 67.0° / tip 0.112 m / clr +0.010 |

## Zombie (`AP-007/exports/v003/*.fbx` — 73 bones in every body / module FBX; source `05-blender/source/zombie-master-v003.blend`; spec `05-blender/validation/AP007C-chains-zombie.json`)

| chain | parent | segments | stiffness / damping | max angle | collision | vertices | note |
|---|---|---|---|---|---|---|---|
| `mino_back` | UpperChest | 3 × 0.100 m = 0.30 m | 0.55 / 0.22 | 55° | 0.02 m on a 0.24 m capsule along the parent bone | 1398 | straw mino cape, back centre |
| `mino_left` | UpperChest | 3 × 0.100 m = 0.30 m | 0.55 / 0.22 | 55° | 0.02 m on a 0.24 m capsule along the parent bone | 1458 | mino cape, left side |
| `mino_right` | UpperChest | 3 × 0.100 m = 0.30 m | 0.55 / 0.22 | 55° | 0.02 m on a 0.24 m capsule along the parent bone | 1650 | mino cape, right side |
| `hair_mass` | Head | 2 × 0.110 m = 0.22 m | 0.5 / 0.2 | 75° | 0.01 m on a 0.11 m capsule along the parent bone | 897 | H2 / H7 matted hair mass hanging down the neck |
| `queue` | Head | 2 × 0.045 m = 0.09 m | 0.7 / 0.25 | 80° | 0.01 m on a 0.1 m capsule along the parent bone | 175 | H3 limp topknot queue |
| `hachimaki` | Head | 2 × 0.055 m = 0.11 m | 0.5 / 0.2 | 90° | 0.01 m on a 0.1 m capsule along the parent bone | 105 | H5 hachimaki tail |
| `bandage_tie` | Head | 2 × 0.070 m = 0.14 m | 0.5 / 0.2 | 90° | 0.01 m on a 0.1 m capsule along the parent bone | 168 | H7 bandage tie |

| clip | per chain |
|---|---|
| `zombie_shamble_A` | mino_back 18.7° / tip 0.02 m / clr +0.020 · mino_left 11.0° / tip 0.03 m / clr +0.069 · mino_right 11.1° / tip 0.029 m / clr +0.069 · hair_mass 29.9° / tip 0.039 m / clr +0.010 · queue 59.6° / tip 0.042 m / clr +0.010 · hachimaki 33.0° / tip 0.053 m / clr +0.010 · bandage_tie 41.7° / tip 0.067 m / clr +0.010 |
| `zombie_run` | mino_back 55.0° / tip 0.087 m / clr +0.020 · mino_left 55.0° / tip 0.124 m / clr +0.060 · mino_right 55.0° / tip 0.087 m / clr +0.062 · hair_mass 68.0° / tip 0.109 m / clr +0.010 · queue 57.7° / tip 0.062 m / clr +0.010 · hachimaki 87.1° / tip 0.121 m / clr +0.010 · bandage_tie 78.9° / tip 0.127 m / clr +0.010 |
| `zombie_hit_front_std` | mino_back 49.7° / tip 0.157 m / clr +0.020 · mino_left 46.7° / tip 0.098 m / clr +0.066 · mino_right 55.0° / tip 0.155 m / clr +0.059 · hair_mass 75.0° / tip 0.228 m / clr +0.010 · queue 83.3° / tip 0.084 m / clr +0.010 · hachimaki 90.0° / tip 0.151 m / clr +0.010 · bandage_tie 90.0° / tip 0.189 m / clr +0.010 |
| `zombie_attack_swipe` | mino_back 51.7° / tip 0.126 m / clr +0.020 · mino_left 55.0° / tip 0.175 m / clr +0.045 · mino_right 28.9° / tip 0.112 m / clr +0.046 · hair_mass 75.8° / tip 0.141 m / clr +0.010 · queue 84.2° / tip 0.102 m / clr +0.010 · hachimaki 90.0° / tip 0.145 m / clr +0.010 · bandage_tie 79.7° / tip 0.107 m / clr +0.010 |

Chains ↔ modules: `mino_*` → T7_mino_cape; `hair_mass` → H2_hair_matted + H7_matted_wound; `queue` → H3_topknot_broken; `hachimaki` → H5_hachimaki_band; `bandage_tie` → H7_matted_wound. A zombie without those modules has the bones at rest (no cost beyond the transform).

Previews: `AP-006/06-renders/secondary/*.png` (walk / run / strafe / hit, 6 frames each), `AP-007/06-renders/v003/secondary/*.png` (shamble / run / hit / swipe). Clearance is ≥ +0.01 m on every chain and clip after the push-out (the runtime script must do the same push-out: the raw sim dips into the head capsule by ≤ 3 cm on the head whip before it).
