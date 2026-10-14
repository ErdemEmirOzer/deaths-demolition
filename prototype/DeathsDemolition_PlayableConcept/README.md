# Deaths Demolition: Unity project

This folder is the Unity project of **Deaths Demolition**, a top-down wave-survival zombie shooter. It is the game itself, developed incrementally from the original playable concept. The folder name and the Unity product name (`Deaths Demolition - Playable Concept`) are kept on purpose, so the player's save-data path never moves. A release build shows **"Deaths Demolition"** everywhere a player looks: the Dock, the menu bar, the About box and the window title.

For the feature overview, architecture and tooling, start at the [repository README](../../README.md).

| | |
| --- | --- |
| Unity | `2022.3.22f1` (`ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | URP `14.0.10` (`Packages/manifest.json`); URP assets per quality tier in `Assets/Settings/` |
| Input | Input System `1.7.0`; actions in `Assets/_Prototype/Input/Resources/DD_Controls.inputactions` |
| Navigation | AI Navigation `1.1.5` (NavMesh) plus a custom crowd layer |
| Network | None. The game runs fully offline. |

---

## 1. How to run it

1. Clone the repository **with Git LFS**. Without LFS objects, models, textures and audio are pointer files.
2. Open this folder with **Unity 2022.3.22f1**, or add it in Unity Hub with *Add → Add project from disk*. Decline any upgrade prompt for another editor version.
3. **The first open takes several minutes.** `Library/` has to be built, shaders imported and the URP assets compiled. This happens once per machine; do not interrupt it.
4. Open `Assets/_Prototype/Scenes/Boot.unity` and press **Play**.

`Boot` is the only valid entry scene. It loads `Assets/_Prototype/Core/Resources/PrototypeConfig.asset`, applies the runtime settings that asset dictates, brings up the persistent services (pools, config, the development debug panel) and hands off to the first real scene (`Core/Boot/PrototypeBootstrapper.cs`). Starting from any other scene skips that hand-off, and the scene will be missing its services.

**Build scene order** (`ProjectSettings/EditorBuildSettings.asset`, enabled scenes):

```
0  Assets/_Prototype/Scenes/Boot.unity
1  Assets/_Prototype/Scenes/MainMenu.unity
2  Assets/_Prototype/Scenes/Selection.unity        Prepare Run
3  Assets/_Prototype/Scenes/Run.unity
4  Assets/_Prototype/Scenes/Results.unity
5  Assets/_Art/Scenes/World_AP005.unity           older world, kept for A/B
6  Assets/_Art/Scenes/World_V6_Greybox.unity      the game's world, loaded additively by Run
7  Assets/_Art/Scenes/World_FPTPOnly.unity        content only the legacy cameras can see
```

The flow is `Boot → MainMenu → Selection (Prepare Run) → Run → Results`. Results returns to the main menu or, with **PLAY AGAIN**, to Prepare Run with the last selection.

**Player builds** are made with the repository's tools, not from the Build Settings window: `tools/build.sh dev|release|release-probe` (see [`tools/README.md`](../../tools/README.md)).

---

## 2. Controls

Bindings are declared once, in `DD_Controls.inputactions`, and read through an abstraction (`Core/Input/IInputSource.cs`, `Input/PrototypeInputSource.cs`). Every gameplay binding can be rebound for keyboard and gamepad in **CONTROLS** (`Input/InputRemapService.cs`), and on-screen prompts follow the last used device. Movement and aim are independent: you can run one way and fire the other.

| Action | Keyboard and mouse | Gamepad |
| --- | --- | --- |
| Move | `W` `A` `S` `D` (arrow keys also bound) | Left stick |
| Aim | Mouse cursor position in the world | Right stick |
| Fire (hold for automatic) | Left mouse button | Right trigger |
| Focus (narrows the cone, slows the step) | Right mouse button | Left trigger |
| Reload | `R` | West face button (X / Square) |
| Interact (wall buy) | `E` | South face button (A / Cross) |
| Pause | `Esc` | Start / Menu |
| Debug panel (**development builds only**) | `F1` | Select / View |

- **Specials** fire with Fire while one is held. The separate *Special* action (`Q` / left shoulder) is bound but not used yet.
- **UI navigation** has its own action map: `WASD`, arrows, left stick or d-pad to move; `Enter`, `Space` or the south button to confirm; `Esc` or the east button to go back. The mouse can point and click.
- **The game pauses itself** when its window loses focus.

---

## 3. Development builds and the Editor

### 3.1 The F1 debug panel

`F1` (or the gamepad's Select button) toggles the **PROTOTYPE DEBUG** panel in development builds and the Editor. A release build never draws it (`Core/Debug/PrototypeDebugPanel.cs`), and `PrototypeConfig.System.DebugPanelEnabled` can switch it off in development builds too. The panel costs nothing while closed.

The panel lists every `[PrototypeOnly]` value it can reach, editable live, with the pending decision each value occupies. Other systems add their own controls through `DebugPanelRegistry` (`Core/Debug/DebugPanelRegistry.cs`):

| Tab | What lives there |
| --- | --- |
| **Config** | Every field of `PrototypeConfig`, live-editable mid-run |
| **Rounds** | Run, round and clock readouts; start or restart a run; skip or complete rounds; force the round-end screen open or closed; gold and XP grants |
| **Enemies** | Clear or kill all; spawn one of each tier; fill to the safety ceiling; break all armour; `Verify Damage Model`; crowd, tier-schedule and FX-budget readouts |
| **Weapons** | Equip weapons, Special controls, live fire rate and damage, camera-shake intensity, game-feel A/B, effect-pool and feel-budget readouts |
| **Economy** | Wallet, packet and reward-ledger readouts; gold grants; sweep or destroy packets; live magnet radius |
| **Spawn** | **Force Spawn to 40 (perf measurement)**, release the forced target, round jumps to tier debuts, skip to round N, spawning on/off |
| **Perf** | Pools, crowd and NavMesh readouts, crowd layer on/off, tree-system counters |
| **Candidates** | The candidate decision list. Flagging a value here changes nothing and writes nothing to `planning/`. |
| **Misc** | Player heal, damage and kill; player, camera, input and aim-device readouts; occluder readout; UI contrast audit; **Reset local profile** (destructive: it resets the real player profile; tests use an isolated profile path); `PROTOTYPE_DIRECT_TO_RUN (next boot)` |

### 3.2 Direct to run

`PrototypeBootFlags` (`Core/Boot/PrototypeBootFlags.cs`) can send the boot straight into `Run`, skipping the main menu and Prepare Run. In ascending priority:

1. the stored switch (the F1 *Misc* toggle and `PrototypeConfig.System.DirectToRun`), **development builds and the Editor only**;
2. the process-local command-line flag `-ddDirectToRun`, used by the smoke run;
3. the scripting define `PROTOTYPE_DIRECT_TO_RUN`;
4. a session override set by the panel or another system.

A release player never skips the main menu on a stored switch.

### 3.3 Command-line flags

Players read `-dd…` flags for automation. The most used:

| Flag | Effect |
| --- | --- |
| `-ddDirectToRun` | Boot straight into a run |
| `-ddDifficulty normal\|hard\|demolition` | Fix the run's difficulty; the menu's choice is then ignored |
| `-ddProfilePath <file>` | Use a sandbox profile instead of the real one (development builds) |
| `-ddTelemetry 0`, `-ddTelemetryDir <dir>` | Turn run telemetry off or redirect it |
| `-ddQaBot <dir>` (+ `-ddQaBotWeapon`, `-ddStartRound`) | Let the seeded QA bot play the menu → run flow and write a report (development builds) |
| `-ddTreePreset <name>` | Apply a run-tree preset such as `perf_worst` |
| `-ddPilotPerfProbe` (+ `-ddPilotWeapon`, `-ddPilotAutoFire`) | Forced 40-body perf windows (development and release-probe builds) |
| `-ddWorldSource V6\|AP005\|Procedural` | Choose the world (V6 is the default) |
| `-ddCameraMode topdown\|tp\|fp` | Start camera (third- and first-person are legacy) |
| `-ddRagdoll 0` | Ragdoll proxies off, for A/B |

Shipping evidence is always taken on the flagless path the player uses. The capture scripts (`tools/td-capture.sh`, `tools/mk-capture.sh`) drive the development-only capture agents.

---

## 4. Release builds

`tools/build.sh release` produces the build a player gets:

- LZ4HC-compressed data, a version stamp, an app manifest (SHA-256 per file) and a footprint report against the 5 GB cap.
- The display name **"Deaths Demolition"**, written into Info.plist after the build (`tools/app-display-name.sh`). The executable, bundle id and data paths keep the product name, so existing saves are untouched.
- No F1 panel, no stored direct-to-run switch, no QA bot and no capture agents. The one exception is `PrototypeCaptureAgent`, which the smoke run uses and which does nothing without its flag.
- The perf probe exists only in `release-probe` builds, which are never distributed.

---

## 5. Where things live

Runtime code lives under `Assets/_Prototype/`, the art pipeline and production content under `Assets/_Art/`, and the URP pipeline assets in `Assets/Settings/`. Every major system, with its folder, main types and status, is listed in [`docs/SYSTEMS-INDEX.md`](../../docs/SYSTEMS-INDEX.md). Contracts, event flows and performance rules are in [`docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md).

