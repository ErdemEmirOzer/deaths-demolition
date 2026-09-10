# Deaths Demolition — Production Starting Codebase

**Status update — 2026-09-10:** This project was originally created as an experimental
visualisation prototype. The user has now approved it as the starting codebase that will
be developed incrementally into the final game. This does not automatically approve its
placeholder numbers, names or content; those remain tracked in
[`PROTOTYPE-ONLY.md`](PROTOTYPE-ONLY.md) until audited or replaced by approved values.

## Production Baseline (WP-000, 2026-09-10)

* **Verified on a clean copy** (Assets + Packages + ProjectSettings, no `Library/`) with
  Unity 2022.3.22f1 (887be4894c44) / URP 14.0.10 / macOS, Metal, Apple M1 Pro:
  import + compile PASS (0 `error CS`, 0 `warning CS`), EditMode **175/175**, PlayMode
  **21/21** on a real graphics device, macOS Player build PASS (verification only — not a
  platform decision), MainMenu and Run captured with no shader fallback and no exceptions.
  Physical keyboard/mouse, gamepad and OS-level Retina capture: **not measured** in WP-000.
* Evidence, commands, exit codes and hashes live in the WP-000 report set outside the
  repository (`S-01-ASAMA-2-KANIT-PAKETI.md`, final WP-000 report) — not in the historical
  `Build/` folder or the 2026-08-20 Player logs, which are superseded.
* Invariants for every later package: Unity 2022.3.22f1 and URP 14.0.10 are fixed
  (DEC-058/059); no bulk rewrite (DEC-060); no Unity-generated files in version control;
  only Boot is a valid entry scene; `planning/` is never written by agents.
* Fixed in WP-000 under test protection: firing while the simulation is frozen
  (`PlayerWeaponBinding.CanFire`, `PauseFireTests`); PlayMode tests writing the real player
  profile (`TestProfileIsolation` set-up fixture, `ProfileIsolationTests`); second-run reset
  coverage (`SecondRunTests`).
* Known gaps carried forward (not fixed in WP-000): paid reroll (DEC-050), orb lifetime vs
  DEC-045, hand-authored half of DEC-021, per-round purchase limit (DEC-048), locked-branch
  offers (DEC-049), persistent `DD.Prototype.DirectToRun` flag and F1 panel in Player
  builds, aborted runs not reaching the profile, leftover `Assets/InitTestScene…unity`.
* Historical documents at the repository root (`PROTOTIP-INCELEME-RAPORU-2026-08-20.md`,
  `PROTOTIP-ONARIM-PROMPTU-2026-08-20.md`) are frozen evidence; do not run their commands.

