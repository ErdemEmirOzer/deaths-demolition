# Main / C-00 CI alignment audit — 2026-09-29

Scope: independent read-only audit by ci_audit. No test, build, fetch, integration, lock reclamation or production/worktree change was performed. This report is the only authorized output write.

## Verified CI evidence

| Execution | Result | Verified counts / boundary |
|---|---|---|
| Current main `3c0875e1e259cebae34e2cb496d03bb4e8dd9045` | NOT RUN | No matching compile/main-ci evidence found under the CI root, including the nested recovery output. Older SHA results cannot establish this main's health. |
| Compile `ff06d5793e1e9b703fa128b4692c74f05a59b714` | PASS | Unity exit 0, errorsCS 0, 2 compile-trace matches; 2026-09-28 23:01:01 UTC. |
| Main-ci `ff06d5793e1e9b703fa128b4692c74f05a59b714` | FAIL / RED | EditMode 1473 passed, 1 skipped, 0 failed; PlayMode 179 passed, 2 failed; CI exit 1. Summary UTC 2026-09-28T23:26:34Z. |
| Original candidate CI `eecfe54c88fc62c4595244d2a7a006398dd5c205` | INCOMPLETE / BLOCKED | EditMode 1473 passed, 1 skipped, 0 failed; no PlayMode XML, summary or original CI exit receipt. The missing results are not passing or failing test evidence. |
| Recovery candidate CI `eecfe54c88fc62c4595244d2a7a006398dd5c205` | FAIL / RED | Latest completed execution: EditMode 1473 passed, 1 skipped, 0 failed; PlayMode 180 passed, 1 failed; CI exit 1. Summary UTC 2026-09-29T02:05:31Z. This is a diagnostic candidate, not current main. |
| Main-ci `a17cb73daf5414930b2d924f827fff2e5906a9cc` | GREEN | Latest green recorded main-ci: EditMode 1468 passed, 1 skipped, 0 failed; PlayMode 181/181 passed. Summary UTC 2026-09-28T20:53:21Z. |

Counts were independently parsed from NUnit XML test-case elements and agree with summary.json. Main-ci runs full EditMode plus the core PlayMode fixtures listed in tools/ci-suites.txt; it does not run every PlayMode fixture.

The skipped case is `DD.Prototype.Tests.EditMode.SpawnGateCoverageProbe.V6_KapiKapsami_Arama`, with XML reason `probe: DD_WP076_PROBE=1`. Do not label 1473 passed + 1 skipped as 1474/1474 passed.

## Failures and ownership

Both failures map to S-02 using the current tools/ci-owners.tsv; no ci-signatures.tsv root-cause override was recorded.

| Failure identity | ff06 original | eec recovery |
|---|---|---|
| CoreCombatSliceTests.GameFeelOlaylari_FleshHitOlumVeOyuncuHasari_AyriSinyaller | FAIL: zombie not killed in 20 seconds | Same FAIL. XML output: 600 gate evaluations; ready 0; rise/head/ray rejected 0/279/321; damage/dressed 0/0. |
| WeaponFeelSliceTests.DEC082_AlmaSirasinda_HareketVeNisanKilitlenmez | FAIL: max speed 1.7919192E-05 versus >0.5 | PASS: 49 samples; max speed 6.2; accumulated travel 4.629541. |

NUnit seeds differ between the executions. Diagnostic instrumentation is not a source fix; DEC082 passing in recovery does not prove the original cause is closed. The recovery's geometry snapshots are not simultaneous, and no exact source defect is proven by this evidence.

## Guard / integrity

For ff06 and recovery, both inner and outer editor guard restore receipts are exit 0, touched 0, verified 1, extraKeys 0, outOfScopeChanged 0. ff06 completed provenance artifact hashes matched 13/13; recovery matched 16/16. Original interrupted candidate provenance hashes matched 9/9.

CI canonical checkout was observed clean at detached HEAD `3c7b4872ddc01c13d636c4c1fc7a4a4db2a7f40a`. Its active canonical lease filename was absent; the earlier owner record pid 36149 remained preserved in `.lease.quarantine-c00-20260929T014628820932Z`. The separate /private/tmp/dd-c00-compile-checkout-20260929 was at ff06 with no active canonical lease. No lease was changed by this audit.

## Tool contract inconsistencies

- tools/qa/rc_aggregate.py:70 requires a stage `soak-exact`. tools/qa/rc.sh:136-137 writes `soak-stability-61` and `soak-stability-26`, not `soak-exact`. A clean new RC output cannot satisfy the aggregate stage list.
- rc_aggregate.py:95 still gates the real bot's requested rounds / zero deaths / pass=true. rc.sh treats real-bot runs as reports and the two stability runs as mandatory under DEC-091.
- rc.sh:116 runs `release-library`; rc_aggregate.py's stage list omits it.
- rc_aggregate.py requires zero EditMode skips; main-ci summary treats Skipped and Inconclusive as skipped and permits a green result if there are no reds/compile errors and both XML files exist without watchdog. The optional probe therefore has different acceptance treatment.
- tools/qa/wp064_gate.py:16 and its footprint checks enforce 550 MB. planning/00-PROJECT-PLAN.md:165 DEC-093 replaces the prior cap with 5 GB, and tools/build-report.sh:17 already uses 5120 MB.

