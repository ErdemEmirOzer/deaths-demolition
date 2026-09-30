# WP-078 A8 report producer

Authority: `WP-078-R1-ACCEPTANCE-PLAN.md` §§1,4–7,9, current DEC-091/092/093/100 and the
C-00 30.09 K1–K9 record (`c-00/WP-078-K-KARARLARI-2026-09-30.md`) plus C-00 30.09 R1–R3 (route runs).
The existing output is **wp078-report.json**, schema **dd-wp078-acceptance-v1**.
The plan specifies content, not exact JSON property spelling. The names below are
implementation mappings of that content, not new product decisions or a new schema.

`wp078_report.generate_report(manifest, evidence_root, targets=None)` is pure evidence
inspection. CLI: `python3 tools/qa/wp078_report.py --manifest INPUT.json
--evidence-root EVIDENCE --out NEW_REPORT_DIRECTORY`. It does not run Git, tests,
Unity, Players, builds or gates, acquire resources, or read raw user-data snapshots.
Output files are created exclusively; prior reports are never overwritten.
CLI exit 0 means complete acceptance PASS, 1 means an honest non-PASS report, and
64 means invalid command/input/output. Python fixture PASS is not game acceptance.

## Serialization and source binding

The manifest uses `schema`, full 40-hex `commit`, explicit `final: true`,
`assetsTree` (the FINAL commit's full Assets tree identity), `buildHashes`
(dev, release and probe binary SHA256 — all three mandatory, K1), and `checklist` A0–A7.
A8 is derived by the producer. Report content includes `checklist`, `perf`, `soak`,
`reportRuns`, `botRuns`, `routeRuns`, `buildParity`, `evidence`, `failures`, `status`, and `pass`.

Each step has `status` (PASS/FAIL/BLOCKED/SKIPPED/NOT_RUN), `owner`,
`executionCommit`, `originalExit`, `provenance`, and a nonempty `evidence` list.
Executed steps A1–A7 also have `guard` containing `snapshotExit`, `restoreExit`,
`verified`, and `outOfScopeChanged`. Original exit and restoration are independent.
Unknown or missing values never default to success. A0 is read-only provenance.
Optional `acceptance` HOLD/OPEN/NOT_READY prevents promotion to full acceptance.

Every file reference is `{path, artifactSha256}`. Paths resolve beneath the explicit
evidence root; SHA256 is checked against actual bytes. Provenance is a JSON file,
not a label on the manifest: its `executionCommit` or Library `head` must be the
full FINAL commit. Present `sourceCommit`/`testedCommit` must agree. A present
`artifactSha256` map and `commandSha256` are also checked against real resources.
Every execution provenance must bind its selected outputs through its native
`artifactSha256` map. Merely supplying a new intake hash for old XML or a summary
cannot promote it. Executed gate receipts require native `status=complete` and
the same guard metadata as the selected receipt. Executed native receipts must
contain integer `exitCode`, matching the independently recorded mandatory
`originalExit`; missing native exit proof is BLOCKED. A0's read-only Library
inspection is exempt from a native process exit field. Native summary `timedOut`
is checked when present; native completion/ordered-round/stability proofs remain
mandatory, so omission cannot substitute for missing completion proof. Binary hashes are
matched to actual build binary references and to each physical run's provenance.
No short SHA, different source, absent evidence, skipped test, or targeted HOLD
receipt can yield M4-R1 PASS.

Present timeout/write-failure fields must be native boolean false; true or
malformed types contradict completion. In particular native attempt timeout
metadata cannot be hidden by a successful summary or restoration.

## Step payloads

- A0: `library` reference to existing Library provenance: `head`, empty `porcelain`,
  `schema=dd-wp064-library-provenance-v1`, `phase=post-build`, matching
  `assetsTree`, `lfsPointersOnDisk=0`, `lfsMissing=0`, positive
  `library.artifactDbBytes` and `artifactDbMtime`.
- A1: `xml` mapping `editMode`/`playMode` to raw NUnit XML references. Nonempty,
  consistent leaf counts, all Passed, zero failed/skipped/inconclusive.
- A2: `runs`, exactly topdown/tp/fp normal seed61 gameplay runs, each five ordered completed
  rounds, deaths0, native pass, no runtime errors; all five rounds have camera
  inside-collider log samples with zero affected frames. Native `stuckRecoveries`
  is present, reported (integer, missing = BLOCKED; with `unstickSweeps` per run in
  `botRuns`) — the bot's own route recovery, not a gate (K6). No unknown source or
  binary is accepted. Gameplay runs carry no evidence frames (R1–R3).
  `routeRuns`: exactly one QaRunBot ROUTE run per camera mode (topdown/tp/fp,
  `-ddQaBotRouteTour <runOut>/frames`, dev build), each with `provenance`,
  `originalExit` 0, verified guard, `summary` (bot-summary.json, native
  `runMode=route`), `log`, `buildRole` dev and native-bound `frames` keyed
  `courtyard`, `corridor`, `kairo`, `horizon`. Topdown `horizon` is recorded
  `{"notApplicable": "topdown-fixed-camera"}`: the top-down yaw is fixed north and its
  top ray is ~28° below the horizon; the E-3 eye-level horizon is proven by FP. A
  missing key is BLOCKED — never FAIL, never substituted. Each bound frame is
  cross-checked against the run's native `routeFrames` list (indexed by `key`):
  `status=captured`, `path` = `frames/route-<key>.png` = the bound file inside that
  run's directory, `mode` = the run's camera mode, `roofAbove=true` for corridor and
  kairo; all frame SHA-256 values of one run are distinct and every frame is a
  complete PNG. No rounds/deaths gate applies to route runs. Hash/PNG coverage checks
  do not replace AD review of those images.
- A3: `checks`, one source-bound PASS with evidence for each plan matrix area:
  `projectile`, `penetration`, `launcher`, `arsenal`, `gel`, `chain`, `ragdoll`,
  `zombieAttacks`, `animation`, `camera`, `world`, `aiFlags`, `progression`,
  `audio`, `ui`, `readability`. N/A remains useful reporting, never full R1 PASS.
  Primary proof is actual NUnit `test-run` XML with **all** exact existing test
  method identities in `A3_REQUIRED_TEST_IDS` for that area. Parameterized NUnit
  instances normalize to their declared method. The identities are traced to
  native owner suites; a class prefix, invented method, generic `pass/ok` JSON
  or unrelated build `result=Succeeded` cannot establish domain PASS.
  Supplemental JSON preserves closure/HOLD and explicit negative verdicts; it
  cannot replace domain outputs. Each present `executionCommit`, `head`,
  `sourceCommit` or `testedCommit` must equal the full execution/FINAL commit.
  Each present `pass` or `ok` must be native boolean true, and present `result`
  must be the exact string `Passed`, `PASS` or `Succeeded`. False, unknown,
  partial, mistyped or contradictory values remain nonPASS even beside valid
  domain XML. These fields are optional on supplementary metadata: their absence
  does not invent a new gate or a positive verdict, and XML remains mandatory.
  This identity mapping does not reinterpret an owner test as
  a missing dedicated scenario, route, simulation or AD proof elsewhere in the plan.
  `aiFlags.flagRuns` additionally requires exactly four separate native Player smoke
  executions, `-ddAiV3` and `-ddRagdoll` x0/1 — the AI argv flags the FINAL C# source parses
  (C-00 30.09 K4; `AI_FLAGS`, kept equal to the source by
  `tests/test_wp078_ai_flags.py`; a newly parsed AI flag grows the matrix by two runs).
  The five other specification flags are listed on the A3 row under
  `areas.aiFlags.notPass` ("spesifikasyonda var, kodda yok") and never count as PASS:
  `-ddChainHops`/`-ddGelPuddles` retired (DEC-097/099); `-ddCorpseLinger`,
  `-ddCrowdNeighbours`, `-ddSpawnLos` withdrawn in writing (S-03, C-00 30.09).
  `-ddRagdoll` reached main with S-06 R2 and is in `AI_FLAGS`. Each binds `flag`, integer `value`,
  native argv `command`, `summary`, `log`, `provenance`, `originalExit`, `guard`
  and `buildRole`. The exact flag/value must appear once in native argv, and at
  least one ordered normal round must actually complete. An EditMode flag test
  cannot stand in for the Player matrix.
  `world` (K6; C-00 30.09 records it as a package gate): `tools/qa/stuck_clusters.py`
  (same spot ≥ 5 s within 1 m with a stall event) runs on every hash-verified A2 and A5
  stability Player log. Any cluster is FAIL; a missing or unmeasurable log (no timed
  trace) makes the world area NOT_RUN — "ölçülmedi" — reported BLOCKED, never PASS.
  Real-bot report-run logs are measured and listed, not gated.
- A4: `perfRoot` evidence-relative directory, `buildRole` **probe** (mandatory; missing
  = BLOCKED, any other role = FAIL — K1: the perf gate runs on release-probe, bots on
  dev; a dev measurement is only an A/B delta and never covers a probe FAIL), `ledger`
  (the root's `attempts-ledger.jsonl`, hash-bound), and `attempts` references anchored
  in the step's execution artifact map. Every window's `binarySha256` equals
  `buildHashes.probe`. Attempts attest current resolution, activeEnemies, warmupSeconds and
  windowSeconds from the target file.
  Ledger (K8): one root and one ledger per FINAL — a perf window directory anywhere
  else under the evidence root is BLOCKED. Ledger attemptIds (read with
  `wp064_gate.read_ledger`; `classify_perf_ledger` is not used) and the disk
  `perf-*/attempt.json` attemptIds match one-to-one; a missing or orphan record is
  BLOCKED. A discard is only (a) `status=preflight-failed` ∧ `exitCode=75`, or (b)
  `status=failed` ∧ `hostClean=false` ∧ `timedOut=false` ∧ `exitCode=0` ∧
  `missingArtifacts=[]` (wp064_run exit 76). Every other non-complete attempt is FAIL
  (to its owner). Discards are listed (`perf.discards`) with a per-cell count. Every
  `complete` ∧ `hostClean` window counts; no best-window selection. All nine current
  cells gate, including Sinematik under DEC-100, ≥3 counted windows each.
  Native map must include `frame-times-ms.csv`, `01-scene-ready.json`,
  `02-force-40.json` and `03-warmup.json`. Stalls (K9): the stall count, the stall
  fraction (≤ `stallFractionMax`) and the filtered p99 are recomputed from the
  hash-bound CSV with the tier's `tiers.<tier>.stallMs` (Sinematik 40, Yüksek and
  Performans 33.33; top-level `stallMs` is the fallback). The probe's native 33.33 ms
  fields (`framesExcludedAsStalls`, `frameP99FilteredMs`) and mean must equal the CSV
  as a probe-integrity cross-check only. Per window the report gives raw p99, the
  fraction of frames > 33.33 ms, p99.9 and max; `rawP99OverLimit=true` marks a raw p99
  above the tier's DEC p99 while the filtered p99 passes (listed in
  `perf.escalations` for C-00 to carry to the user). `wp078_perf_report.report` is
  recorded as a cross-check (`perfReportCrossCheck`), not the verdict.
  Requested scene/resolution, exact40 load, warmup and full window are verified.
  Mean/p99 allow 0.001ms for native float/CSV serialization; full-window duration
  allows 0.1s at the recorded boundary. These are serialization allowances,
  not relaxed product performance thresholds.
