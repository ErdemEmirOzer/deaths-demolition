# Marketing capture (mk-capture)

WP-110 (S-01, C-00 11.10; S-11's plan §9). QA bots play the game. A dev-only capture agent keeps the highlight moments as
clips and stills in Instagram formats: 9:16 1080×1920, 3:4 1080×1440, 4:5 1080×1350 and 1:1 1080×1080, with sound.
The tool is `tools/mk-capture.sh`. The shoot itself is S-11's, at the end of R4, after the R4 FINAL.

## Run it

```bash
zsh tools/build.sh dev --out "$K/wp-110/builds/dev-cur"
export DD_GUARD_KNOWN_GOOD="…"   # R4 rule 1: the value in K/c-00/R3/KARAR-GUARD-KG-10.10.md; the snapshot must say kgSource=env
zsh tools/mk-capture.sh --app "$K/wp-110/builds/dev-cur/DeathsDemolition.app" --wp wp-110 --label shoot-1 --segments segments.txt
```

Options: `--raw-gb 5` (raw bytes one segment may write, see Disk), `--ring-mb 640` (the preroll ring), `--hud-safe` (the
HUD streams lay the HUD out inside the Reels safe rectangle x 0–960, y 250–1520 of 1080×1920 — S-11), `--formats`,
`--window 960x540`, `--tier sinematik`, `--keep-frames`.

`segments.txt` holds one segment per line, columns separated by whitespace. A `-` means the default; `#` starts a comment.

```
# name weapon start rounds life     specials camera hud  seconds clips music seed [threshold [mode [tree]]]
a1     Rl1    15    30     immortal 0        G      both 600     6     off   26   12        demolition barrage_r15
c1     Sg2    1     -      mortal   0        O      off  300     -     on    73
```

| column | meaning (default) |
|---|---|
| name | `[a-z0-9-]+`, the segment's folder |
| weapon | a `ClassicWeaponId` name or number, e.g. `Rl1`, `Lmg1`, `9` (`AssaultRifle`); passed as QaRunBot `-ddQaBotWeapon` |
| start | the first round, 1..50 (1) |
| rounds | rounds the bot plays, 1..50 (30) |
| life | `immortal` (the player cannot die) or `mortal` (immortal) |
| specials | `1`: the bot grants itself Special Weapons near a crowd (0) |
| camera | `Y` near · `O` middle · `G` the game's distance (O) |
| hud | `off` · `on` · `both`: both writes a HUD-less and a HUD version of every clip (off) |
| seconds | the session cap, 60..7200 (600) |
| clips | how many clips to keep (12) |
| music | `off` mutes the mixer's Music group, so SFX, ambience and UI are recorded (on) |
| seed | the bot's seed (26) |
| threshold | the heat that starts a clip (10) |
| mode | `normal` · `hard` · `demolition`, passed as `-ddDifficulty` (WP-102); always passed, `normal` included (normal). `summary.json` `difficulty` is the mode the run actually played |

| tree | a WP-104 upgrade-tree preset, passed as `-ddTreePreset` (dev builds): the game turns the new run tree on and grants the preset's nodes at run start (none). `summary.json` `tree` records what applied; a preset that did not apply (an unknown name is ignored by the game) fails the segment (exit 1) |

The presets (`Rounds/RunTree/V2/TreePresetsV2.cs`, main 17f29c5d) are made for one weapon and round; give the segment the
same weapon and start round. Until WP-104 B2 lands, the preset's nodes are owned (`summary.json` `tree`) but the weapons
do not read them yet (dry-4 12.10: `barrage_r15` applied 18/18, `RocketBurst` still 0).

| tree | weapon | start | what it shows |
|---|---|---|---|
| `barrage_r15` | `Rl1` | 15 | rocket launcher, full auto, twin, triple / fan, explosive bore — rocket bursts (`RocketBurst` ≥ 3 rockets in 1 s) once WP-104 B2 wires the tree into the fire path |
| `element_r15` | `AssaultRifle` | 15 | fire / ice / fusion, chain and hole |
| `shot_r10` | `CombatShotgun` | 10 | heavy shot, bore, ricochet, full auto |
| `demolition_r4` | `AssaultRifle` | 4 | an early build for Demolition (mode `demolition`) |
| `perf_worst` | `Rl1` | 15 | the perf gate's worst build — not a marketing look |

## What happens

1. **One user-data guard window** (`--scope player`) covers all segments. One Player runs per segment, in a window (default
   960×540). The capture is off-screen, so the window only shows a preview.
2. **The master.** The TopDown camera renders into a 1080×1920 target.
   - The vertical field of view is 68.6°, so the master's horizontal angle equals the game's own 42°. A 1:1 crop therefore
     frames the scene like the game.
   - The camera keeps its angle (north stays up). Y/O/G only changes the distance, 0.6 / 0.8 / 1.0.
   - The camera leans up to 3 m toward the centre of the nearby crowd, leaving look room on the threat's side.
   - The "Development Build" watermark is drawn on the window, never into the master.
3. **Clips.** Every frame is JPEG-encoded into a preroll ring (24 s asked; the ring's memory, `--ring-mb` 640, holds
   ≈ 18 s of `hud both` frame pairs at 30 fps and ≈ 35 s HUD-less — a 1080×1920 pair is ≈ 1.2 MiB). Nothing is written
   until a clip starts.
   - Events add heat: kills, multi-kills (a marketing beat from 6 bodies), explosions, rocket bursts (≥ 3 rockets in 1 s),
     Special Weapons, an Elite or Ultra coming within 18 m, the low-health escape and the last zombie.
   - When the heat of the last 4 s reaches the threshold, a clip starts 24 s back. It ends 8 s after the trigger, and every
     strong event pushes the end further, to at most 36 s raw.
   - Stills: the trigger moment (0.35 s later, once the effect has bloomed) and new heat peaks, at most 2 per clip.
   - A clip carries the events of its preroll too: `killsInClip`, its events and `triggerKind` (the kind that weighed most
     in the 4 s burst that started it).
   - **Body counts** (S-11 12.10): `maxGroupKills` is the largest single pull or blast in the clip ("one rocket, N zombies"),
     `maxChainKills` the largest 0.6 s chain. Use these, not the game's `MultiKill` events: the game raises one only when a
     pull or chain reaches a new tier, so its count is 3, 5 or 8 (the bodies at that moment), never the blast's total. The
     multi-kill beat is counted from the kills: when a burst reaches 6 bodies it adds 1.5 × bodies, every further body
     1.5 more (event detail `burst N`); the game's own event (detail `game tier T at N`) then weighs nothing.
4. **HUD.** `on` and `both` move the screen-space canvases to a private UI camera at the master's size. It draws them over
   black and over white; the difference gives the coverage, and the HUD is composited onto the game frame. The portrait
   HUD layout is the game's own CanvasScaler at 1080×1920. Check it in the dry run before using HUD clips.
5. **Mux.** After each segment's Player, `tools/mk-capture/mk_clips.py` runs on that segment.
   - Each clip becomes four MP4s (H.264 30 fps, AAC 256 kbit/s), all centre crops of the master; nothing is scaled up.
   - The sound is laid second by second on the WAV anchors (`td-capture/align_audio.py`). `avError` > 0.1 s means the clip
     is not a candidate.
   - Stills are written as PNG and JPEG q92 per format.
   - A clip's raw JPEG frames are removed once all of its MP4s have probed. A failed clip keeps them; `--keep-frames` keeps
     them always.

## Output: `K/<wp>/capture/<label>/`

| Path | Content |
|---|---|
| `<segment>/mp4/<segment>-NN-<WxH>[-hud].mp4` | the clips, four formats each (and the HUD versions) |
| `<segment>/stills/<segment>-NN-sK-t<t>-<WxH>[-hud].png/.jpg` | stills per format |
| `<segment>/clips.json` | per clip: trigger kind and time, round, weapon, kills in the clip, crowd within 20 m and alive at the trigger, camera, look offset, fps, `avError`, files, the segment's settings |
| `<segment>/clips/NN/clip.json` | the agent's record of one clip (times, peak, events) |
| `<segment>/summary.json`, `events.tsv` | the session: stop reason, every event with its weight and heat, frames, ring, orientation |
| `capture.json`, `SHA256SUMS` | build identity, segments, guard result, file hashes (raw frames and the WAV excluded) |

The label folder is never overwritten. Pick a new `--label` for a retake.

## Notes

- **Machine:**
  - Run it in a quiet window. The capture grabs in real time; on a loaded machine the clip fps drops (`fps` in clips.json).
  - The Player needs the `player` slot.
  - The script stops between segments when free disk drops below 20 GB (exit 75); the segments already done stay.
- **Disk:**
  - Raw frames exist only until their clip is muxed: ≈ 36 MB per second of clip with `hud both` (≈ 1.3 GB per 36 s clip).
  - Every segment gets a raw budget: `--raw-gb` (default 5), never more than free disk − 20 GB. The agent stops the
    segment when its clip frames and stills reach it (`stopReason` `raw-budget`); the script refuses a segment with < 1 GB
    of budget (exit 74). A clip the agent dropped (< 5 s) has its frames removed as well.
  - The budget can be passed by up to one preroll ring (≤ `--ring-mb`): a clip's preroll frames are written when the clip
    opens. Measured with `--raw-gb 1`: 1065 MB (dry-3) and 1158 MB (dry-4). The 20 GB floor covers it.
  - The WAV covers the whole session: about 11.5 MB per minute.
- **Not covered by this tool:**
  - The loading screen and menu: use `td-capture.sh --video-from boot`.
  - UI screens: use S-09's `-ddUiCapture` and cine-player.
  - Slow motion: neither `timeScale` nor a 60 → 30 fps retime is done by this tool (tdmux lays frames on their real
    timestamps). Keep the frames (`--keep-frames`) and retime in the edit.
  - A route tour (RouteTour B-roll, S-11 P3): not a segment type yet; the director keeps clips on events only.

Exit codes:

| code | meaning |
|---|---|
| 0 | done |
| 1 | a segment wrote no session, or a clip did not mux |
| 3 | not a dev build |
| 30 / 31 / 65 / 75 | guard (restore 32 is accepted only via `qa/oos_rule.py`) |
| 64 | usage, `DD_GUARD_KNOWN_GOOD` not exported, or the snapshot without `kgSource=env` |
| 74 | disk below 20 GB (or a raw budget below 1 GB) before a segment |
| 128+n | signal (the restore still runs) |
