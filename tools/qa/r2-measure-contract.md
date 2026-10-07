# R2 measurement contract (WP-091, C-00 05.10)

How an owner's R2 measurement test hands its result to the acceptance chain. The thresholds live in
`tools/qa/r2-gates.json` (approved: `K/wp-078/R2-KABUL-TASLAK.md`); the judge is `tools/qa/r2_gates.py`, bound into
`wp078_report.py` as the `R2` step of an R2 manifest (`wp078_accept.py --round R2`).

## What the test writes

One JSON file per gate, written only when the measurement actually ran:

```json
{"schema": "dd-r2-measure-v1", "gate": "L1", "commit": "<40-hex commit under test>",
 "source": "DD.Art.Tests.PlayMode.AnimationLocomotionR2Tests.Ayak_Kaymasi_8Yon",
 "values": {"avg": 0.031, "p95": 0.074}, "samples": 412}
```

- File name `r2-<gate id>.json`, in the folder named by the environment variable **`DD_R2_MEASURE_DIR`**. Without
  that variable the test still asserts its threshold but writes nothing (ordinary CI runs).
- Write it with the shared writer **`R2Olcum.Yaz(gate, source, values, samples)`**
  (`Assets/_Prototype/Tests/Shared/R2Olcum.cs`, S-02; shared by the EditMode and PlayMode test assemblies) — one writer for every owner, no copies.
- `commit` comes from **`DD_R2_COMMIT`** (the runner sets it to the FINAL SHA). A file with another commit FAILs.
- `values` carries exactly the keys listed for that gate in `r2-gates.json` (`checks[].key` and `report[]`), as plain
  numbers (no NaN/Infinity, no strings). A missing key is BLOCKED; a value outside its check is FAIL.
- The test itself should also fail on its threshold (so CI catches a regression early); the artifact is what the
  acceptance report re-judges, so both must use the contract numbers.
- **The source must be in the recording** (S-08 05.10, after S1 0.17 was measured on a weapon stem that held no
  weapon): before it writes, the test asserts that what it measures is actually there — a sound above a floor in its
  window (S-08: shot-window peak median >= -30 dBFS), the objects counted on screen, the frames rendered. If it is
  not, the test FAILS and writes no artifact, so the gate is BLOCKED by name; an empty capture never becomes a
  plausible number. The judge holds the same line: an artifact whose `samples` is 0 or not a positive number is BLOCKED
  ("nothing was measured" — S-10 06.10, after an S4 run with no weapon wrote placeholders that passed every check). The
  chain's own P2 and P5 count their sound windows and R2 sessions.
- Tests that measure are `[Explicit]` + `[Category("Diagnostics")]` when they are slow or need a quiet machine (Y6);
  the acceptance chain runs them by name.

## Required test identities