- A5: `runs`, topdown stability seeds61+26, twelve ordered completed rounds;
  `stabilityPass`, existing `soak_gate.memory_checks` and log rules. Per-run
  `stuckRecoveries` is present, reported (K6). Tier (K5): the expected tier is
  `perf-targets.json` `defaultTier` → `tiers[defaultTier].probeValue` (Sinematik,
  DEC-100), read from the bot summary's native `qualityTier`; `worldHalfExtent=55` is
  read from the bot summary; a missing field is BLOCKED (argv or labels are not
  evidence). The run's hash-bound `command.json` must give `-ddQualityTier sinematik`
  exactly once (case-insensitive); `hostClean=true` still comes from the attempt
  provenance. Each stability run's slowest round and its margin to the 420 s round
  timeout at Sinematik 30 fps is reported in `soakRoundTimeoutMargin` (K5; report only,
  the timeout is unchanged). `reportRuns`
  contains source-bound normal-bot data: ≥5 topdown, ≥2 tp, ≥2 fp. Deaths and
  difficulty are reports, not extra balance gates.
  In addition to the generic log helper, §5.5 rejects actual
  `Failed to create agent` / NavMesh `SetDestination` failures, native
  `[Pool] ... exceeded its capacity of N. Growing.` warnings, nonzero native
  `[Projectile] round=N ... overflow=O`, and bounded Joint/Rigidbody error
  context. `overflow=0`, descriptive physics and generic benign pool references
  remain non-errors. No new product warning threshold is introduced.
