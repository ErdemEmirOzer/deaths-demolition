# S-01 / S-03 independent intake — 2026-09-29

Read-only source and existing-evidence review. No test/build/Unity, git status, Git index/ref/worktree mutation, lease change or lock maintenance was performed. This report is the only output written by this review.

## Verdict

- **S-01 e8dc425bbdea74b25a9c40b7ee0bdfc20397df03: HOLD for strict acceptance hardening.** The stage-name, DEC-091 and DEC-093 changes are useful and the delivered 67 Python tests passed, but the new aggregate accepts incomplete M3/closure gate sets. Its current positive fixture actually omits most mandatory closure evidence and still expects PASS. This is not a real RC acceptance result.
- **S-03 f802c78462484d43e11d013f897a8c08e56a0c5f: PASS for diagnostic-only intake.** One real targeted controller Move/contact smoke is independently bound to this exact candidate. It proves the opt-in observer plumbing and Ground scene source binding, not a stalled-C1 cause or production fix. C1 remains UNKNOWN / KnownOpen.

The S-03 hash supplied in the dispatch had a trailing `2`; receipt, Git object, input manifest and test-base-sha.txt all give the 40-hex SHA ending `...e56a0c5f` above.

## Source / evidence identity

Both commits have exactly one parent: 3c0875e1e259cebae34e2cb496d03bb4e8dd9045.

| Item | S-01 | S-03 |
|---|---|---|
| Changed paths | Exactly 5: rc.sh, rc_aggregate.py, wp064_gate.py, test_rc_aggregate.py, test_wp064_gates.py | Exactly 2: ZombieEnemy.cs, V6AiRoundTests.cs |
| Delivery evidence hashes | 7/7 matched | Receipt hashes 12/12; artifact-hashes.json 9/9 matched |
| Candidate/source binding | All 5 candidate blob SHA256 values equal test-receipt sourceHashes | All 12 input manifest Git blobs and commit/LFS SHA256 bindings matched; all 12 current source file hashes also matched |
| Existing tests | XML contains 67 actual testcase elements, no failure/error/skip; log ends Ran 67 tests / OK | XML contains exactly C1_Observer_ActualMoveContact_HookSmoke, 1 passed / 0 failed / 0 skipped |
| Original exit | test-receipt pythonOriginalExit 0 and overallExit 0; static zsh -n and git diff --check exits 0 | unity.exit 0; snapshot.exit 0; restore.exit 0 |
| Guard | N/A: Python and shell syntax work, no Unity/Player/profile operation claimed | editor scope; touched 0, verified 1, extraKeys 0, outOfScopeChanged 0 |

S-01 tests ran with edited source atop base HEAD before the commit; the recorded source hashes equal the later candidate exactly. This is an adequate code-byte binding, but not an executed RC chain. Source worktree clean fields are owner claims; no fresh git status was invoked because the LFS filter has side effects. S-03 tested-change.patch is byte-identical to Git's binary diff base→candidate for the two changed paths.

## S-01 review

Correct changes:

- rc.sh restricts the DEC-091 seed to 61 or 26, and final aggregation cannot accidentally inherit the report-only dispatch mode.
- rc_aggregate.py stage list now includes release-library and soak-stability-61 / soak-stability-26; the stale soak-exact stage is removed.
- The RC-only adapter uses regression.sh's existing DD_AGGREGATE hook, invokes the original aggregate, preserves its original pass/gates and exit, and changes only bot.rounds to report-only. Other *present* false gates still reject the adapter, including a false optional HUD probe.
- wp064_gate.py uses 5120 MB under DEC-093; existing finite/type/mode checks remain. Soak seed pair is exactly {61,26}; repeated resolved real-report directories cannot count as five distinct reports.
- Optional EditMode/PlayMode SKIPPED policy is explicitly unchanged. Existing optional-probe skip issues are not resolved or reclassified by this atom.

**P1 — missing mandatory gate sets can be accepted.** Candidate tools/qa/rc_aggregate.py:70-77 checks that bot.rounds exists and all *remaining present* gate pass fields are True. It never requires the mandatory M3 gates to be present or unique. A gates list containing only bot.rounds=False and readability.tierEnvContrast.Ultra=True passes m3_acceptance for a normal typed bot summary. The final aggregate's separate Ultra predicate also passes that truncated report. Missing editmode.qa, spawnmix, bot allocation, independent perf-run integrity/metrics/mean/p99, HUD cost and armor-form gates are not rejected.

The supplied test_rc_aggregate.py positive fixture itself has only bot.rounds, perf.mean and one Ultra gate; its successful CLI test demonstrates the gap without a new test execution by this auditor. Its adapter tests mock the original aggregate subprocess; they do not establish that every original producer gate is retained or required.

**P1 — closure validation accepts a handcrafted partial receipt.** Candidate rc_aggregate.py:99-111 requires only twelve soak/release-library check names. It trusts closure.pass=True and all present checks passing, but does not require build/dev/release, exact binary provenance, git clean/commit/LFS, perf attempt/ledger/CSV/host/40-load, smoke, log and full normal-report attempt checks. The test's accepted closure fixture omits these entirely. A truthy free-text realBotReport plus raw stability summaries and stage exit files suffice; no binary, required player log, memory samples or immutable attempt exists in that positive fixture. release/library-provenance.json in the fixture contains only schema/phase/head, also lacking actual Assets/Library content.