These are evidence/acceptance tool findings for S-01 with S-10 contract review. They should be corrected before an expensive RC chain is launched.

## Absolute evidence paths

- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/compile-ff06d5793e-20260928T230045Z/timeline.txt
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/compile-ff06d5793e-20260928T230045Z/compile.log
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/summary.md
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/summary.json
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/editmode/editmode.xml
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/playmode/playmode.xml
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/s01-provenance.json
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/eecfe54c88fc62c4595244d2a7a006398dd5c205/s01-exit.json
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/s01-eecfe54c-recovery-20260929T014714Z/eecfe54c88fc62c4595244d2a7a006398dd5c205/summary.md
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/s01-eecfe54c-recovery-20260929T014714Z/eecfe54c88fc62c4595244d2a7a006398dd5c205/playmode/playmode.xml
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/s01-eecfe54c-recovery-20260929T014714Z/eecfe54c88fc62c4595244d2a7a006398dd5c205/s01-provenance.json
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/s01-eecfe54c-recovery-20260929T014714Z/eecfe54c88fc62c4595244d2a7a006398dd5c205/s01-causal-comparison.md
- /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/a17cb73daf5414930b2d924f827fff2e5906a9cc/summary.md

Next evidence: after bounded source fixes/integration, run one compile-check and one main-ci on the exact final main SHA in a clean detached checkout with the existing slots and editor guard. Preserve old output; use a separate output root for any same-SHA recovery. This audit did not authorize or perform a rerun of an old candidate.

## Follow-up: main index lock maintenance review (read-only)

Observed 2026-09-29 approximately 06:59 TR: main `.git/index.lock` regular file, size 0, inode 49785778, device 16777233, birth/mtime 06:31:00 +0300, ctime 06:31:03 +0300. Main `.git/index` inode 49767333, size 1530740, mtime 06:24:37 +0300. `/private/tmp` and main `.git` have the same filesystem device 16777233, so a same-device rename can be atomic. An escalated read-only lsof check found no current open holders for these two files (exit 1).

A concurrent process snapshot did contain short-lived git status/filter/diff commands from the ChatGPT app pid 46035 and Codex runner pid 46078. These specific child pids were gone at the next snapshot. The first snapshot therefore did not establish absence of Git activity. No owner can be inferred from the 0-byte size or age alone. No lock was removed, renamed or acquired by this auditor.

No MERGE_HEAD, CHERRY_PICK_HEAD, REBASE_HEAD, REVERT_HEAD, sequencer, rebase-merge or rebase-apply existed. A read-only cached diff-index was empty. ls-files --others reported exactly the 13 new `planning/work-packages/*-MAIN-20260929.md` coordinator prompts. A complete worktree-clean result was not obtained by this auditor: even with GIT_OPTIONAL_LOCKS=0, git status invoked the LFS clean filter, which attempted a .git/lfs/tmp write and failed under the sandbox.

Main index.lock is a checkout index transaction marker, distinct from the canonical CI `.dd-ci-<hash>.lease` owner/token protocol. A narrowly scoped C-00 maintenance decision for the former should not reclaim or modify the latter.

Before any maintenance rename: ensure coordinator-controlled writers are quiescent; capture metadata, exact bytes/hash, HEAD, original index hash and staged state; use lstat/O_NOFOLLOW to reject symlinks or non-regular/replaced files; recheck immediately before the operation. Prefer an exclusive no-overwrite rename to a unique evidence destination; preserve the inode and verify the moved object equals the captured identity. If size/identity changes, a holder appears or a new lock appears, stop and preserve evidence. A process/lsof snapshot cannot eliminate the race with an automatic new Git operation. An empty old lock is not proof of no live writer. Do not overwrite a newly acquired replacement lock when restoring.

integration effects: integrate.sh --apply requires a genuinely clean main and source worktree, performs ff-only merge or cherry-pick --no-commit, automatically resolves designated planning files, commits, creates an annotated tag and by default runs post-integrate.sh. Its failure rollback enumerates *every dirty path*, restoring tracked paths and removing untracked paths. Thus the 13 coordinator prompt files must be deliberately staged/committed before integration; do not bypass the dirty gate or use add -A. Review exact paths and an explicit documentation-only commit.

integrate.sh status gates pipe `git status --porcelain` to wc without checking Git's status. It uses set -u, not pipefail. A failing status command can produce a false zero count; independently require git status exit 0 before accepting a clean gate. post-integrate.sh writes backup/reports outside the checkout, copies LFS objects, performs cleanup dry-run, disk/LFS reports and footprint copying. backup-repo.sh creates a full bundle/LFS backup and, after verification, prunes older tool-created backups beyond the default keep=3. These are additional side effects to account for; --no-post postpones them, --compile starts a Unity check. No integration tool was run by this audit.

Resource snapshot at approximately 07:02 TR: available disk 23794904 KiB (22.69 GiB, above the 15 GiB launch threshold), swap used 562 MB. Direct read-only slot-file inventory showed one unity and one player record for `s05-sky-intake-r2-20260929`, pid 35887, start Tue Sep 29 07:02:21 2026; blender/smoke and all four queues had zero records. This is a point-in-time inventory, not a liveness guarantee. `slot.sh status` was deliberately not executed because its initialization creates directories and its stale cleanup can remove slot files.
