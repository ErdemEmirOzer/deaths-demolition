# TD capture — the R3 visual-evidence standard

WP-099 (S-01, C-00 07.10). Every R3 package that changes what the player sees attaches the same evidence:
8 fixed TopDown stills and one TopDown gameplay MP4, taken by `tools/td-capture.sh` from a **dev** build.

## Run it

```bash
zsh tools/build.sh dev --out "$K/wp-0NN/builds/dev-<label>"
zsh tools/td-capture.sh --app "$K/wp-0NN/builds/dev-<label>/DeathsDemolition.app" --wp wp-0NN --label after-1
```

- Takes about 4–6 minutes: one Player for the stills, one for the video.
- The script opens the user-data guard window itself (`--scope player`; it holds the player slot), so do not wrap it in another guard.
- Release builds carry no capture agent; the script exits 3.
- Use the defaults (seed 26, 1920×1080, `sinematik`, the default pose file) for before/after comparisons. Change a flag only when the package says why.

## What you get — `K/<wp>/capture/<label>/`

| Path | Content |
|---|---|
| `stills/01-…png` … `08-…png` | the 8 poses: plaza · lamps · gate · hall interior · water · path · grass · 40-zombie crowd |
| `stills/contact.png` | all 8 on one labelled sheet — put this in the delivery note |
| `stills/poses.json` | per pose: where the player stood, status, banner wait; crowd block (alive / within 15 m / reached) |
| `video/td-<label>.mp4` | 45 s (10–120 with `--video-seconds`), H.264 + AAC, constant 30 fps, the QA bot playing |
| `video/probe.json` | codecs, size, fps, both track durations |
| `capture.json`, `SHA256SUMS` | build identity (`build.stamp`: git describe/commit of the build, `build.head`), seed, pose-file hash, guard result, `videoQuality`, file hashes (raw frames excluded) |

The label folder is never overwritten. Pick a new `--label` for a retake.

## Notes

- **Spawns:** enemy spawning is held for poses 1–7, so a before/after compares world, light and characters. Pose 8 (`crowd40`) releases spawning and forces the 40-body ceiling.
  - If fewer than 38 zombies arrive within 15 m, the script exits 1 (`crowdShort`) but keeps the frames. Retake with a longer `--crowd-wait` (default 45 s; 20 s filled 22 of 40 on the plaza in smoke-2).
- **Pose file:** one `key x z [crowd40]` per line, in TopDown map coordinates (+Z north). Crowd poses go last.
  - A pose with no NavMesh within 2 m is recorded as `navmesh-miss` and the script exits 1.
- **Audio/video sync:** the video frames are grabbed in real time and timestamped (the game is not slowed). The muxer lays them on a 30 fps grid.
  - `td-capture/align_audio.py` lays the WAV second by second on the anchors in `video/audio-sync.tsv`, so the sound is exact at every anchor. It writes `video/audio-aligned.wav` and `align.json`.
  - The game's audio clock drifts from real time even on a quiet machine (−0.23 s over 60 s), and the WAV has gaps while no audio listener mixes (scene swaps). With a single offset the sound would slide for the rest of the clip.
  - Without anchors (an older agent), the single `sync.json` offset is used and the timeline says so.
- **Video quality:** `capture.json` → `videoQuality.verdict` is `ok`, `low-fps`, `av-drift` or `low-fps+av-drift`. The script prints a warning, and still exits 0.
  - `low-fps`: effective capture fps is below 24, so the Player ran slow on a loaded machine and the MP4 stutters.
  - `av-drift`: sound and picture are more than 0.1 s apart somewhere in the MP4 (`videoQuality.avError` = `align.json` `maxSecondErr`, the worst second). The raw audio clock is in `videoQuality.avDrift`.
  - A boot clip is judged per phase (C-00 08.10). `videoQuality.phases` lists loading / menu / selection / fighting, each with `fps`, `longestFrame` (s) and `avError`.
    - The verdict comes from the menu and fighting phases only; the loading screen's hitches are a product measurement, not a capture fault.
    - A boot clip that never reaches the menu or the run gets `phase-missing` and exits 1.
    - The script prints one line with every phase's numbers.
  - Both mean: retake on a quieter machine (`--video-only`, new label), and don't judge sound against picture.
  - `summary.json`'s `audio.driftSeconds` is read after the frame drain and is misleading; use `videoQuality.avDrift`.
- **HUD-less frames:** `--hud off` hides the in-run HUD in stills and video (for TopDown background plates). The dev watermark stays.
- **Scenario video (R4, S-06 / S-01 11.10):** `--scenario <name>` makes the video's QA bot play a named QaRunBot scenario
  instead of the free fight (`-ddQaBotScenario <name>`, e.g. `crowdwalk`: the hero walks through a 40-zombie crowd under a
  tree crown, R4 kit D2). The name is `[a-z0-9][a-z0-9-]*`; it needs a fighting video (not `--stills-only` or
  `--video-from boot`), otherwise exit 64. `capture.json` → `scenario` records the name asked for (null = free fight); the
  bot's own output confirms that the build knows the scenario.
- **Loading screen / menu clip:** `--video-only --video-from boot --video-seconds 30` records from Player start: the loading screen (intro → loop → exit), the main menu logo intro, then the first seconds of the run.
  - `capture.json` → `video.phases` holds `loadingStart`, `loadingEnd`, `menu`, `fighting` (real seconds from the MP4's t0; null when not seen).
  - S-09's still mode `-ddUiCapture loading` is separate.
- **Sound is required:** an MP4 without an AAC track (the WAV did not arm) exits 1. The file is kept so you can look at it.
- **Size:** the window is limited by the screen. On this Mac `--size 1920x1080` gives 1496×842 (Retina logical size); `probe.json` holds the real size.
- **Watermark:** dev builds draw "Development Build" bottom right. That is expected, the same in before and after.
- **Tools:** nothing is installed. The two Swift helpers compile with the Command Line Tools' `swiftc` into `$TMPDIR/dd-td-capture/`.

Exit codes: 0 done · 1 launch failed or refused / NavMesh miss / crowd short / MP4 without sound / boot phase missing · 3 not a dev build · 30/31/65/75 guard · 32 restore not accepted by `qa/oos_rule.py` · 64 usage · 128+n signal (the restore still runs).