Under a correctly executed intact producer, wp064_gate.py still produces its complete strict check list and all nonzero stage exits block RC. The findings concern the final acceptance reader rejecting incomplete/replaced evidence; they do not prove a live real RC was falsely accepted. However, this atom should not receive a strict fail-closed PASS while an incomplete report is accepted by its own success fixture.

Recommended bounded correction: require the complete mandatory M3/closure gate identities for the configured perf runs, reject duplicate/unknown substitution, validate adapter original-exit/report-only metadata and report shape, and bind raw release/stability/normal-run evidence rather than accepting a minimal marker-only closure. Add negative cases that remove each mandatory gate/check, empty/corrupt the bot gate, substitute a fake minimal closure, or remove binary/log/memory/attempt provenance. Preserve DEC-091's report-only real-bot deaths and the existing optional-probe policy. This calls for Python/static validation, not a new Unity/RC run.

## S-03 review

The only production-file difference is an opt-in UNITY_EDITOR diagnostic hook around the existing normal CharacterController.Move and ClampToWalkable calls, plus a local CollisionFlags variable. After stripping UNITY_EDITOR additions and normalizing that local result variable, ZombieEnemy.cs is textually identical to base outside comments/whitespace. The ten other manifest sources — Factory, profile/UI isolation, crowd, NavPathFollower, world scene, NavMesh, spawn JSON, greybox builder and placement asset — have unchanged Git blobs from base.

One factory Standard body at I_PlazaSouthWest (-14, 0.0811535716, -18) was selected after grounding and a complete path. The test deliberately disables its local director to isolate one normal body; this is identified as smoke plumbing, not the natural 25-second/12-body C1 fixture.

Raw c1-hook.json independently gives:

- hookSmoke=true, stationSeconds=0, stallSeconds=0, stalls=0; outcome explicitly C1 CAUSE UNKNOWN.
- phases [0,1,2], frames [28,28,28], Move callId 1, lifeId 3; contact callback call/frame/time match the selected Move.
- before→Move and before→postClamp deltas both (0.0081443786621, 0, -0.0065460205078); requested delta (0.0081440480426, -0.0077403341420, -0.0065471753478).
- grounded before/after and flags Below(4); a single enabled non-trigger Environment BoxCollider callback with upward normal. contact.moveLength=0.3577403426 is the controller's internal sweep value, not the requested or achieved displacement.
- Actual collider instance 42282 at [AP005 World]/Greybox v6/Ground, GlobalObjectId scene GUID 2afaebde573d0428c96966afda288bb6 / targetObjectId 790591239.

World_V6_Greybox.unity bytes hash 101511ce0ca0495e03eaead8803d91ae5d8895bd9481d0e5ecf6bea4f0254da5 matches the manifest, candidate and loaded-scene record. Its .meta GUID matches; YAML BoxCollider &790591239 refers to GameObject 790591236 with correspondingSource/prefabInstance/prefabAsset all zero. This establishes a directly scene-authored Ground collider, not an AP-023 or blocked-C1 source. No prefab source exists for this collider; the UNKNOWN prefab fields in raw observation are correctly bounded by the explicit scene binding.

The hook asserts process-local profile/UI/telemetry paths before SceneLoad. guard.log agrees with the three original exit files. playmode.log contains no error CS lines; XML count/name/result and hook output agree. ResetHook runs from finally and TearDown; the controller hook is per-selected-body, absent from the Player compile. No source change to collision/world/nav/spawn/crowd rules was found.

Boundary to preserve on intake: no production cause fix, no transfer of historical 12spawn/6reached/0stall result to fresh acceptance, no natural C1 retake, no perf/RC claim. Default smoke opts out using Assert.Ignore; it adds a new default SKIPPED case outside the current RC PlayMode skip allowlist. Main-ci's core fixtures do not include V6AiRoundTests, but a full PlayMode RC would reject that skip under the unchanged policy unless the exact diagnostic opt-in/exclusion policy is resolved by the acceptance owner. Do not silently loosen gates to accommodate it.

## Evidence paths

S-01 root: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/rc-tools-main-20260929T040635Z

- delivery-receipt.json, test-receipt.json, python-tests.xml, python-tests.log, optional-probe-proposal.md
- Exact candidate review lines: tools/qa/rc_aggregate.py:70-77, 99-116, 154-165 at e8dc425bbdea74b25a9c40b7ee0bdfc20397df03

S-03 root: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-c1-intake-r2-20260929T035943Z/s03-c1-hook-r2-20260929T040509Z-38424

- receipt.json, artifact-hashes.json, input-bindings.json, tested-change.patch
- playmode.xml, playmode.log, c1-hook.json, runtime-scene-source-binding.json
- unity.exit, snapshot.exit, restore.exit, guard.log, slot.log

Main index maintenance is outside this review. Root reported successful preserved-lock maintenance at /private/tmp/dd-main-index-maintenance-20260929-f865cogv with no HEAD/index change; this report does not independently claim to have executed or verified that operation.