| Folder | Summary |
| --- | --- |
| `_Prototype/Core/` | Boot, `PrototypeConfig` and its sections, pooling, the input abstraction, camera and quality contracts, weapon definitions, the F1 panel |
| `_Prototype/Difficulty/` | Normal / Hard / Demolition, pursuit roles |
| `_Prototype/Weapons/` | Firing, hitscan, projectiles, Specials, wall buys, gun attachments, the arsenal assets |
| `_Prototype/Damage/` | Regional damage, armour, death impulse |
| `_Prototype/Enemies/`, `Navigation/`, `Spawn/` | Zombie runtime, attacks, reactions, threat director, crowd, spawn planning |
| `_Prototype/Rounds/` | Run director, round loop, round-end shop, run tree v2 (`RunTree/V2/`) |
| `_Prototype/Economy/`, `Profile/`, `Achievements/` | Gold, the crash-safe profile (schema v6), Awakening, achievements and unlocks |
| `_Prototype/Camera/`, `Player/`, `Characters/` | Top-down camera, player, hero and zombie animation, ragdoll |
| `_Prototype/Feedback/`, `Audio/`, `UI/`, `World/`, `Rendering/`, `Props/` | Game feel and VFX, audio, the brush-ink UI, world and vegetation, quality application, breakables |
| `_Prototype/Diagnostics/` | QA bots, capture agents, probes, telemetry, crash log |
| `_Prototype/Tests/` | EditMode and PlayMode suites |
| `_Art/` | Import contracts, world V6 builders, render features, art libraries, art tests |