- A6: `builds` dev, release and probe (all mandatory, K1), each `library`, `buildReport`,
  `report`, `binary`, `provenance`, `originalExit`, `guard`. Native build report
  has clean `stamp.commit`, `stamp.stamped`, `mode` (dev / release / `release-probe`),
  `result=Succeeded`, `totalErrors=0`, `totalSize`; footprint/smoke `report` has `ok=true`.
  Release provenance attests `compression=LZ4HC`; native `totalSize` is positive
  and ≤5,000,000,000 bytes (DEC-093). Parity (K1, `buildParity`): release and probe
  native reports each carry `compression=lz4hc` and `development=false`, and equal
  `sceneList` (build order, `{path, guid}` entries as DdBuild writes them; path and guid compared), `scriptingBackend`, `target` and scene count.
  A parity field the native report does not carry is BLOCKED and named in
  `buildParity.missingNativeFields` — it is never inferred from the build mode.
  Report field aliases do not replace evidence.
- A7: `byteEqual: true`, verified restoration and `integrityXml` containing an
  actual Passed `ProjectIntegrityTests` leaf, without failed/skipped tests.

A2/A5/report/route/flag runs have `provenance`, `originalExit`, `summary`, `log`,
and `buildRole` — always dev (K1: QaRunBot exists only in DEVELOPMENT_BUILD; any other
role is FAIL). Their guard receipt is read from the run's own attempt.json
(`tools/qa/wp078_bot.py`, wp064_run shape: `verified` and `outOfScopeChanged` may be
booleans); a manifest `guard` copy must repeat it. Present attempt `kind`
(gameplay/route/stability/report/flag), `buildRole` and `mode` must match the step
and the bot summary. Source SHA/binary/artifact/guard
validation applies to every run, not only its enclosing step. Guard metadata is
read from receipts; the producer never repairs or inspects raw profiles.

