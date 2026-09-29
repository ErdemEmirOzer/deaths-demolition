# C-00 independent read-only S09 intake — 2026-09-29

Decision: ACCEPT the scoped production Boot watchdog change `88c6f69c5366a4fc2b8b9ddc19e03fcd5fe19aa9` for C-00 integration. No blocking defect found in the added BootRoutine logic. This is not a whole-UI/main-CI/Player acceptance. All checks below inspected existing committed objects, source, and receipts; no Unity, test, build, fetch, integration, status, lock, or worktree mutation was performed. Only this /private/tmp report was written.

## Identity and source binding

- Exact one parent: `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`.
- Exact scope: PrototypeUiFlow.cs (27 additions solely in BootRoutine), LoadingWatchdogTests.cs (224 new lines), LoadingWatchdogTests.cs.meta (2 new lines). No scene/asset/font/string/default/Core/Input/Data changes in the commit.
- Handoff SHA256: `80aaa74ded39ca2bd66c7c5ea5b23abf0e2c1f01ee42de8839aa566dacf5f53e`.
- All 17 artifact hashes listed by the handoff independently match existing files. All 3 source SHA256 values independently match candidate Git blobs, the owner's live checkout, and the pre-run source receipt. That receipt records edited source atop parent 3c; the subsequent committed candidate has the same bytes. This is valid changed-source test binding, not a claim that the run's original HEAD already contained the later commit.
- The commit.patch's diff body byte-matches `git diff --binary base candidate`; its additional commit header explains why the whole file differs from a bare diff.
- PrototypeUiFlow SHA256: `9f87da08b4269a8657d270acfc4b2d326ad6017c3e4f434bc25deb8233c37a5e`.
- Test SHA256: `4c1504f96c5b13eb109abc311b1bd015cac6e26c582b011aea31a876a1767442`.
- Test meta SHA256: `2cd136013b5124eb524a429f36cd595fd305ca75dbf113ddc33613b6c890df5f`.
- The owner's `root_independent_*` fields and root-review artifact are owner-side claims. The acceptance above comes from this C-00 independent read, not those labels. Live checkout cleanliness was not rechecked with git status; artifact status-before/after files have equal bytes.

## Production semantics

BootRoutine candidate lines 503–535 mirror the existing ordinary LoadRoutine watchdog: unscaled time; progress must advance by >0.001 before resetting lastProgressAt; a one-time notice after >10 seconds; a one-time stalled, QUIT-only modal after >60 seconds. Constants, defaults, ordinary LoadRoutine, choreography, loaded-progress boundary, minimum handoff time, and activation-generation guard remain unchanged. The default/artificial-delay-free ready path follows the same preexisting loaded/hold/handoff conditions; the new variables alone do not delay activation.

The added HideError block at candidate lines 560–563 runs before Activate, ReleaseSimulation, and allowSceneActivation=true. It removes the error overlay and restores the hidden device while the same async operation is recovered. It does not cancel or replace an activation-held operation. s_loading still blocks a second request while that operation is pending.

UiCurtain.cs and UiModal.cs are unchanged Git blobs relative to the base. UiCurtain.ShowError(scene, true, null, null) supplies Application.Quit as the sole primary callback, hides the device and stall notice, and opens the modal. HideError destroys the modal and restores device visibility. No callback new to production code is introduced. The 60s modal is reached after normal boot handoff, so the boot-skip polling does not become a new modal cancel route.

Residual scope: the watchdog covers lack of reported progress in the pre-activation loop. The unchanged post-activation `while (!op.isDone)` and the unchanged Boot `op == null` handling are outside this fix. As with the ordinary path, an advancing load can reset the stall timer. This is parity with existing behavior, not a guarantee against all engine freezes/native load failures.

## Existing physical run verified

XML: result Passed, actual 4 test-case nodes, passed4 failed0 skipped0 inconclusive0. Start 04:15:01Z, end 04:17:04Z, duration 122.9703926 seconds. Unity wrapper wall time was 234 seconds including import/startup.