Some gates (`tests` in `r2-gates.json`, e.g. Z1, Z3b, H4, H4a, H4b, U3) also require a test case by full name to be
`Passed` in an NUnit XML bound to the run (A1 core/alone, A3 isolated, or the chain's `<E>/r2/` sessions). If the
name is not in any bound XML the gate is BLOCKED; any other result is FAIL.

## Camera scope (C-00 06.10: top down only, tests too)

A gate may name the camera modes it is measured in (`modes` in `r2-gates.json`): fp — H4, H4a, H4b; tp — H1b, H1c, H6;
multi-mode — Z3 (topdown, tp), S3 (topdown, tp, fp). `wp078_accept.py --modes topdown` runs a2 and z3 in top down only,
does not run the r2 / r2q sessions of a gate no run mode covers, and records the modes in the manifest. The report
writes such a gate, and every left-out A2 / Z3 mode, OUT_OF_SCOPE ("kullanıcı 06.10: yalnız TopDown"), never FAIL or
BLOCKED; a multi-mode gate is judged on its modes in scope. Without `--modes` every mode runs as before.

## Which session measures a gate

`measuredBy` in `r2-gates.json` (`{"suite": "playmode"|"editmode", "filter": "<test.sh --filter regex>", "quiet": bool,
"env": {"NAME": "value"}}`; `env` optional: the owner's own switches such as `DD_KIT_WEAPONS`, never `DD_R2_*`)
(an `env` value may use the one placeholder `{KPROJ}`, the KADRO project root the chain fills in from
`tools/dd_paths.py` — never a machine path in the contract) tells `wp078_accept --round R2` to run that test in its own guarded session: stage `r2` (after A3) for `quiet: false`,
stage `r2q` (after A4, B9 and Z3) for `quiet: true`. Gates sharing one filter and env share one session.
**What `quiet: true` holds in r2q** (KADRO game-test-engineer SHOULD-6; C-00 06.10 decision in 8626d147, the criterion
r2_s3 uses): r2q cannot take every Unity slot, because its own Unity needs one. Its PlayMode guard snapshot already holds the
player slot, so a guarded foreign Player waits for it.
- Quiet = no foreign Unity, Player, Blender or ffmpeg. The chain's own descendants are not foreign.
- The load average is reported, never judged. iCloud I/O keeps load1 at 26–27 here with nothing heavy running.
- Before each quiet session the chain waits at most 30 min for quiet. While the session runs it samples every 5 s.
- The samples must cover the session, as r2_s3's samples must cover its takes (3b0e4327): the first within 30 s of the
  session's start, the last within 30 s of its end, no gap over 30 s. A sampler that stalled or died early sees nothing
  after it. `ps` and `sysctl` run with a 10 s timeout.
- The record is `<E>/r2/<step>/host.json` plus `host-samples.tsv`, bound with the session and with each gate it measured.
- A session that did not start quiet, had a foreign heavy process beside it, could not read the host (ps), took no
  sample, or whose samples do not cover it still runs, and is recorded `dirty`. The report shows **KİRLİ** on those gates, as a note, never as their
  status.
- The owner's own quality gates decide the value: S1/S2's pile-up check re-takes, then fails the test; S4 has no dropped
  samples at load1 ~105.
- A quiet gate measured with no host record says so too.
"KİRLİ" has two uses (gt KP-16). This chain note marks a value that is bound and judged on a busy host. An owner tool's
own KİRLİ stamp (SABİT-KURALLAR 5.8) is different: `r2_s3.py` writes `r2-S3.KIRLI.json` instead of `r2-S3.json`. That file
is never bound, and the gate stays BLOCKED until a clean run.
An `[Explicit]` test runs only when the filter names it in full (S-02 05.10): give its full name, never a class regex. A gate without
`measuredBy` gets its artifact from the owner's own run into `<E>/r2/`. H4 is split (C-00 05.10): H4 = weapon yaw/roll
table (S-02), H4a = FP arm keys (S-06), same thresholds. H4a is measured with the FP arms on (C-00 06.10: the only mode where
they are visible; on the default path they are off) by `AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta`
(WP-083 R2 `6711f57f`; not `[Explicit]`, also in CI). It writes `armOffscreenFrames` and `armInCameraFrames` over the armed
weapons' rest frames; `samples` is the frames measured. The chain runs it by full name in the r2 stage. H4a also names it
in `tests` (C-00 06.10), so the test's own acceptance is part of the gate: arm share of the screen <= 10 %, hand <= 1 cm,
rest <= 0.3 cm, elbows off screen. H4a can FAIL on a failed case while both keys read 0.
Other S-06 writers (WP-083 R2, S-06 06.10; playmode, `[Explicit]`, run by full name in the r2 stage, quiet false, no env):
- H2 `R2HeroGovdeTests.H2_SilahGovdeGomulmesi_TumKlasikSilahlar_DurusYuruyus` (`f114d24c`): `clipFrames` (weapon LOD0 more
  than 0.5 cm inside the chest, shoulders or upper arms); `samples` = the sampled frames.
- L4 `R2LokomotionIkincilTests.L4_IkincilHareket_DurusSonrasiOturma` (`c001a5e1`, report): `amplitude` (deg),
  `settleSeconds` (s). There is no spring layer today, so it measures LocomotionLean, the only procedural body layer.
- L5 `R2LokomotionIkincilTests.L5_Kalabalik_FazSenkronuCiftleri` (`c001a5e1`): `syncedPairShare`, the worst sampled frame
  over the 40-zombie GateMix crowd.
- L1, L1t, L2, L3: one course, `R2LokomotionOlcumTests.L1_L3_Lokomotion_8Yon_KalkisDurusDonus` (`ca1d743d`), one r2
  session writes all four. Hero only; the draft's per-tier zombie L1 is not covered (C-00's call).
  - L1 / L1t are unitless: foot speed over body speed, with body speed floored at 1 m/s. `samples` = contact foot-frames
    for L1, transition-window foot-frames for L1t.
  - L2 measures the planted foot's backward speed; the draft's literal world-space step always equals v. `samples` =
    steady cycles.
  - L3's steady peak is floored at 1 deg/frame. `samples` = transition frames.

## H1 (hand to grip, decision B; elbow report — C-00 05.10)

**What `maxCm` measures** (C-00 05.10, decision B): the right hand is measured from `socket_weapon_r` to the weapon
definition's grip point, `mount.TransformPoint(GripOffset)` — the point WeaponMountAlignment puts on the socket — **not**
AP-021's `marker_grip`, which is the model origin (trigger root, ADR-042-1). Grip decision B (25.09) puts the hand behind
and under the trigger on the measured mid-grip; the offsets are checked against the model by
`Ap021ClassicModelGateTests.Ap021Klasik_GripOffset_Lod0TutamakBolgesinde` (LOD0 grip region centre <= 1 cm, >= 50
vertices). The left hand is measured from its palm contact point — not the wrist — to its target K, on the gripped
element's surface at the station of `grip_l` / hands_in_weapon_frame, which the weapon authors as that palm contact point
(`WeaponGripIk.LastError` = |palm − K|; C-00 06.10 definition fix; 1 cm unchanged). The gate is the default player path: an optional switch (e.g.
`DD_HOLD_IK=1`) is evidence for a fix, not the gate value.

**Elbow** (report): `R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus` writes `elbowMaxDeg` next to `maxCm`: the left (support) arm's
elbow, the interior angle at LeftLowerArm between the upper arm and the forearm (180 = straight), the largest over the frames the
grip IK carries full weight, all weapons. In H1 it is reported, never gates; the elbow and reach checks are H1c
(C-00 06.10). Source: S-02's hero v2 reach pre-study (K/wp-082/evidence/hero-v2-reach/; its correction: with the hold
on 121–153°, rl_1 the largest — the 167° / 161° of its first table were taken to grip_l, not to the IK's real target).

## H1b (support-hand grasp — C-00 06.10)

Over every classic weapon with a foregrip (rl_1's front grip included), stance and walk, the frames where the grip IK carries
full weight (C-00 06.10 H1b record (a)–(d), on S-06's t-b2f3 and t-b2f4-off measurements):

- `palmToWeaponMaxDeg <= 35` — the worst angle between the left palm normal (from the hand's geometry, its sign checked
  against the thumb) and the perpendicular from the palm point P to the gripped element's own axis. The axis direction
  comes from the weapon frame: +Z on a foregrip / handguard, the grip's +Y on rl_1's vertical grip. The axis line runs
  through the LOD0 section centre at P's station, which the test computes itself, independent of the IK. It is not the
  barrel line: on C and sg_2 the pump foregrip sits under the barrel, and a barrel line would shift the angle by 20–40°.
  The first criterion (palm against the weapon frame's +Y ≤ 30°) read 40° on lmg_2, which the AD calls a good grasp, and
  meant nothing on rl_1.
- `fingertipGapMaxCm <= 1.5` — the index and middle fingertips to the nearest point of the weapon's LOD0 surface.
- `palmGapMaxCm <= 1.5` — P to the nearest point of the LOD0 surface (the left-hand counterpart of the right hand's MUST-4).

**The palm threshold is derived, and 35° is provisional:**
- After S-06's fix, the binding value is the highest palmToWeaponMaxDeg on the frames the AD calls good, + 5°, capped
  at 30°. C-00 writes it here.
- If an AD-good frame reads above 30°, or an AD-rejected frame reads below the threshold, C-00 decides.
- "lmg_2 + margin" was rejected: 45° would pass sg_2 (42°), which the AD rejected.

Report keys (plain numbers):
- `weaponsMeasured`, `framesMeasured` — the test writes nothing unless both are above zero (source in the record).
- `wristClampShare` — the share of measured frames the wrist gate clamped, 0–1.
- `wristMaxDeg` — the largest wrist angle against rest: flexion/extension, deviation or twist.
- `thetaSwitchCount` — how often the wrap target changes over one weapon's stance + walk, largest over the weapons. The
  target is 0 at stance and ≤ 1 per walk cycle (co-sign K1); it is a note, not a check.

The worst weapon's name goes to the log, not into the values. B2e AD MUST-2 (S-06). Writer (WP-083 B2f, e8f3ae19 / 18320dab):
`R2HeroOlcumTests.H1b_SolEl_Kavrama_TumKlasikSilahlar_DurusYuruyus` (`[Explicit]`, Diagnostics), run by full name in the chain's
r2 stage (`measuredBy`, playmode, quiet false, no env).

## H6 (TP weapon visibility — C-00 06.10)

On the over-the-shoulder TP game camera, every classic, stance and walk: `visibleShareMin >= 0.25` — the worst weapon's
visible pixels (depth-tested in the scene) over its own unoccluded mask; and `muzzleInsideSilhouetteFrames == 0` — frames
whose Muzzle socket projects inside the body silhouette. Report `weaponsMeasured` and `secondWorstShare` (the second-worst
weapon's share, so one weapon stuck alone is told apart). The per-weapon table goes beside the artifact
(`r2-H6-perweapon.csv`: rosterKey, share, muzzleInsideFrames) and into the log. Rendered with `cam.Render()` into a
target texture in batch (no WaitForEndOfFrame). B2e AD MUST-3 (S-06). Writer: `R2HeroGovdeTests.H6_TpSilahGorunurlugu_
TumKlasikSilahlar_DurusYuruyus` (WP-083 R2 `f114d24c`, `[Explicit]`), run by full name in the r2 stage. It needs a graphics
device: `tools/test.sh` runs PlayMode without `-nographics`; under `-nographics` the source check fails and no artifact is
written. The CSV next to the artifact is never bound (the judge reads `r2-<id>.json` only).

## H1c (support-arm reach — C-00 06.10)

Over the frames the grip IK carries full weight, every classic, stance and walk, worst frame: `elbowMaxDeg <= 165` — the
left elbow's interior angle at LeftLowerArm between the upper arm and the forearm (180 = straight), the measure H1 already
reports; and `reachMarginMinCm >= 1` — the arm's spare reach. It is the upper arm + forearm (bone lengths read from the
live skeleton, never a constant) minus the straight distance from the shoulder (LeftUpperArm) to the IK's wrist goal W
(`WeaponGripIk.LastWristGoal`, after the wrist gate: the wrist that puts the palm contact point on its target, H1). A
target out of reach reads negative. Today's arm is 29.0 + 25.5 = 54.5 cm (S-02's reach pre-study,
K/wp-082/evidence/hero-v2-reach/); hero v2 may change it. With the IK on its target the two are one geometry: on today's
arm 165° leaves about 0.5 cm and 1 cm is about 158°, so the reach check binds; the elbow check still catches a pose that
bends the arm straight short of its target. S-06's own B2f gate is the same (C-00 06.10: its "max(165°, b2f4-off)"
relaxation is void). C-00 06.10 decision on S-06's t-b2f4-off. S-06 writes it (R2Olcum.Yaz("H1c", …)) in
`R2HeroOlcumTests.H1c_SolKol_Erisim_TumKlasikSilahlar_DurusYuruyus` (WP-083 B2f, `[Explicit]`), run by full name in the chain's r2
stage (`measuredBy`, playmode, quiet false, no env).

## P3 (B9 poses, report — S-05, C-00 06.10)

No owner test: `tools/qa/r2_z3.py` writes `r2-P3.json` in the same run as Z3, from the chain's B9 windows
(`<E>/b9/<pose>/perf-<tier>-<mode>-<n>/`, R2 only; the S-05 pose list given to wp078_accept as --b9-poses, and to the tool
so that every listed cell is required). One cell per pose x tier x camera mode: the medians over its counted windows of the
probe's frame mean, filtered p99 (the probe's own 33.33 ms stall filter), SetPass, batches, triangles, draw calls and bodies
on screen. Its one value `cells` is the cell count (values are plain numbers); the table is the artifact's top-level cells
list, as in P2. The Z3 source rule below holds here too (armour not required): a window that breaks it leaves P3 unwritten
(BLOCKED by name).

## P5 (Selection → Run load time)

No owner test: `wp078_accept` writes `r2-P5.json` in stage `r2` from the chain's own A2/A3 bot sessions
(`<session>/ui-data/load-timing.json`, entry `Run`), median over the R1 FINAL evidence's same files
(`--r1-evidence <dir> --r1-commit <40-hex>`; tool `tools/qa/r2_load_time.py`). Dev build against dev build, no code in
either build (C-00 05.10 decision (a)). R1 sat on the 1.2 s curtain floor in every session, so `floorLimited` tells
that the ratio only shows an R2 load that grows past it. Every `r2` run first renames an earlier `r2-P5.json` to
`.stale-<utc>`, with or without a baseline this time. A run whose `--r1-commit` differs from the one P2 (the a4 record) or
P5 (the r2 record) was made with stops with 64 at the start of r2 (or of a4), before that stage's sessions. Earlier stages
that have not passed yet still run first. The check applies with or without `--resume`: the existing-evidence refusal only
looks at the selected stages' own outputs (gt KP-18). One report, one R1 baseline (gt 06.10 N-11, KP-2).

## P2 (R2 − R1 perf per cell, report — C-00 05.10)

No owner test: in an R2 round `wp078_accept` A4 runs the R1 FINAL's own release-probe build (the `--r1-evidence` root's
build-probe, proven by that evidence's unit record: complete, built at `--r1-commit`, the same binary sha256) alternately
with FINAL's — window by window in every tier × mode cell, ABBA (R2 R1 R1 R2 …, the starting side swapped per cell), each
pair inside one quiet batch
(`tools/qa/wp078_perf.py` `--baseline-bin`; R1 root `<E>/a4/perf-r1`, R1 commit). `tools/qa/wp078_perf_report.py`
`--baseline` compares the mean of each cell's counted windows and writes `r2-P2.json`: `flaggedCells` (over +10 % and
+1.0 ms together), `worstDeltaMs`, `worstDeltaPct`, and the per-cell rows (window means, SD, the SE of the difference,
the n-th R2 window paired with the n-th R1 window, and both sides' commit and binary sha256). The source is in the
recording on both sides: every window used proves its side's commit and build, the Run scene at the target resolution,
exactly 40 forced and held for the whole window, the warm-up and window lengths, the TP/FP sweep and a full CSV (the
probe exits 0 even when its own window failed). A broken window, or a cell short of the tier's minWindows sound windows
on either side, writes no `r2-P2.json`: P2 is BLOCKED by name and the reasons are listed. The A4 gate is judged on FINAL's
windows only; the R1 root is declared to the report as a secondary perf root whose every window carries the R1 commit
and build. Without `--r1-evidence`, or when the R1 build does not prove itself, A4 runs FINAL alone and P2 is BLOCKED by
name. An R1 build that fails on its own (wp078_perf_report.classify "failed", not a host discard) in three consecutive
windows stops the R1 side for the rest of the matrix (`wp078_perf.py --baseline-fail-stop`, KADRO test-engineer NOTE-4).
That saves ~60 empty windows; FINAL's windows go on and P2 is BLOCKED.

## S3 (Dense-40 loudness and peak, S-08 06.10)

No C# test writes S3: three recordings are measured offline. Writer: `tools/qa/r2_s3.py` (S-08), run by the owner on the
FINAL commit into `<E>/r2/` (its commit is DD_R2_COMMIT). Each key is the worst over its takes:

- `truePeakDbtp`: Dense-40 Player captures (a dev build stamped with the commit, not dirty; QaRunBot from round 12 with
  -ddAudioDense40 for 40 s; one or more runs per camera, the worst counts; R3 (kullanıcı 06.10): top-down by default,
  `--modes tp,fp` for R2's pair), the whole recording, max over channels of the 4x oversampled peak: the higher of a polyphase FIR interpolation and the Catmull-Rom
  estimate of `audio_capture_report.py` (R1's method, kept as the R1-comparable value).
- `momentaryMaxLufs`: the same captures, BS.1770 K-weighting, 400 ms windows on a 10 ms hop (R1's 100 ms grid, which can
  miss up to ~0.4 LU of a peak, is kept next to it).
- `preLimiterGrDb`: the A9 stack render of `AudioSliceTests.A9_EnYuksekYigin_LimiterOncesiZarf_Olcum` (master limiter
  opened to 0 dB): max(0, envelope max + 8) with the -8 dBFS limiter's detector (10 ms attack, 90 ms release, linked stereo).
- `weaponMarginLu`: the W (SFX-Player alone) and R (every other bus) blocks of `AudioSliceTests.DinlemePaketi_AB_Uret`
  (the top-down take; R2 also first-person): 10 log10 of their K-weighted mean power in the burst windows (0.15-1.2 s of
  each 2 s fire cycle). R3 (C-00 06.10, decided before the next measurement — SD K1, a single take is ±0.2 LU noise): every
  AB run given (`--listening` repeats) is POOLED block-weighted per mode, the W and R powers averaged over all their blocks;
  the worse mode counts. Today's Run snapshot is R1's mix B, so this is R1's "mix B margin" (+1.57 / +3.18) measured directly.

The source checks run before anything is written; one fails and nothing is written (S3 BLOCKED): capture dropped 0 and
telemetry overflow 0 in every take, the driver's own dense window (R3, C-00 06.10: player.log `target=` >= 40, "dense
checkpoint reached" = spawn.ActiveCount >= target, "capture window complete"; the window is the capture's last
-ddAudioDenseSeconds, at least 35 s; the markers' alive count leaves rising / dying bodies out and is reported only —
FINAL c14f8a10 read alive 38-39 through a complete window), the camera the take claims, weapon shots
delivered and heard (shot-window peak median >= -30 dBFS; Dense-40 exception to the "source must be in the recording"
rule's >= 6 dB over the pre-window — C-00 06.10: a continuous Dense-40 mix cannot hold it (R1 +3.8 … +5.5 dB), so the
source check is ClassicWeaponFire delivered >= 20 + Fire voices started in the dense window >= 10 + that peak median;
the rise is reported per take in the artifact's takes detail, never asserted), the A9 stack loud in every second and not clipped, W + R within
1 dB of the full mix, both PlayMode tests Passed in the given NUnit XML. A KİRLİ run (a foreign Unity, Player, Blender or ffmpeg sampled during the window in the `--host` log, no host log,
host samples that do not cover the takes — none within 30 s of the first take's start or the last take's end, or a
gap over 30 s between them, a take's span being its WAV's mtime — at or after the capture's end — less its length; run
on the window's folder or a copy that keeps the mtimes, cp -p or an APFS clone — or C-00's mark) writes `r2-S3.KIRLI.json` instead, with the note: ranking only, S3 stays BLOCKED until a clean
measurement. Karar C-00 06.10: the load average is not a criterion (on this machine it counts iCloud I/O waits, not
CPU) and neither is CPU idle (the 06.10 window's valid captures ran at idle p10 0.4 %); both are reported in the
artifact's takes detail.

## S4 (room tails, v2 — C-00 05.10 decision; S-10 reviews after)

The gate is the **RT60 of the tail**, as the AP-027 spec states it — not the −30 dB energy decay from the dry onset, which
mixes the dry level in and tops out near RT60 / 2 once the tail dominates (S-08 tools/tail_mix.py). `MekanKuyrugu_EDC_Olcum`
records each weapon × Open / Corridor / Hall twice on the weapon bus alone: as the game plays it (the source gate: shot-window
peak ≥ −30 dBFS, and the old `edc30` kept as a report), and tail only (the weapon's Fire and Mechanical silenced for that take;
tail peak ≥ −50 dBFS or it fails). Tail take: 500–1000 Hz band, Schroeder EDC from the tail onset, T20 (−5 … −25 dB) × 3,
median of four cues. Thresholds = the spec RT60 ±20 %: open 0.56–1.08 s, corridor 0.88–1.68 s, hall 1.28–2.28 s, and open <
corridor < hall for every weapon (`orderViolations` 0). Report: `openEdc30MsMin`, `corridorEdc30MsMin`, `hallEdc30MsMin`.

## Z1 (DEC-077 readability, C-00 06.10)

`ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet` (same pose, 12 armoured figures,
armour on/off frames) writes `r2-Z1.json`:
- `silhouetteChange` = (grown + lost silhouette pixels) / unarmoured silhouette pixels; gate ≥ 0.30;
- `deltaVBandMean` / `deltaVBandMin` = |ΔV| between armour pixels and the 6 px unarmoured neighbour band of the same
  figure (DEC-077's "brightness band"); gate mean ≥ 0.12, min ≥ 0.08;
- report only: `deltaVUnderMean`, `deltaVUnderMin` (armour vs the cloth under the same pixels), `deltaVGround` (armour
  mean V vs the ground ring).
The test asserts the DEC-077 thresholds as well as writing them (before 06.10 it only reported the band and asserted a
silhouette change > 0.02). Source in the record: the armour mask is non-empty (> 500 px per figure, ≥ 6 figures).

## Z3 (render ratios and character budget — S-05, C-00 06.10)

No owner test writes `r2-Z3.json`: `tools/qa/r2_z3.py` does, after the chain's z3 pose run and its r2 stage
(python3 tools/qa/r2_z3.py --evidence <E> --commit <FINAL> --r1-commit <R1 FINAL>; the same run writes P3).

- **Render ratios** (counts, not times: quiet false). One fixed pose, z3-plazan (start 0,-4,0, the B9 sin-plazan pose),
  Sinematik, topdown and tp, 40 zombies with `-ddArmor all` in both builds, 5 windows per cell run alternately R2 / R1 by
  wp078_perf with `--baseline-bin` on the two release-probe builds: R2 cells in `<E>/z3/perf/`, R1 cells in
  `<E>/z3/perf-r1/`. The n-th counted R2 window of a cell pairs with the n-th counted R1 window; each pair gives R2/R1 of
  the probe's 04-window SetPass, batch and triangle averages; a cell's value is the median over its pairs (at least 5, else
  BLOCKED) and the gate takes the worse mode: `setPassRatio <= 1.10`, `batchRatio <= 1.10`, `trianglesRatio <= 1.15`.
- **Static budget** (needs Unity: skinned bones, imported texture sizes, material counts). After the AP-025 import an
  EditMode test, run by name in the chain's r2 stage, writes `<E>/r2/z3-static.json` (schema dd-r2-z3-static-v1, the
  FINAL commit, exactly the Z3 keys that are not ratios, as numbers > 0: a count, size or MB, never a -1 sentinel); its
  `measuredBy` lands with that test. Without the
  file `r2-Z3.json` is not written and Z3 is BLOCKED by name; the ratios still land in `<E>/r2/z3-ratios.json`
  (diagnostic, never bound).
- **Definitions (C-00 06.10).** `bonesMax <= 74` is the largest `SkinnedMeshRenderer.bones.Length` among a family's
  skinned renderers; the armour anchors (`anchor_helmet`, `anchor_torso`, `anchor_legs`) and rig transforms that are not
  bones are not counted (06.10: 74 on the fit A/B AP-007 rig v002 clothes, 73 on the O1 body and the faces, 74 on face and
  body once the AP-025D jaw lands). `mapsPerAtlasMax <= 3` counts the PBR maps of one atlas (BaseColor, Normal, ORM); the
  mask Texture2DArray is not an atlas map, it counts in the texture budget (Z3b, 96 MB). `materialsPerFamilyMax <= 4` is
  the renderer material count of one zombie instance, armour and tier pieces included; the crowd total per body family
  (8 on 06.10) is the report key `materialsPerFamilyCrowdTotal`. `characterTextureMaxPx <= 2048` (Character class).
  `atlasesPerFamilyMax <= 2` counts the family's own atlases only (body, face, clothes). The armour and tier atlases are
  reported in total as `armorTierAtlasesTotal` (C-00 06.10 13:30 scope decision, S-05). Like every static key, it must be
  in `<E>/r2/z3-static.json` as a number > 0.
  `shippedCharacterMB <= 400` (the shipped character row, ADR-015-1 v3 / DEC-093).
- **The source is in the recording.** Every paired window, both builds: a counted window of its build's commit (one build
  per side, two different builds, `--r1-commit` different from `--commit`), -ddPilotStart 0,-4,0 and -ddArmor all on its
  command line with the probe's "armor all: N pieces fitted" at N > 0, the start pose set and sampled as asked, 40
  configured, forced and achieved and held through the window (lowest active count 40), the sweep off, the
  SetPass, batch, triangle and draw-call averages above zero, at least one body on screen in 06-readability, and one pose
  (sampled positions within 0.25 m) and one screen size across both builds and both modes. A window that did not complete
  on a clean host (wp078_perf_report.classify "failed": a crash, a timeout, a missing artifact) fails its cell as in K8;
  only host-only discards are skipped. One attempt counts once (attemptId and resolved folder unique, index 1..n). A broken
  condition FAILS the tool: no `r2-Z3.json`, every condition named on stderr. `--min-pairs` / `--pose-tolerance` may only
  tighten 5 / 0.25 m and are written into the artifact; a previous `r2-Z3.json` / `r2-P3.json` in the out-dir is renamed
  `.stale-<utc>` before the run (never deleted), so a FAIL or BLOCKED run cannot leave an older artifact bound.

## U1 (two measures — C-00 05.10)

`UiTextInventoryTests.U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr` counts the visible letters of the EN main menu and
Selection against the R1 baseline in two ways, and the gate needs both:

- `u1_default >= 0.40` — the player's default path: v4's real arrival (every pane closed; the test asserts the menu arrived
  that way before it counts) + Selection;
- `u1_same_content >= 0.40` (U1b) — like with like: the menu with the Profile pane open, R1's arrival state, + Selection,
  so no words pass the gate by hiding in a pane.

`lettersTotalReduction` is the smaller of the two, reported for continuity with the earlier single number;
`lettersPerScreenReductionMin` and `unnamedSelectables` are checks as before. A failing U1 names the measure that failed.

## Human gates

H5, L6, Z5, S6, U4: one review file `<E>/r2/reviews/<gate id>.json`:

```json
{"schema": "dd-r2-review-v1", "gate": "H5", "commit": "<40-hex>", "reviewer": "AD Fable",
 "verdict": "KABUL_DÜZELTMELERLE", "must": [{"id": "MUST-1", "closed": true}]}
```

KABUL passes; KABUL_DÜZELTMELERLE passes only with every MUST closed; RED fails. MUST/SHOULD items name the measure
and the frame they rest on (R1 format).

## Report entries

`kind: "report"` (L4, P2, P3, P5) never fail; their values are shown. P2 flags a perf cell that exceeds +10 % and
+1.0 ms over R1 together, and its owner writes the reason.

## Changing a threshold

Only by editing `r2-gates.json` in a reviewed commit **before** the measurement it applies to, with a C-00 record;
`tests/test_r2_gates.py` pins the approved values so an unreviewed change is a red test.
