# Windows x64 portability audit: runtime C# and shipping config (DEC-089)

- **Date:** 2026-09-24 · **Base:** `main` @ `cf34180` · **Branch:** `cloud/windows-audit`
- **Method:** a static read of the runtime C#, shaders, ProjectSettings and `tools/`, done in a cloud clone. Unity cannot run there, LFS binaries are pointers only, and nothing was executed on Windows. Findings marked **(verify)** are strong expectations about how Unity/Mono behaves on Windows. The first real run should confirm them (§3).
- **Scope:** the 444 runtime `.cs` files (excluding `Tests/` and `Editor/`), 16 custom shaders, `ProjectSettings/*.asset`, and `tools/*.sh`. Game code is unchanged. This file is the only change.
- **Paths:** `A/` = `prototype/DeathsDemolition_PlayableConcept/Assets/`, `PS/` = `prototype/DeathsDemolition_PlayableConcept/ProjectSettings/`.

## 0. Summary

| Rank | Count | Theme |
|---|---|---|
| **BLOCKER** | 2 | The Windows save/lease code path has never run and has no tests that can run on Windows. There is no Windows smoke or user-data guard. |
| **HIGH** | 5 | Save replace/move has no retry for Windows sharing violations. Fullscreen keeps the windowed backbuffer. The frozen-frame blur's Y-flip was measured on Metal only. The default 1920×1080 window does not fit common Windows laptops. tr-TR culture reaches the UI numbers and the dev CLI parsing. |
| **MEDIUM** | 7 | Settings JSON is written non-atomically. Windows lock semantics and stale locks. Mouse-look sensitivity differs on Windows (raw input). PlayStation pads through the legacy Input Manager. Crash-log home masking. D3D11 shader errors do not fail the build. Audio loops after a device change. |
| **LOW** | 9 | Half precision, Alt+Enter, hardware cursor size, path length and productName, registry PlayerPrefs, Player.log location, test portability, cosmetic time separators, single-instance. |

