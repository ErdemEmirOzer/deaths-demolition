# tools/ — build, smoke, backup (WP-025)

Shared scripts for every session and for regression automation (WP-026). All paths resolve
from the script location, so each worktree uses its own project. Environment overrides:
`DD_UNITY` (editor binary), `DD_BUILD_ROOT` (output root, default
`~/Desktop/KADRO/context/projects/deaths-demolition/builds`), `DD_MAX_UNITY` (machine rule, 2),
`DD_WAIT_SECONDS` (queue timeout, 3600).

| Script | What it does | Exit codes |
|---|---|---|
| `build.sh {dev\|release} [--smoke] [--out DIR] [--no-wait]` | Takes a machine slot via `slot.sh` (≤ 2 Unity batch editors; released after the build, the smoke run takes its own), stamps `Resources/build-info.json` (git describe/commit/branch/UTC/mode/Unity — restored to the placeholder after the build), builds every enabled scene for macOS via `DD.Prototype.Core.EditorTools.DdBuild`, writes `build-report.json` + `report.json` + `timeline.txt`; `--smoke` runs `smoke.sh`. | 0 ok · 10 build failed · 20 smoke failed · 30 user-data guard failed · 64 usage · 75 busy |
| `smoke.sh <app> <outdir> [--delay S] [--mode dev\|release]` | e1 main-menu and e4 direct-to-run captures (1280×720) with exception scan; dev builds run with `-ddProfilePath <out>/profile-sandbox/…` (real profile never opened); release builds and PlayerPrefs plists cannot be redirected, so the four real user-data files (+ `.bak`/`.corrupt` side files; the live `.lock` lease is excluded) are copied before and restored + hash-verified after (`userdata.verified`). `smoke-report.json`. | 0 · 20 · 30 · 64 |
| `backup-repo.sh [--dest DIR] [--keep N] [--verify-only DIR]` | ADR-025-1/ADR-031-1: `git bundle --all` + rsync of `.git/lfs/objects` into `~/Backups/deaths-demolition/<utc>/` (two-pass: rsync repeats until source/backup object counts agree, ≤ 3 passes — parallel sessions add objects meanwhile), `git bundle verify`, LFS count check, `BACKUP.json`; keeps the newest 3 (pruning skipped when a run is not verified; a partial backup is kept). Manual; no remote/push. | 0 · 10 bundle · 11 verify · 12 LFS mismatch (partial kept) · 13 space · 64 |
| `cleanup-merged.sh [--dry-run] [--apply] [--root DIR]` | ADR-031-1: reclaims disk from finished packages — worktrees whose branch is integrated into main (tip ancestor of main, has its own commits, clean) → `git worktree remove` + prune (branches/tags kept); `wp-*/verify-build*`, `wp-*/build-*`, `wp-*/verify`, `wp-*/verify-final`, `builds/*`; orphan worktree folders. Never: evidence/, reports/, README, backup/, prompts/, manifest/, asset-production; `.app` bundles inside evidence are listed only. **Default is dry-run; `--apply` is run by C-00 with the user's approval.** | 0 · 1 removal failed · 64 |
| `post-integrate.sh <tag> [--dest DIR]` | C-00's chain after an `--ff-only` merge + tag: tag = main HEAD check → backup (two-pass, verified) → cleanup dry-run report → disk report → LFS pointers vs objects; report in `~/Backups/deaths-demolition/reports/post-integrate-<tag>.txt`. | 0 · 2 tag · 3 backup · 4 LFS · 64 |
| `disk-report.sh [--root DIR] [--json FILE]` | Workspace breakdown: worktrees (Library share), build products/verification copies, evidence, reports, backups, asset-production, `.git` (+LFS), free space and swap. | 0 · 64 |
| `slot.sh acquire\|release {unity\|blender} <tag>` / `status` | Machine slot lock (C-00 rule 2026-09-16): at most 2 Unity batch processes and 1 heavy Blender CLI on this machine; `acquire` blocks until a slot is free, the lock is bound to the calling shell's pid (`/tmp/dd-slots`). Every session calls it before a Unity batch/Blender run. | 0 |
| `dd-env.sh` | Shared variables/functions (sourced): `dd_slot_acquire`/`dd_slot_release`, `dd_unity_count`, `dd_log`. | — |

Version stamp at runtime: `DD.Prototype.Core.Build.BuildInfo.Read()` → `.Label` ("wp-017-…-g00bcde0 · release · 2026-09-16") for a menu corner, `.Describe()` for logs; an Editor session or an unstamped build reads `dev-unstamped`.

Render-texture measurement (dev build): `DeathsDemolition.app/Contents/MacOS/… -ddRtProbe <out> -ddProfilePath <out>/prototype-profile.json` → `summary.txt` + per-variant RT dumps (ADR-025-2).

Crash log: `DD.Prototype.Diagnostics.CrashLogReporter` writes `<persistentDataPath>/Logs/dd-crash-<utc>.txt` in players (Exception/Error/Assert + unhandled exceptions; home directory and account name masked; duplicates suppressed; newest 10 files kept; `-ddNoCrashLog` disables). Nothing is uploaded.

Restore from a backup: `git clone <backup>/repo.bundle <dir> && rsync -a <backup>/lfs/objects/ <dir>/.git/lfs/objects/ && git -C <dir> lfs install --local && git -C <dir> lfs checkout`.

Repository integrity tests (`ProjectIntegrityTests`, WP-025/WP-031): version stamp placeholder committed; `tools/` scripts present; `PlayerPrefs.` only in the allow-list (`UiSettings` until WP-022B, `PrototypeBootFlags.DirectToRun`); PlayMode tests run under the namespace-less `TestProfileIsolation` fixture and never re-point the profile at the real path; `Packages/manifest.json` + `packages-lock.json` pinned by hash (a version change is a user decision and updates the constants in the same commit).