---

## 6. PROTOTYPE-ONLY values

Every tunable value that still waits for a product decision carries a `[PrototypeOnly(...)]` attribute that names the open decision it temporarily occupies. The tag shows in the Inspector and in the F1 panel, so nobody can read a number off the screen without also reading where it came from. The census is in [`PROTOTYPE-ONLY.md`](PROTOTYPE-ONLY.md).

---

## 7. Known limitations

- **Legacy cameras.** Third- and first-person views can still be chosen from the Pause menu's CAMERA row, but they have been frozen since 6 Oct 2026 and are outside test and acceptance scope. Top-down is the developed and tested mode.
- **Developer flags in release.** A few gameplay and visual A/B flags (`-ddDifficulty`, `-ddQualityTier`, `-ddVegetationOff` …) are still read by a release player when passed on the command line. Moving them behind development guards is planned.
- **Executable name.** The executable and bundle id still contain "Playable Concept"; only Activity Monitor shows it. A rename is planned after R4.
- **Achievements.** Seven achievements read counters of the retired v1 tree and are probably not earnable under tree v2 (unverified).
- **Windows.** `tools/build.sh --target win64` builds a Windows x64 player, but no R3 or R4 Windows release was made.

The earlier per-package change log of this file (WP-000 onward) is kept in git history: `git log -p -- prototype/DeathsDemolition_PlayableConcept/README.md`.
