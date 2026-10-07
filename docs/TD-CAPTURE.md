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
| `capture.json`, `SHA256SUMS` | build identity, seed, pose-file hash, guard result, file hashes (raw frames excluded) |

The label folder is never overwritten. Pick a new `--label` for a retake.

## Notes

- **Spawns:** enemy spawning is held for poses 1–7, so a before/after compares world, light and characters. Pose 8 (`crowd40`) releases spawning and forces the 40-body ceiling.
  - If fewer than 38 zombies arrive within 15 m, the script exits 1 (`crowdShort`) but keeps the frames. Retake with a longer `--crowd-wait` (default 45 s; 20 s filled 22 of 40 on the plaza in smoke-2).
- **Pose file:** one `key x z [crowd40]` per line, in TopDown map coordinates (+Z north). Crowd poses go last.
  - A pose with no NavMesh within 2 m is recorded as `navmesh-miss` and the script exits 1.
- **Audio/video sync:** the video frames are grabbed in real time and timestamped (the game is not slowed). The muxer lays them on a 30 fps grid and aligns the WAV with `sync.json`'s offset.
  - A drift above 0.05 s is printed as a warning.
- **Tools:** nothing is installed. The two Swift helpers compile with the Command Line Tools' `swiftc` into `$TMPDIR/dd-td-capture/`.

Exit codes: 0 done · 1 launch failed / NavMesh miss / crowd short · 3 not a dev build · 30/31/65/75 guard · 32 restore not accepted by `qa/oos_rule.py` · 64 usage · 128+n signal (the restore still runs).