The producer reports incomplete evidence as BLOCKED and proven violations as
FAIL. FAIL→owner is preserved; unknown owner is explicitly UNKNOWN, never guessed.
All A0–A8 require one FINAL SHA for M4-R1 PASS. Existing owner source/test/AD ledgers
are retained separately and are not relabeled to a new source.

Host-discarded perf attempts follow the mechanical ledger rule in A4 (K8): only the
two runner-defined discards are dropped, each is listed, and nothing else is. The
producer never silently drops a failed complete window. TP/FP and non-Sinematik
(Yüksek/Performans) supplementary stability observations are reporting limits, not
DEC-091 gates (K3: TP/FP stability is a report in R1).

Native fixture details: camera log lines are `[Camera] inside-collider round=N
... frames=0/TOTAL`; `stuckRecoveries` is the actual QaRunBot counter. Perf attempt
JSON uses existing `executionCommit`, `binarySha256`, `commandSha256`,
`artifactSha256` mapping, `status`, `exitCode`, `hostClean`; its manifest-bound
`guard` metadata uses the common guard fields above. If that per-run guard receipt
is not available, the producer reports missing proof, never assumes restoration.
Real-bot report runs may end in balance deaths with original exit 3 (QaRunBot's
native failed-run exit; older receipts 1) / native status=failed; those outcomes remain reports and are retained, not called PASS.
They still require source/binary/artifact/log/restoration proof. Stability runs
and every acceptance gate require original exit0 independently of guard exit0.