| Actual test | Result | XML seconds | Log observations |
|---|---:|---:|---|
| Boot_AutomaticWatchdog_10sNotice_60sQuitOnly_Recovery | PASS | 60.855022 | MainMenu notice10.172s; error60.197s; recovery errorOpen0 loading0 covering0 |
| ErrorModal_EN_PrimarySubmit_CancelIgnored_CopyFits | PASS | 0.411305 | focused Submit callback1; textScale1.25; TMP truncatedFalse and all meshes fit |
| ErrorModal_TR_PrimarySubmit_CancelIgnored_CopyFits | PASS | 0.393669 | focused Submit callback1; textScale1.25; TMP truncatedFalse and all meshes fit |
| Transition_AutomaticWatchdog_10sNotice_60sQuitOnly_Recovery | PASS | 61.245803 | Selection notice10.332s; error60.318s; recovery errorOpen0 loading0 covering0 |

Summary/original Unity timeline exit0, compiler error CS count0, wrapper lease finished with wrapper_exit0. Independent line-anchored engine exception count0. Raw `Exception` word count18 comes from test-framework ExecuteEnumerableAndRecordExceptions stack metadata, not 18 runtime exceptions.

Guard snapshot0 and restore0; touched0 verified1 extraKeys0 outOfScopeChanged0. Guard before.txt == after.txt byte-for-byte. before-editor == after-editor == after-run-editor and before-player == after-run-player. Extra/out-of-scope manifests are empty. Profile hash in actual test logs is consistently `39fbaffbeefd536495e2dfc2b9984d23172f4cb9d7950324f81cba7b5002d9ac`; the tests independently compute it from the isolated profile bytes. The watchdog/recovery baseline is intentionally taken after legitimate ordinary screen entry/HINT persistence, so the proven claim is watchdog/modal/recovery do not change those bytes.

## Test quality and acceptance boundary

The two timing tests run real LoadSceneAsync operations and real unscaled/realtime clocks. They use the preexisting ArtificialLoadSeconds hook with 1e9 seconds to keep reported progress below 0.001 and activation held, wait through real 10/60 seconds, assert old scene/loading state, observe UI notice and modal, reject a second Load request, release the same operation, and assert destination/cover/modal/simulation-hold/profile recovery. No altered threshold, fake clock, canned completion, synthetic bot/seed outcome, or assertion of only the provided input substitutes for the watchdog outcome. Without the new Boot watchdog, its test would not observe ErrorOpen before its 80s deadline. That before-fix physical run was not performed and is not claimed.

The EN/TR cases invoke actual UI Submit on the focused primary and verify one injected callback call, modal closure, exact localized body/action labels, and rendered TMP geometry at TextScale1.25. Their primary callback is deliberately a counter; this proves event-to-callback wiring, not safe fallback scene navigation. Focused cancelHandler returns false and the modal stays open; UiModal's documented HandleCancel is explicitly called by owners and is not a per-frame input loop, so these tests do not prove physical Escape/B routing. QUIT action binding is source-proven, while real Player exit is not exercised.

Not newly proved: native SceneManager failure or op-null reproduction; actual safe fallback navigation; actual Player QUIT exit; hardware Escape/B; screenshots/whole-scene art direction; Reduced Motion/device matrix; performance/build size; Core gamefeel red closure. No manufactured expansion of acceptance is appropriate.

New LoadingWatchdogTests is not listed in current tools/ci-suites.txt (LoadingScreenTests is). Its 4/4 target receipt must remain attached as separate candidate evidence. A later full compile/main-CI receipt proves the integrated head for its configured suites, not automatically execution of these four new tests. No new main evidence was generated in this audit.

## Absolute evidence paths

- Candidate checkout: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/worktree/deaths-demolition
- Handoff: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-owner-handoff.json
- Source before run: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target-source-before.json
- Patch: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-commit.patch
- XML: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target.xml
- Unity log: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target.log (notice/recovery lines1514–1564, EN/TR1611–1762, transition1820–1869)
- Summary: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target-summary.txt
- Lease: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target-lease.json
- Guard: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-closure-main-r2-target-ug
- Wrapper source (read only): /private/tmp/s09-ui-closure-main-r2-codex.sh
