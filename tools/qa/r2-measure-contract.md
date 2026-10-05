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
  (`Assets/_Prototype/Tests/PlayMode/R2Olcum.cs`, S-02) — one writer for every owner, no copies.
- `commit` comes from **`DD_R2_COMMIT`** (the runner sets it to the FINAL SHA). A file with another commit FAILs.
- `values` carries exactly the keys listed for that gate in `r2-gates.json` (`checks[].key` and `report[]`), as plain
  numbers (no NaN/Infinity, no strings). A missing key is BLOCKED; a value outside its check is FAIL.
- The test itself should also fail on its threshold (so CI catches a regression early); the artifact is what the
  acceptance report re-judges, so both must use the contract numbers.
- **The source must be in the recording** (S-08 05.10, after S1 0.17 was measured on a weapon stem that held no
  weapon): before it writes, the test asserts that what it measures is actually there — a sound above a floor in its
  window (S-08: shot-window peak median >= -30 dBFS), the objects counted on screen, the frames rendered. If it is
  not, the test FAILS and writes no artifact, so the gate is BLOCKED by name; an empty capture never becomes a
  plausible number.
- Tests that measure are `[Explicit]` + `[Category("Diagnostics")]` when they are slow or need a quiet machine (Y6);
  the acceptance chain runs them by name.

## Required test identities

Some gates (`tests` in `r2-gates.json`, e.g. Z1, Z3b, H4, H4b, U3) also require a test case by full name to be
`Passed` in an NUnit XML bound to the run (A1 core/alone, A3 isolated, or the chain's `<E>/r2/` sessions). If the
name is not in any bound XML the gate is BLOCKED; any other result is FAIL.

## Which session measures a gate

`measuredBy` in `r2-gates.json` (`{"suite": "playmode"|"editmode", "filter": "<test.sh --filter regex>", "quiet": bool,
"env": {"NAME": "value"}}`; `env` optional: the owner's own switches such as `DD_KIT_WEAPONS`, never `DD_R2_*`)
(an `env` value may use the one placeholder `{KPROJ}`, the KADRO project root the chain fills in from
`tools/dd_paths.py` — never a machine path in the contract) tells `wp078_accept --round R2` to run that test in its own guarded session: stage `r2` (after A3) for `quiet: false`,
stage `r2q` (in the quiet window after A4) for `quiet: true`. Gates sharing one filter and env share one session.
An `[Explicit]` test runs only when the filter names it in full (S-02 05.10): give its full name, never a class regex. A gate without
`measuredBy` gets its artifact from the owner's own run into `<E>/r2/`. H4 is split (C-00 05.10): H4 = weapon yaw/roll
table (S-02), H4a = FP arm keys (S-06), same thresholds.

## P5 (Selection → Run load time)

No owner test: `wp078_accept` writes `r2-P5.json` in stage `r2` from the chain's own A2/A3 bot sessions
(`<session>/ui-data/load-timing.json`, entry `Run`), median over the R1 FINAL evidence's same files
(`--r1-evidence <dir> --r1-commit <40-hex>`; tool `tools/qa/r2_load_time.py`). Dev build against dev build, no code in
either build (C-00 05.10 decision (a)). R1 sat on the 1.2 s curtain floor in every session, so `floorLimited` tells
that the ratio only shows an R2 load that grows past it.

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