| | |
|---|---|
| Unity | `2022.3.22f1` (`ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | URP `14.0.10` (`Packages/manifest.json`) |
| Input | Input System `1.7.0`, actions in `Assets/_Prototype/Input/Resources/DD_Controls.inputactions` |
| Navigation | `com.unity.ai.navigation 1.1.5` NavMesh + a custom crowd layer |
| Network | none — the prototype runs fully offline |
| C# files | 233 under `Assets/` (WP-000 count, 2026-09-10; regenerate with `find Assets -name '*.cs' | wc -l`) |

---

## 1 · How to run it

1. The project is already registered in Unity Hub as
   `~/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept`.
   If it is not, use **Add → Add project from disk** and point at that folder.
2. Open it with **Unity 2022.3.22f1**. Any other editor version will trigger an
   upgrade prompt — decline it.
3. **The first open takes several minutes.** `Library/` has to be built, shaders have
   to be imported and the URP pipeline assets have to compile. This is normal and
   happens only once per machine. Do not interrupt it.
4. Open `Assets/_Prototype/Scenes/Boot.unity`.
5. Press **Play**.

`Boot` is the only correct entry scene. It loads
`Assets/_Prototype/Core/Resources/PrototypeConfig.asset`, applies the runtime settings
that asset dictates, brings up the persistent services (pools, config, debug panel) and
hands off to the first real scene (`Core/Boot/PrototypeBootstrapper.cs`). Starting from
any other scene skips that hand-off and the scene will be missing its services.

**Build scene order** (`ProjectSettings/EditorBuildSettings.asset`):

```
0  Assets/_Prototype/Scenes/Boot.unity
1  Assets/_Prototype/Scenes/MainMenu.unity
2  Assets/_Prototype/Scenes/Selection.unity
3  Assets/_Prototype/Scenes/Run.unity
4  Assets/_Prototype/Scenes/Results.unity
```

The meta flow is `Boot → MainMenu → Selection → Run → Results → MainMenu`.

Shortcut for iteration: the F1 panel has a **Misc → `PROTOTYPE_DIRECT_TO_RUN (next
boot)`** toggle that sends the next boot straight into `Run`
(`Core/Boot/PrototypeBootstrapper.cs:65`).

---

## 2 · Controls

Bindings are declared once, in `DD_Controls.inputactions`, and read through an
abstraction (`Core/Input/IInputSource.cs`, `Input/PrototypeInputSource.cs`). Movement
and aim are fully independent: you can run one way and fire the other. There is no dash
or dodge. Taking damage never interrupts movement, aim or fire.

### Keyboard and mouse — verified at prototype delivery (user statement, no artefact); WP-000 covers this path by simulated input only

| Action | Binding |
|---|---|
| Move (camera-relative) | `W` `A` `S` `D`, arrow keys also bound |
| Aim direction | mouse cursor position in the world |
| Fire (hold for automatic) | hold **Left Mouse Button** |
| Reload | `R` |
| Pause (freezes the whole simulation) | `Esc` |
| Prototype debug panel | `F1` |

### Gamepad — **NOT VERIFIED, no hardware on this machine**

The bindings below exist in `DD_Controls.inputactions` and the device-switch path in
`Input/PrototypeInputSource.cs:379` handles the right stick as an aim source. **None of
it has been exercised against a real controller.** Do not read this table as "working";
read it as "written and unsanctioned".

| Action | Binding | Status |
|---|---|---|
| Move | Left Stick | not verified |
| Aim + limited camera look-ahead | Right Stick | not verified |
| Fire | Right Trigger | not verified |
| Reload | West face button (`X` / `Square`) | not verified |
| Pause | `Start` / Menu button | not verified |

UI navigation is bound separately (`UI` action map): `WASD`/arrows/left stick/d-pad to
navigate, `Enter`/`Space`/South button to submit, `Esc`/East button to cancel.

---

## 3 · The F1 debug panel

**The panel is not a convenience. For several things in this prototype it is the only
way to see them at all.** Three of the request's own numbers make specific states
unreachable through natural play:

* **The performance target never occurs naturally.** Target active enemies is
  `min(7 + 2R, 32)` and the cap is `32`
  (`Core/Config/RoundConfigSection.cs:34`), while the stated prototype target is
  "around 40 concurrent enemies". A 40-enemy load can only be produced from
  **Spawn → `Force Spawn to 40 (perf measurement)`**
  (`Spawn/EnemySpawnDirector.cs:813`).
* **Ultra may never be seen.** `Executioner` debuts at round 8
  (`Core/Config/EnemyConfigSection.cs:309`, `IntroductionRound = 8`) and the rounds 1-7
  pool is 271 enemies. **Spawn → `Skip to Round 8 (Executioner debut)`**
  (`Spawn/EnemySpawnDirector.cs:846`) exists for exactly this reason.
* **The three gold collection modes cannot be compared any other way.** There is no
  in-game surface that switches them. **Economy → `Mode: DirectCredit` /
  `Mode: ContactPickup` / `Mode: ProximityMagnet (default)`**
  (`Economy/GoldService.cs:411-421`).

`F1` toggles it. It never appears during normal play. Nine tabs
(`Core/Debug/DebugPanelRegistry.cs:9-18`):

| Tab | What lives there |
|---|---|
| **Config** | Every field of `PrototypeConfig`, live-editable mid-run, each row showing the pending planning decision it occupies. |
| **Rounds** | Run/round/clock/XP readouts, start-restart run, end run, skip to next round, force-complete round, skip to round N, force the upgrade screen open or closed, live upgrade offer, gold grants. |
| **Enemies** | Clear/kill all, spawn one of each tier, fill to safety ceiling, break all armour, `Verify Damage Model` (runs the regional-damage assertions and logs violations), crowd / tier-schedule / FX-budget / armour readouts. |
| **Weapons** | Equip any classic weapon, trigger or end the Special Weapon, infinite ammo, live fire rate and damage, camera-shake intensity, `+1` on each upgrade branch, clear build, effect-pool and feel-budget readouts. |
| **Economy** | Wallet and collection readouts, the three gold-mode buttons, gold grants, collect/destroy all orbs, live magnet radius. |
| **Spawn** | Force spawn to 40, release forced target, clear enemies, spawning on/off, round-3 / round-5 / round-8 debut jumps, skip to round N, live spawn-point weight table. |
| **Perf** | `Pools`, `Crowd`, `NavMesh` readouts, crowd layer on/off, reset crowd peak. **The `Crowd` row is the only measuring instrument for the crowd solver's frame cost.** (`Navigation/NavigationSystemBehaviour.cs:93`) |
| **Candidates** | The candidate decision list. Flagging a value here changes nothing and writes nothing to `planning/`. |
| **Misc** | Player heal/damage/kill, player and camera readouts, input and aim-device readouts, occluder readout, UI contrast audit, reset local profile (**destructive** — resets the real player profile; never use in tests, tests use an isolated profile path), `PROTOTYPE_DIRECT_TO_RUN`. |

---

## 4 · Main systems and files

Runtime code lives under `Assets/_Prototype/`; URP pipeline, renderer and volume assets live in `Assets/Settings/` (plus `Assets/UniversalRenderPipelineGlobalSettings.asset`). New production content will go under `Assets/_Game/` (asset contract, WP-000).

| Folder | One-line summary |
|---|---|
| `Core/` | The spine: `PrototypeConfig` and its ten sections, the `PrototypeOnly` origin attribute, boot/scene hand-off, object pooling, the input abstraction, and the F1 panel plus its registry. |
| `Input/` | The single concrete `IInputSource` implementation over the Input System, plus the installer that decides which device currently owns aim. |
| `Player/` | Motor, aim, health and regeneration, damage feedback, procedural animation, the primitive-built rig and its three skins, and the binding that applies upgrade tiers to player stats. |
| `Camera/` | The fixed high-angle third-person rig with look-ahead and smoothing, the shake service, and the occlusion fader that dissolves anything between the camera and the player. |
| `Weapons/` | The three classic weapons and the Special Weapon: firing loop, hitscan engine, target resolution, modifier state for the upgrade branches, the stagger governor, the visual rig, and the Special Weapon spawn/pickup/round bridge. |
| `Feedback/` | The hit-feel chain — muzzle flash, tracer, impact, hit marker, damage number, burst, beacon and link effects — all pooled and metered by a shared budget, plus hit-stop and the camera-shake bridge. |
| `Enemies/` | The single zombie family: factory, targeting, tier scheduler, attack state machine, hit-reaction state, procedurally built body and appearance layers, and the pseudo-animator. |
| `Damage/` | The regional damage model: hitbox registry, the three-piece armour set, the damage contracts, and `RegionalDamageProof`, which asserts the approved armour and explosion rules against the live model. |
| `Navigation/` | NavMesh path following plus the custom crowd layer — solver, spatial grid and tuning — that gives soft separation without a solid traffic lock, and the ring-region model of the map. |
| `Spawn/` | The twelve hand-placed spawn points, the dynamic weight calculator that scores them per frame, the tier selector that owes each tier its guaranteed debut, and the director that maintains the round's target active count. |
| `World/` | The 90×90 m blockout: procedural mesh building, ground, structures, foliage, atmosphere, palette, landmarks, and the NavMesh bake — assembled at load by `WorldBootstrap`/`WorldBuilder`; the editor tool `RunSceneBuilder` is a one-time setup utility and is not run in normal work. |
| `Rendering/` | The serialized shader library (`PrototypeShaderLibrary`, in `Resources/`), the eight variant-pin materials that keep URP keyword variants in the Player build, the `PrototypeShaders` accessor that replaced `Shader.Find`, and the build-time check that stops a build when the library is missing. |
| `Rounds/` | The run director: round pools, round-end flow, the three-card upgrade shop, XP and stats tracking, the run flow clock that freezes everything on pause, and the debug registrations. |
| `Economy/` | The gold wallet, the orb, its tuning, and the service that implements all three collection modes behind one interface. |
| `UI/` | Every screen (main menu, selection, HUD, upgrade, pause, results) built on a shared kit — tokens, fonts, sprites, buttons, bars, motion, sound and a contrast auditor — plus the flow object that drives scene transitions. |
| `Profile/` | The local, offline profile: persistent stats, level and unlocks, its file store, and the per-run reward report that Results reads. |
| `Audio/` | Fully synthesised placeholder audio — no external assets. Recipes for weapons, impacts, ambience and meta, a clip library, a voice pool with a budget, buses, a mix plan and the ambience director. |
| `Tests/` | Edit-mode tests for the formulas (round, progression, special-weapon chance, regional damage, mesh winding) and play-mode tests for the flow and scene integrity, plus `OTOMATIK-KAPI.md` (what the automated gate verifies) and `MANUEL-TEST-KITI.md` (what only a human can verify). |

---

## 5 · PROTOTYPE-ONLY assumptions

The full list is in **[`PROTOTYPE-ONLY.md`](PROTOTYPE-ONLY.md)**. Summary:

* Every tunable value carries a `[PrototypeOnly(...)]` attribute naming the still-open
  planning decision it temporarily occupies. **491 such declarations exist in the
  code** (WP-000 count, 2026-09-10; 337 at the 2026-08-18 scan); the tag is visible in the Inspector and in the F1 panel, so nobody can read a
  number off the screen without also reading where it came from.
* **237 of the original 337 (2026-08-18 scan) had no source in the request at all**; the invented split is re-issued after WP-000 Phase 4 — they were invented so a
  system could function. They are flagged `invented` and rendered in a different colour.
* Six of the seven lines under *"Kararlaştırılması sonraya bırakılan alanlar"* in
  `planning/00-PROJECT-PLAN.md` are currently occupied by prototype numbers.
  `PROTOTYPE-ONLY.md` §1 maps which value occupies which line.
* **Gold collection with `ProximityMagnet` is now an approved product decision.**
  The other prototype comparison modes remain development-only until removed or retained
  by a later implementation decision.
* Weapon names, enemy names (`Runner`, `Guard`, `Executioner`), the upgrade branches and
  the achievements are placeholder content, not an approved roster.

---

## 6 · Known limitations

None of these are softened. All of them are real and observable.

**1 · The unlock ladder is exhausted in a single run.**
Reaching profile level 4 costs a cumulative 525 XP (`100 + 175 + 250`, from
`LevelRequirement = 100 + 75 × (Level-1)`, `Core/Config/ProgressionConfigSection.cs:64`).
A run that reaches round 8 earns roughly 545-600 XP (245 round XP for rounds 1-7 plus
271 base kill XP plus tier bonuses). From the second run onward **"Newly Unlocked
Content" on the Results screen is usually empty.** The numbers are frozen by user
decision and were not rebalanced.

**2 · The performance target does not occur in natural play.**
`min(7 + 2R, 32)` caps at 32 (`Core/Config/RoundConfigSection.cs:34`); the absolute
safety ceiling is 40 (`:38`); the stated target is "around 40". The 40-enemy load is
reachable **only** by forcing it from F1 → Spawn.

**3 · Ultra may not be seen through natural progression.**
The rounds 1-7 pool totals 271 enemies (12+20+29+38+48+57+67 from
`TotalEnemyPool`, `Core/Config/RoundConfigSection.cs:58`), and `Executioner` debuts at
round 8. The panel's round-jump presets exist because of this.

**4 · Gamepad could not be verified.** No controller hardware on this machine. The
abstraction and the bindings are written; they were never exercised. Acceptance
criterion 5 is split: keyboard+mouse verified, gamepad **not verified**. WP-000
(2026-09-10): still **not measured** — physical keyboard+mouse was not re-measured either;
WP-000 exercised the input path through simulated `IInputSource` sources only.

**5 · The hand-authored half of DEC-021 is absent from the prototype.**
DEC-021 requires hybrid round difficulty: a formula baseline plus hand-authored armour
ratios and tier distributions on specific rounds. Only the formula half is implemented.
The override table is a deliberately empty skeleton —
`_roundDifficultyOverrides: []` in `Core/Resources/PrototypeConfig.asset:417`. This was
a user decision, not an oversight.

**6 · The frame budget was not measured.**
The crowd solver's cost at 40 agents is an estimate, not a measurement. The measuring
instrument is **F1 → Perf → `Crowd`**
(`Navigation/NavigationSystemBehaviour.cs:93`). Until somebody reads that row under a
forced 40-enemy load, treat the 60 FPS / 40 enemy target as **not measured**.

**7 · The economy's well runs dry around rounds 10-13.**
Reported by the economy task from a 20-round × 20-seed simulation: the entire upgrade
tree fills there, after which gold is a meaningless counter. **This is a candidate
finding. It has not been applied** and nothing was changed because of it.

**8 · The round 1 upgrade screen opens dead in 13.4% of runs.**
Round 1 gross income is 24 gold (12 standard enemies × 2 gold each;
`TotalEnemyPool(1) = 12`, `RoundConfigSection.cs:58`, `GoldReward = 2` at
`Core/Config/EnemyConfigSection.cs:233`). Three of the eight branches offerable at
profile level 1 cost more than that — Piercing 30, Ricochet 30, Maximum Health 25
(`Core/Config/EconomyConfigSection.cs:92-150`). When the draw returns three of those,
nothing is affordable. The 13.4% frequency is the economy task's simulated figure and
was not re-measured here. It is not a wall — unspent gold carries over — but the screen
is dead. **Candidate finding, not applied.**

---

## 7 · Historical verification at original prototype delivery (superseded by WP-000, 2026-09-10)

> The MD5 values below describe `planning/` as of 2026-08-17 and are **no longer valid**;
> `planning/` was updated by the user on 2026-09-10. Current baseline evidence: see the
> "Production Baseline" section above.

* At the original prototype delivery, **`planning/` was not modified.** All four files were last written on
  **2026-08-17 03:29-03:30**, before this prototype folder existed
  (`prototype/DeathsDemolition_PlayableConcept` was created 2026-08-17 04:26). MD5s at
  time of writing:

  ```
  c6c76920e31102c6f45a50455eb8508b  planning/00-PROJECT-PLAN.md
  838bcc2d349582732268415eac80f03a  planning/01-REQUIREMENTS.md
  63f893401a29666c644db2ca1e7cb135  planning/02-FLOWS-ARCHITECTURE.md
  0f94760ccf6ad44bf150110eb94d34ea  planning/03-ROADMAP.md
  ```

* At the original prototype delivery, **`game/` was not modified.** It still contains only `.gitkeep` (0 bytes, dated
  2026-08-01), exactly as it was before this work started.

* At the original prototype delivery, **no DEC or REQ was created, changed or removed.** This prototype has no write access
  to `planning/`. The candidate decision list (F1 → Candidates,
  `Core/Debug/CandidateDecisionLog.cs`) is a notepad; **nothing moves from it into
  `planning/` without an explicit, separate instruction.**

* At the original prototype delivery, **no commit was made.** The initial baseline commit
  (DEC-062, WP-000 Phase 5) covers the verified planning documents together with the
  prototype source, settings and `.meta` files; generated folders (`Library/`, `Temp/`,
  `Logs/`, `UserSettings/`, `Build/`) stay excluded via the root and `prototype/` `.gitignore`.

* **No paid asset, no external download, no internet or cloud service** is used. Every
  package in `Packages/manifest.json` ships inside the 2022.3.22f1 installation. All
  audio is synthesised at runtime; all meshes are procedural primitives.
