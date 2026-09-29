# WP-078 A8 report producer

Authority: `WP-078-R1-ACCEPTANCE-PLAN.md` §§1,4–7,9 and current DEC-091/092/093/100.
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
(dev/release, optional probe binary SHA256), and `checklist` A0–A7.
A8 is derived by the producer. Report content includes `checklist`, `perf`, `soak`,
`reportRuns`, `evidence`, `failures`, `status`, and `pass`.

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
- A2: `runs`, exactly topdown/tp/fp normal seed61 runs, each five ordered completed
  rounds, deaths0, native pass, no runtime errors; all five rounds have camera
  inside-collider log samples with zero affected frames. Native stuck counters,
  must be present and zero; no unknown source or binary is accepted.
  Each run has native-bound `frames` references keyed `courtyard`, `corridor`
  (including under-roof coverage), and `horizon`. Hash/PNG coverage checks do not
  replace AD review of those images.
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
  `aiFlags.flagRuns` additionally requires fourteen separate native Player smoke
  executions for the seven plan flags x0/1. Each binds `flag`, integer `value`,
  native argv `command`, `summary`, `log`, `provenance`, `originalExit`, `guard`
  and `buildRole`. The exact flag/value must appear once in native argv, and at
  least one ordered normal round must actually complete. An EditMode flag test
  cannot stand in for the Player matrix.
- A4: `perfRoot` evidence-relative directory, `buildRole` dev or explicitly provided
  probe, and `attempts` references anchored in the step's execution artifact map.
  Attempts attest current resolution, activeEnemies, warmupSeconds and
  windowSeconds from the target file. The existing `wp078_perf_report.report` is evaluated with the current
  `perf-targets.json`; all complete native attempts must bind FINAL/binary, have
  clean host, verified artifacts and guards. No best-window selection. All nine
  current cells gate, including Sinematik under DEC-100, ≥3 windows each.
  Native map must include `frame-times-ms.csv`, `01-scene-ready.json`,
  `02-force-40.json` and `03-warmup.json`. CSV count/exclusion/mean/p99 are
  independently checked using existing CSV/exclusion helpers and current targets;
  requested scene/resolution, exact40 load, warmup and full window are verified.
  Mean/p99 allow 0.001ms for native float/CSV serialization; full-window duration
  allows 0.1s at the recorded boundary. These are serialization allowances,
  not relaxed product performance thresholds.
- A5: `runs`, topdown/Yuksek stability seeds61+26, twelve ordered completed rounds;
  `stabilityPass`, existing `soak_gate.memory_checks` and log rules. Per-run
  `stuckRecoveries=0` is mandatory.
  `worldHalfExtent=55` and `hostClean=true` provenance attest scope. `reportRuns`
  contains source-bound normal-bot data: ≥5 topdown, ≥2 tp, ≥2 fp. Deaths and
  difficulty are reports, not extra balance gates.
  In addition to the generic log helper, §5.5 rejects actual
  `Failed to create agent` / NavMesh `SetDestination` failures, native
  `[Pool] ... exceeded its capacity of N. Growing.` warnings, nonzero native
  `[Projectile] round=N ... overflow=O`, and bounded Joint/Rigidbody error
  context. `overflow=0`, descriptive physics and generic benign pool references
  remain non-errors. No new product warning threshold is introduced.
- A6: `builds` dev/release (optional probe), each `library`, `buildReport`,
  `report`, `binary`, `provenance`, `originalExit`, `guard`. Native build report
  has clean `stamp.commit`, `stamp.stamped`, `mode`, `result=Succeeded`,
  `totalErrors=0`, `totalSize`; footprint/smoke `report` has `ok=true`.
  Release provenance attests `compression=LZ4HC`; native `totalSize` is positive
  and ≤5,000,000,000 bytes (DEC-093). Report field aliases do not replace evidence.
- A7: `byteEqual: true`, verified restoration and `integrityXml` containing an
  actual Passed `ProjectIntegrityTests` leaf, without failed/skipped tests.

A2/A5/report runs have `provenance`, `originalExit`, `guard`, `summary`, `log`,
and `buildRole` (dev unless explicitly changed). Source SHA/binary/artifact/guard
validation applies to every run, not only its enclosing step. Guard metadata is
read from receipts; the producer never repairs or inspects raw profiles.

The producer reports incomplete evidence as BLOCKED and proven violations as
FAIL. FAIL→owner is preserved; unknown owner is explicitly UNKNOWN, never guessed.
All A0–A8 require one FINAL SHA for M4-R1 PASS. Existing owner source/test/AD ledgers
are retained separately and are not relabeled to a new source.

This producer is deliberately conservative about host-discarded attempts: it
keeps acceptance non-PASS until C-00 supplies a validated host-only discard
ledger. It never silently drops a failed complete window. TP/FP and Sinematik
supplementary stability observations are reporting limits, not DEC-091 gates.

Native fixture details: camera log lines are `[Camera] inside-collider round=N
... frames=0/TOTAL`; `stuckRecoveries` is the actual QaRunBot counter. Perf attempt
JSON uses existing `executionCommit`, `binarySha256`, `commandSha256`,
`artifactSha256` mapping, `status`, `exitCode`, `hostClean`; its manifest-bound
`guard` metadata uses the common guard fields above. If that per-run guard receipt
is not available, the producer reports missing proof, never assumes restoration.
Real-bot report runs may end in balance deaths with original exit1/native
status=failed; those outcomes remain reports and are retained, not called PASS.
They still require source/binary/artifact/log/restoration proof. Stability runs
and every acceptance gate require original exit0 independently of guard exit0.