**Checked and found clean** (no action needed):
- No `DllImport`, P/Invoke, `Mono.Posix` or `Mono.Unix` in runtime code.
- No `Process.Start` or shell calls at runtime. Only `Process.GetCurrentProcess()` is used, which works on Windows.
- No hard-coded `/Users`, `~` or `/tmp` paths in runtime code. `Path.Combine` is used throughout, and Unity accepts `/` on Windows.
- Data files read at runtime are JSON (JsonUtility and FeelRuntime's own parser). Line endings are pinned to LF in `.gitattributes`.
- Everything persisted is culture-invariant:
  - JsonUtility is culture-invariant.
  - `A/_Prototype/Core/Config/FeelRuntime.cs:651-653,797` uses InvariantCulture.
  - `A/_Prototype/Diagnostics/Qa/QaArgs.cs:42,54,76` uses InvariantCulture.
  - Telemetry file names use InvariantCulture (`RunTelemetry.cs:549`).
- **Turkish-I:** every runtime upper/lower-case call is `*Invariant`. There is no bare `ToUpper()` or `ToLower()`, and no `CurrentCultureIgnoreCase`.
- Graphics API is not pinned (`PS/ProjectSettings.asset:290` `m_BuildTargetGraphicsAPIs: []`), so Windows gets D3D11.
- Shaders use the URP/Unity macros for depth, UV and facing (`UNITY_REVERSED_Z`, `GetNormalizedScreenSpaceUV`, `IS_FRONT_VFACE`, `SampleSceneDepth`).
- `DD_GroundBlend` has 28 textures on 2 samplers, which is under the 16-sampler D3D11 limit.
- BC texture formats are shared with the macOS standalone build.
- `ExclusiveFullScreen` and `MaximizedWindow` are never used, so the FullScreenWindow-only design works the same on both platforms.

---

## 1. Findings

### BLOCKER

**B1: The save lease and atomic-replace path that Windows will actually use has never run, and its cross-process tests switch themselves off on Windows.**

- `A/_Prototype/Profile/ProfileStore.cs:806-831` documents the lease as an "fcntl POSIX record lock" and `:629-631` documents the replace as "a single rename(2)".
- On Windows, Mono maps these calls differently:
  - `FileStream.Lock` (`:862`) becomes **LockFileEx**: a mandatory lock owned by the handle, not the process.
  - `File.Replace` (`:632`, `:769`) becomes **ReplaceFileW**: not a rename, and it refuses when another handle is open without `FILE_SHARE_DELETE`.
- All the real cross-process evidence is Unix-only:
  - `A/_Prototype/Tests/EditMode/ProfileStoreFaultTests.cs:182,209` hard-code `/usr/bin/python3` and `/bin/kill`.
  - `:358-360,611,992,1031` call `Assert.Ignore` when those are missing.
  - `:953-960` uses a Python `fcntl` helper.
- **Why it blocks:** WP-009H's durability contract (lease, kill -9 mid-save, roll forward/back) has no evidence on a release platform whose file APIs work differently.
- **Fix (S-04, S-10):**
  1. Add Windows variants that need no Python:
     - The second process is a second copy of the dev Player running the portable `ProfilePersistenceProbe` (`A/_Prototype/Diagnostics/ProfilePersistenceProbe.cs`, phases A–D).
     - Add a lease phase in which instance 2 must report `SaveLocked` with "Another copy".
     - The kill is `Process.Kill()` or `taskkill /F /PID`.
  2. Replace the `Assert.Ignore` guards with a `RuntimePlatform` switch, so that Windows runs its own variant instead of skipping.
  3. Make "probe A–D + lease + kill on Windows" a release gate.

**B2: There is no Windows smoke and no user-data guard, and the release exe writes the real profile and registry.**

- `tools/build.sh:43` refuses `--smoke` for win64.
- `tools/smoke.sh` and `tools/userdata-guard.sh` are macOS-only (zsh, `~/Library/...`, plists, `defaults`, `.app/Contents/MacOS`).
- Release players have no `-ddProfilePath` (`smoke.sh` header). On Windows they will write:
  - `%USERPROFILE%\AppData\LocalLow\Deaths Demolition\Deaths Demolition - Playable Concept\` (profile, 5 settings JSON, `telemetry\`, `Logs\`)
  - `HKCU\Software\Deaths Demolition\Deaths Demolition - Playable Concept` (PlayerPrefs and Unity `Screenmanager*` keys)
- **Why it blocks:** DEC-089 makes Windows a release target, but no e1/e4 smoke, exception scan or user-data rule can run on it.
- **Fix (S-10, S-01):** the pipeline in §4.

### HIGH

**H1: Save replace, move and backup have no retry for transient Windows sharing violations.**

- Affected code in `A/_Prototype/Profile/ProfileStore.cs`:
  - `ReplacePrimary` `:619-641`
  - backup publish `:765-781`
  - `TryMove` `:790-802`
  - `PreserveCorruptFile` `:1097`
  - reads with the default `FileShare.Read`: `:397`, `:712`, `:718`, `:724`, `:742`
- On macOS, `rename(2)` ignores other open handles. On Windows, `ReplaceFileW` and `MoveFileEx` fail with ERROR_SHARING_VIOLATION (32), ERROR_ACCESS_DENIED (5), 1175 or 1176 whenever another handle is open without `FILE_SHARE_DELETE`. Common sources:
  - Defender or a third-party antivirus scanning the new `.writing` file
  - the Search indexer
  - backup or sync tools
  - a player opening the JSON in an editor
  - **the second, read-only game instance**, which reads the primary with `File.ReadAllText` (`FileShare.Read`, no Delete)
- The failure is handled safely: the recovery chain restores the old primary, including the ERROR_UNABLE_TO_MOVE_REPLACEMENT case where the primary is already gone (`:653-703`). But the save reports `Replace` failed, so **that run's progress is not saved**. That is the most likely real-world Windows save complaint.
- **Fix (S-04):**
  1. Add a bounded retry around every `File.Replace` and `File.Move`: 6 tries, 25/50/100/200/400 ms, on `IOException` or `UnauthorizedAccessException` whose `HResult & 0xFFFF` is 5, 32, 33, 1175 or 1176.
  2. Log the attempt count.
  3. Open every read through a helper with `FileShare.ReadWrite | FileShare.Delete`, so a reader never blocks the owner's replace.
  4. Keep the existing fault hooks, and add a test hook that holds a handle open during `Replace`.

**H2: The fullscreen toggle keeps the windowed backbuffer size on Windows (verify).**

- `A/_Prototype/UI/Runtime/Kit/UiDisplayMode.cs:96-110` deliberately makes a mode-only request (`Screen.fullScreenMode = FullScreenWindow`, then `Screen.fullScreen = true`). The class note at `:8-17` explains this is because Retina sizes are points and backing pixels.
- On Windows, sizes are physical pixels, and both setters keep the current resolution. A 1280×720 window becomes a 1280×720 backbuffer stretched over a 1920 or 2560 monitor, so the UI and text are blurry.
- `ToWindowed` (`:112-151`) restores the saved `WindowWidth` and `WindowHeight` (`UiSettings.cs:402-409`) without clamping them to the current monitor. A size saved on a 4K screen overflows a 1080p one.
- **Fix (S-09):**
  - Under `#if UNITY_STANDALONE_WIN` (or `Application.platform == WindowsPlayer`), go fullscreen with `Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow)`.
  - Clamp the windowed size to `Screen.currentResolution` minus a taskbar margin.
  - Keep the macOS path unchanged.
  - Add a `-ddUiCapture display` assertion that `Screen.width == Display.main.systemWidth` after fullscreen.

**H3: The frozen-frame (pause/shop) blur Y-flip was measured only on Metal (verify).**

- `A/_Prototype/UI/Runtime/Fx/UiFrozenFrame.cs:296-299`:
  - `FlipFor` ignores its `projectionFlipped` argument.
  - It flips whenever `toScreen && SystemInfo.graphicsUVStartsAtTop`. The comment reads "Measured: a screen camera on Metal needs the flip".
- D3D11 also reports `graphicsUVStartsAtTop == true`. But the pass uses the legacy `cmd.Blit(RTHandle → temporary RT)` (`:349-353`), and the orientation of the camera color target on D3D11 depends on MSAA, post-processing and render scale. The Cinematic tier (`A/Settings/DD_URP_Cinematic.asset:28`) uses MSAA 4.
- **Risk:** the blurred frame under the pause or shop screen is upside down on some D3D11 quality tiers.
- **Fix (S-07, S-09):**
  1. Verify on the first run, on all three quality tiers.
  2. If wrong, move the pass to `Blitter.BlitCameraTexture` (RTHandle-aware, handles the flip itself), or derive the flip from `projectionFlipped` per API.
  3. Add a Player capture check to `UiFxCaptureAgent` (the top-strip luminance of the frozen RT matches the live frame) so the D3D11 result is locked in by a test.

**H4: The default windowed 1920×1080 does not fit the most common Windows laptops.**

- `PS/ProjectSettings.asset:45-46` sets `defaultScreenWidth/Height` to 1920×1080.
- `:80` `defaultIsNativeResolution: 0`, and `:106` `fullscreenMode: 3` (Windowed).
- A 1920×1080 client area plus title bar is larger than the work area of a 1920×1080 panel, and 125–150 % scaling is the Windows laptop default. The title bar ends up off-screen or the window sits under the taskbar. At 1366×768 it is worse.
- macOS hides this because its sizes are in points.
- `UiSettings.ApplyStoredDisplayMode` (`A/_Prototype/UI/Runtime/Kit/UiSettings.cs:411-425`) does nothing on first launch (`DisplayMode < 0`).
- **Fix (S-01, C-00 decision):**
  - Either default to FullScreenWindow at native resolution (`fullscreenMode: 1`, `defaultIsNativeResolution: 1`),
  - or, on first launch, clamp to 80 % of `Display.main.systemHeight`.

**H5: tr-TR culture reaches the UI numbers and the dev CLI parsing.**

- No code sets `CultureInfo.CurrentCulture` or `DefaultThreadCurrentCulture` (grep: none). On Windows, Mono uses the user's regional format, so a Turkish Windows runs as **tr-TR**: `,` is the decimal separator and `.` the group separator.
- Nothing in the repo shows whether the macOS runs so far used tr-TR. Treat Windows as the first run in that locale.
- **UI (the English UI gets decimal commas):**
  - `A/_Prototype/Rounds/UpgradeCard.cs:317-403` (`{1:0.00}x`, `{1:0.#} m`)
  - `A/_Prototype/UI/Runtime/Presentation/RunHudViewModel.cs:398,404`
  - `A/_Prototype/UI/Runtime/Screens/RunHudScreen.cs:1934`
  - `A/_Prototype/UI/Runtime/Screens/SelectionScreen.cs:644-650`
  - `A/_Prototype/UI/Runtime/Screens/PauseScreen.cs:417,429`
  - `A/_Prototype/Player/PlayerDebugHooks.cs:133-181`
  - The result is "1,25x", "0,5s", "2,5 m", mixed with other spots that are already invariant.
- **Parsing (wrong values, silently):**
  - `A/_Prototype/Core/Boot/PrototypeCaptureAgent.cs:78`
  - `A/_Prototype/UI/Runtime/Flow/UiCaptureAgent.cs:274,491`
  - These call `float.TryParse(x)` with the current culture. Under tr-TR, `"2.5"` parses as **25** (dot = thousands separator), so `--delay 2.5` waits 25 s.
- **Fix (S-01, one place):**
  1. Add a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` in Core/Boot that sets `CultureInfo.DefaultThreadCurrentCulture`, `DefaultThreadCurrentUICulture` and `Thread.CurrentThread.CurrentCulture` to `InvariantCulture`.
  2. Add `InvariantCulture` to the three `TryParse` calls anyway.
  3. Add an EditMode test that formats an UpgradeCard with `CurrentCulture = tr-TR` and asserts `.`.

### MEDIUM

**M1: Settings JSON files are written non-atomically.**

- Affected writes:
  - `A/_Prototype/UI/Runtime/Kit/UiDataFiles.cs:96` (ui-settings, load-timing, ui-hints; called from `UiSettings.cs:485`, `UiDataFiles.cs:181,258`)
  - `A/_Prototype/Core/Input/InputPreferences.cs:340`
  - `A/_Prototype/Input/InputRemapService.cs:218`
- All three use `File.WriteAllText`, which truncates and then writes. A power loss, a kill, or a sharing violation from antivirus (more frequent on Windows) leaves an empty or partial file. The readers (`UiDataFiles.cs:73-84`, `InputPreferences` load) then fall back to defaults silently, and the player loses keybinds and settings.
- **Fix (S-04 helper; S-09 and S-02 adopt it):** a shared `AtomicTextFile.Write(path, text)`: write `.tmp`, `Flush(true)`, then `File.Replace` or `File.Move` with H1's retry. Optionally keep a `.bak`.

**M2: Windows lock semantics differ, and a stale lock after a crash can make the session read-only.**

- `A/_Prototype/Profile/ProfileStore.cs:859-872` treats *any* `IOException` from `Lock(0,1)` as "another copy".
- On Windows the lock is released when the dying process's handles are closed. MSDN says the timing "depends upon available system resources". A fast relaunch after a crash or a Task-Manager kill can find the lock still held, so the session starts **read-only** and that session's progress is not saved.
- Two semantics also differ, but the code already complies with both (keep it that way):
  - LockFileEx is owned by the handle: a second open-and-lock in the same process conflicts instead of dropping the lock.
  - The lock is mandatory: other handles cannot read byte 0.
- `PS/ProjectSettings.asset:93` has `forceSingleInstance: 0`.
- **Fix (S-04, S-01):**
  1. Retry `Lock` for about 1.5 s (6 × 250 ms) before going read-only.
  2. Log `HResult` (33 = ERROR_LOCK_VIOLATION versus others).
  3. Consider `forceSingleInstance: 1` for the Windows player, keeping the lease as a second line of defense (C-00 decision).

**M3: Mouse-look feel differs on Windows.**

- `A/_Prototype/Input/PrototypeInputSource.cs:428` reads the raw `Mouse.current.delta`.
- On Windows the Input System uses WM_INPUT raw counts: no OS acceleration, scaled by the mouse's DPI. On macOS the deltas are accelerated, point-based motion.
- The default `LookSensitivity = 0.08` (`A/_Prototype/Core/Input/InputPreferences.cs:54`, range 0.02–0.30) was tuned on a Mac trackpad and mouse. It will feel noticeably different on Windows, and 0.30 may not be enough for low-DPI mice.
- **Fix (S-02):**
  - Calibrate on Windows with an 800 DPI and a 1600 DPI mouse.
  - Add a per-platform default, or a scale factor applied before `LookSensitivity`.
  - Never rewrite a value the player has saved.

**M4: PlayStation pads through the legacy Input Manager map Cancel to Cross on Windows.**

- `A/_Prototype/UI/Runtime/Kit/UiInput.cs:39,50,76,87,144-146` read the legacy `Cancel` and `Submit` buttons. Those are `joystick button 1` and `joystick button 0` (`PS/InputManager.asset:255,287`).
- Xbox pads through XInput map correctly (0 = A, 1 = B).
- DualShock 4 and DualSense through DirectInput on Windows map 0 = Square and 1 = **Cross**. So Cross cancels menus and Square confirms.
- (On macOS an Xbox pad is button 16/17, so this path was probably never exercised with a gamepad.)
- **Fix (S-09, S-02):** read UI Cancel and Submit from the Input System (`Gamepad.current.buttonEast` / `buttonSouth`, or the UI action map), and remove the `joystick button` alternatives from the legacy axes.

**M5: Crash-log home-directory masking misses Windows spellings.**

- `A/_Prototype/Diagnostics/CrashLogReporter.cs:241-265,291-305`:
  - The home replace is Ordinal, so it is case-sensitive.
  - Unity reports Windows paths with forward slashes (`C:/Users/X/AppData/LocalLow/...`). Those are masked only through the `"/Users/" + UserName` rule.
- That rule fails when the profile folder name is not `Environment.UserName`: a renamed account, a Microsoft account with a truncated folder name, or a domain account.
- `HOME` may also be set by Git Bash (`/c/Users/X`) when a QA script launches the game.
- **Fix (S-10):**
  - Mask `GetFolderPath(UserProfile)` in its `\`, `/` and `/c/...` spellings.
  - Compare with `OrdinalIgnoreCase`.
  - Add Windows cases to the existing `Mask` test.

**M6: D3D11 shader compile errors do not fail the build.**

- The win64 build "compiles", but Unity only logs D3D11 shader errors and still produces a player, with pink materials.
- 16 custom shaders are affected: `A/_Art/Shaders/*` (9), `A/_Art/VFX/Resources/Shaders/*` (2, including the new `DD_VfxRibbon`), `A/_Prototype/UI/**/Shaders/*` (5).
- Note that `DD_TierLit.shader:254-258` uses `target 4.5` and `exclude_renderers gles3 glcore`, which is fine on D3D11 (SM5).
- **Fix (S-01, S-07):**
  - `build.sh --target win64`: fail on `Shader error in` in the build log, and list `Shader warning in` lines for DD shaders.
  - Visual check in §3.

**M7: Audio loops stay silent after a device change.**

- `A/_Prototype/Audio/Runtime/PrototypeAudioDirector.cs:694-697` only counts `OnAudioConfigurationChanged`.
- The score and beds heal themselves (`A/_Prototype/Audio/Runtime/MusicDirector.cs:185-205`). But active weapon, Special and field loops that were stopped by the reset are not restarted.
- Default-device switches are far more common on Windows: plugging in a headset, a Bluetooth reconnect, an HDMI monitor waking up.
- Sample rate is fine: `PS/AudioManager.asset` has `m_SampleRate: 0` (device rate, usually 48 kHz on Windows). The synth clips at 44.1 kHz (`SynthOps.cs:40`, `UiSound.cs:60`) are resampled by Unity, and `AudioOutputCapture` reads `outputSampleRate`.
- **Fix (S-08):** when `deviceWasChanged`, restart the loops the director believes are active, and add a test that fakes the event.

### LOW

**L1: Half precision.** Metal uses real 16-bit `half`. D3D11 promotes `half` to `float`, so Windows gets more precision. Banding and overflow tuning in the UI shaders (`DD_UiFrozenFrame.shader`, `DD_UiFx.shader`) may look slightly different. Include in the visual comparison; no code change expected.

**L2: Alt+Enter.**
- `PS/ProjectSettings.asset:105` `allowFullscreenSwitch: 1` lets Windows' Alt+Enter bypass `UiSettings`. The live row reads `Screen.fullScreenMode`, which is fine, but the saved `DisplayMode` goes stale and the next launch reverts.
- **Fix (S-09):** persist the setting when the mode changes externally, or turn the switch off.

**L3: Hardware cursor size.**
- `A/_Prototype/UI/Runtime/Kit/UiCursor.cs:96-105,148` uses `CursorMode.Auto`. At 150–200 % scaling the arrow is 31 px plus its outline. Check that Windows keeps it as a hardware cursor and neither clips nor falls back.
- The sizing math is already covered by `UiCursorSizeTests.cs:17-20`.

**L4: Path length and product name.**
- The deepest file is `…\AppData\LocalLow\Deaths Demolition\Deaths Demolition - Playable Concept\prototype-profile.json.corrupt.<ticks>.json`, about 150 characters plus the user name. That is under MAX_PATH, and there are no invalid characters.
- Non-ASCII user names (Ö, Ş, İ) are fine through Mono's wide-character APIs. Test one (§3).
- Renaming `productName` (`PS/ProjectSettings.asset:16`) for release moves both the save folder and the registry key. A C-00 decision is needed, plus a migration plan.

**L5: PlayerPrefs live in the registry.** `A/_Prototype/Core/Boot/PrototypeBootFlags.cs:21,70-75` (`DD.Prototype.DirectToRun`) and Unity's `Screenmanager*` keys live in `HKCU\Software\Deaths Demolition\<product>`. The Windows guard must use `reg export` and, to restore exactly, `reg delete` plus `reg import`.

**L6: Player.log location.** On Windows, `Player.log` and `Player-prev.log` live **inside** `persistentDataPath`; on macOS they are in `~/Library/Logs`. The Windows user-data inventory must exclude them, along with `.lock`. Crash dumps go to `%TEMP%\Deaths Demolition\<product>\Crashes`.

**L7: Test portability.**
- The source-scan tests already normalize `\` to `/` (for example `ProjectIntegrityTests.cs:227`, `InputParityContractTests.cs:157`).
- `ProfileApplyOnceTests.cs:106` compares mtimes; NTFS has 100 ns resolution, which is fine.
- `UiDataPaths.ProcessLocalRoot` (`UiDataFiles.cs:56`) is portable.
- The only non-portable suite is B1's.

**L8: Cosmetic time separators.** `CrashLogReporter.cs:75,155` use `DateTime.ToString` custom formats without InvariantCulture. The `:` in `HH:mm:ss` follows the culture's time separator. tr-TR uses `:`, so this is harmless there. The H5 global fix covers it.

**L9: Dev CLI parsing.** `A/_Prototype/Audio/Runtime/AudioDense40EvidenceDriver.cs:56` and `A/_Art/Runtime/*Bootstrap.cs:304,317` call `int.TryParse` with the current culture. Integers are safe under tr-TR. Listed for completeness.

---

## 2. What does not change between macOS and Windows

These behave the same, so no Windows-specific work is needed:
- `File.Move` onto an existing file throws on both.
- NTFS and APFS are both case-insensitive by default.
- `Directory.GetFiles` results are sorted Ordinal before use (`CrashLogReporter.cs:220`).
- The `.lock` file is never deleted.
- The candidate→backup→replace ordering and all the recovery branches (`ProfileStore.cs:196-340`) do not depend on the OS.
- `Flush(true)` becomes `FlushFileBuffers` on Windows, which gives the same durability as `fsync`. NTFS journals metadata, so the missing directory fsync matters less than on APFS.

## 3. Windows QA checklist for the first real run

Test environment:
- Windows 11 x64, a discrete GPU, and one laptop with integrated graphics.
- Defender left on.
- Regional format set to Turkish (`Set-Culture tr-TR`) for one pass and English (US) for another.
- A user name containing non-ASCII letters for one pass.

Steps:
1. **Boot:**
   - Unzip the lz4hc build and run the exe (expect a SmartScreen prompt if unsigned).
   - Check `Player.log` for `Exception`, `Shader error` and `DllNotFoundException`.
   - Confirm the D3D11 device line.
2. **Display:**
   - At 100 %, 125 % and 150 % scaling, check where the first-launch window lands (H4).
   - Toggle Settings DISPLAY: fullscreen must be sharp at native resolution (H2).
   - Back to windowed: the size must be restored and fit the screen.
   - Try Alt+Enter and Alt+Tab (L2).
   - On a second monitor, go fullscreen there.
3. **Cursor and input:**
   - FP/TP look: the cursor locks. Alt+Tab releases it; clicking back re-locks it; the cursor never gets trapped.
   - Cursor size at 150 %.
   - Mouse sensitivity with an 800 DPI and a 1600 DPI mouse (M3).
   - Xbox pad (XInput), DualSense over USB and Bluetooth: menu Submit/Cancel (M4), glyph names, rumble.
   - AZERTY or Turkish-Q keyboard: V (shoulder swap) is by physical key; remap glyphs show readable characters.
4. **Frozen frame:** open pause and the shop on the Performance, High and Cinematic tiers. The blurred background must be upright (H3).
5. **Numbers under tr-TR:** check upgrade cards, HUD "recovery in", selection DMG and fire rate, and the pause shake value. Record whether decimal commas appear (H5).
6. **Profile:**
   - Finish a run, quit, relaunch: progress is kept.
   - Open a second copy: the "Another copy" banner appears, and the first copy still saves.
   - Kill the game from Task Manager during the results save, then relaunch immediately. Expect no "could not be locked" banner and a valid profile (M2).
   - With `prototype-profile.json` open in Notepad++, finish a run and check whether the save survives (H1).
7. **Settings:** change volume, keybinds and hints; kill the process; relaunch. Settings are kept, not reset to defaults (M1).
8. **Audio:** plug in or out a USB or Bluetooth headset during a Special weapon loop. The score resumes and the loops resume (M7).
9. **User data:** list `%USERPROFILE%\AppData\LocalLow\Deaths Demolition\…` and `HKCU\Software\Deaths Demolition\…` before and after. Only expected files and keys may change (L5, L6).

## 4. Windows build and smoke pipeline: what it needs (not implemented)

Everything in `tools/*.sh` is zsh on macOS: `/Applications/.../Unity.app`, `pgrep`, `sysctl`, `shasum`, `defaults`, plists, `~/Library`, `.app/Contents/MacOS`. A Windows pipeline needs the following:

1. **Runner:** a Windows 10/11 x64 machine or VM with a real GPU. D3D11 WARP works but is too slow for perf probes; use `-force-d3d11` explicitly. Install git with `core.autocrlf=false` (`.gitattributes` already forces LF), Python 3 with `PYTHONUTF8=1` (the Turkish console code pages cp1254/cp857 break `qa/*.py` output), and PowerShell 7.
2. **Build:** keep cross-building on macOS (`build.sh --target win64`, which already works). Add:
   - Fail the build on `Shader error in` (M6).
   - Zip the output, compute SHA-256, and transfer it.
   - Optionally, a Windows Unity 2022.3.22f1 editor for native EditMode/PlayMode runs, so the B1 variants run under a Windows Editor too.
3. **`dd-env.ps1`:** Unity.exe path, build root, product and company names, and the Windows data paths from L4/L5/L6. Replace `dd_unity_count` and the slot logic with `Get-Process Unity`, and memory checks with `Get-Counter`/CIM instead of `sysctl`.
4. **`smoke.ps1` (e1/e4):**
   - Launch `DeathsDemolition.exe -logFile <out>\player.log -screen-width 1280 -screen-height 720 -screen-fullscreen 0 …`. The Windows player writes no stdout.
   - Wait with a timeout, then `Stop-Process -Force`.
   - Take the exit code from `$p.ExitCode`.
   - Captures come from the in-game agents (`-ddUiCapture`); there is no `screencapture`.
   - Seed e4's `DirectToRun` through the registry (`reg add HKCU\Software\Deaths Demolition\<product> /v DD.Prototype.DirectToRun_h<hash> …`), or better through a command-line flag in dev builds.
5. **`userdata-guard.ps1`:** snapshot and restore of:
   - `…\LocalLow\Deaths Demolition\<product>\`: profile, `.bak`, `.corrupt.*`, the 5 settings JSON files, `telemetry\`, `Logs\`
   - the registry key (`reg export`, then `reg delete` + `reg import` to restore exactly)
   - excluding `*.lock`, `Player.log`, `Player-prev.log`
   - verified by SHA-256 (`Get-FileHash`); files the run created are moved into `<out>\created\`, never deleted.
6. **Profile evidence (B1):** drive `ProfilePersistenceProbe` phases A–D across real process launches, a two-instance lease phase, and a kill during save (`Stop-Process -Force` or `taskkill /F`), then relaunch immediately (M2).
7. **Tests:** a `test.ps1` watchdog using `taskkill /T /F` in place of `pkill -P`/`kill -9`. `ci_summary.py` and `test-durations.py` should be portable once UTF-8 is forced.
8. **QA gates:** port `qa/window-modes.sh` (the display gate, which now matters more per H2 and H4), `qa/regression.sh`, `qa/rc.sh` and `qa/economy-acceptance.sh` to PowerShell wrappers around the same Player flags. Keep the Python aggregators.
9. **Test matrix:** two locales (tr-TR, en-US) × two DPI settings (100 %, 150 %) × one GPU tier minimum. Run the perf probe (`PilotPerfProbe`) on Windows for its own budget, since the macOS numbers do not carry over.
10. **Release extras:** Authenticode signing (signtool) to avoid SmartScreen, plus a pass with Defender real-time protection on.

## 5. Estimated work by owner area

| Owner | Items | Estimate |
|---|---|---|
| **S-04** economy / profile | B1 test variants (with S-10) · H1 retry and share-delete reads · M1 `AtomicTextFile` helper · M2 lock retry | 3–4 d |
| **S-10** QA / tools | B2 and §4 (`dd-env`, smoke, user-data guard, probe driver, test watchdog, window-modes) · M5 masking · runner setup | 5–7 d |
| **S-01** build / core | H4 player defaults · H5 global invariant culture · M6 shader-error gate · single-instance setting | 1–1.5 d |
| **S-09** UI | H2 Windows display path and clamp · H3 orientation check (with S-07) · M4 UI Cancel/Submit · L2 Alt+Enter | 2–3 d |
| **S-07** VFX / render | H3 blur flip on D3D11 · M6 log review · L1 visual comparison | 1–2 d |
| **S-02** input | M3 sensitivity calibration · M4 (with S-09) · gamepad and keyboard-layout matrix | 1–1.5 d |
| **S-08** audio | M7 loop recovery on device change and its test | 0.5–1 d |
| **C-00** | Decisions: first-launch display mode, forceSingleInstance, culture policy, release productName and save-folder migration | 0.5 d |
| **Total** | | **≈ 14–20 person-days**, plus one to two days of first-run triage on real hardware |

Order: first B2, then B1, then H1 (save safety). Next the S-01 settings (H4, H5, M6), which take under a day and remove the most noise from the first run. Then run §3 and, before any fix work, confirm or dismiss H2, H3 and M2 there.
