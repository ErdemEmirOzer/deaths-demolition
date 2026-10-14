# Code review R1-B — correctness review of the post-R1-A main commits

- **Range reviewed:** `a7f25570..a2971897` (origin/main as of 2026-09-24). The requested base tag `review-r1-a-int` does not exist on the remote, so the base is `a7f25570`, the R1-A review commit on main. Every commit after it was reviewed; 167 C# files changed.
- **Method:** static reading only. Unity was not run, LFS binaries were not fetched, no game code was changed.
- **Paths:** `P/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/`. Line numbers are at HEAD `a2971897`.
- **Verdicts:** CONFIRMED = the code path was traced end to end. PLAUSIBLE = depends on a caller that doesn't exist yet, on tuning, or on a design reading.

## Headline: user-data safety (WP-077 B2a)

The v4→v5 migration is lossless, idempotent and crash-safe, and exactly-once run commits still hold. No path grants Ash twice was found, and no path loses user data. The data-safety findings are one medium UI regression (P1) and some low edge cases (P2–P6). Evidence is in "Checked and sound" §A.

---

## Findings (ranked by severity)

### HIGH

**RT1 — Hearth (Ocak), Purse (Kese), keystones and Special Links can't be reached in real play.** CONFIRMED
- **Where:** `P/Rounds/RunTree/RunTreeState.cs:334` (`MarkOwned`), `:402` (`OnSpecialPickedUp`), `:139-144` (Hearth prerequisite); `P/Rounds/RunTree/RunTreeCatalog.cs:193` (`s_live`).
- **Defect:** No game code (only tests) calls `RunTreeState.MarkOwned` or `RunTreeState.OnSpecialPickedUp`, and keystone effect ids aren't in the live set, so no node outside the tiers can ever be owned.
- **Scenario:** Hearth needs an owned weapon keystone, so `TryStokeHearth` always returns `NotAvailable`. Purse never pays. Links always show "no Special yet".
  - `RunTreeTests` pass only because they call `MarkOwned` directly (`RunTreeTests.cs:196, 229`).
  - The `OnSpecialPickedUp` handlers that do match in a grep belong to HUD, telemetry and achievements, not to `RunTreeState`.
- **Fix:** Add a shop node-purchase command that spends gold and grants the node in one step, and subscribe `WeaponEvents.SpecialWeaponPickedUp` → `Tree.OnSpecialPickedUp`. Otherwise, document that these nodes are inert until the S-09 tree screen lands.

**RT2 — `MarkOwned` enforces none of the tree rules (seven-state rules are only checked when read).** CONFIRMED
- **Where:** `P/Rounds/RunTree/RunTreeState.cs:334-342`.
- **Defect:** It sets `_owned[i]` without checking prerequisites, fork exclusion, whether the effect is live, or gold.
- **Scenario:** Once the tree screen wires this up, both forks of an exclusive pair can be owned, or a capstone with no seal.
- **Fix:** In the purchase command, require `Status(i) == Available`, and do the spend and the grant together, the same way `TryStokeHearth` does.

**RT3 — Hearth can't be bought when the shop is `PoolExhausted` or `NotEnoughEligibleBranches`, which is exactly when it is the only gold sink.** CONFIRMED
- **Where:** `P/Rounds/RoundUpgradeShop.cs:901`.
- **Defect:** `TryStokeHearth` requires `Status == UpgradeOfferStatus.Ready`, and `BuildOffer` sets a non-Ready status whenever fewer than 3 tiers can be drawn.
- **Scenario:** Late in a run, with branch tiers maxed, the player has gold and nothing to spend it on. The Hearth button refuses. Direction §2.5 says pool exhaustion should happen only after Hearth reaches 20.
- **Fix:** Allow Hearth whenever the shop is open, whatever the status. Or report `PoolExhausted` only once Hearth is also at its cap.

**RT4 — A reroll can replace the pinned (Nişan) card, which breaks the pin guarantee.** CONFIRMED
- **Where:** `P/Rounds/UpgradeOfferPolicy.cs:310-320` (uniform slot draw), called from `P/Rounds/RoundUpgradeShop.cs:674`. The pin is injected at `RoundUpgradeShop.cs:952-975`, often into the last slot (`:974`).
- **Defect:** The reroll picks its replacement slots at random and doesn't know which card is pinned.
- **Scenario:** Pin a tier, then reroll. With one or two of three slots replaced, there is roughly a 1/3–2/3 chance the pinned card is lost. The design says "reroll does not change it".
- **Fix:** Pass the pinned slot to `Reroll`, exclude it from the slot draw, and exclude its branch from the replacements.

### MEDIUM

**W1 — Third person: the 12° parallax guard (R1-A #1) stops close shots from reaching the crosshair.** PLAUSIBLE (depends on the rig defaults)
- **Where:** `P/Weapons/PlayerWeaponBinding.cs:194` (`LookParallaxMaxDegrees = 12f`, `:174`).
- **Defect:** When the muzzle→aim-point line is more than 12° off the camera ray, the shot flies parallel to the camera ray, starting from the muzzle.
- **Scenario:** With the rig defaults (`PrototypeCameraRig.cs:99-105`: 0.55 m shoulder, 2.8 m distance), the muzzle is about 0.5 m off the camera ray, so the limit trips for targets nearer than about 2.4 m. A crosshair on a zombie's head at 2 m hits about 0.4–0.5 m away, in the chest or a miss.
- **Fix:** Fall back only when `Dot ≤ 0`, or when the aim point is closer along the ray than the muzzle depth plus a margin. Otherwise keep the muzzle→point line.

**W2 — lmg_2 counts as a Heavy weapon, so full auto gets the shotgun's directional camera punch.** CONFIRMED
- **Where:** `P/Weapons/ClassicWeaponRuntime.cs:170` (`StaggerImpulse >= 0.5f` → Heavy); lmg_2 impulse 0.50 at `P/Weapons/Editor/ArsenalAssetBuilder.cs:190`; `P/Weapons/WeaponController.cs:821`.
- **Defect:** At 420 RPM that is 7 directional kicks per second along `-aim`.
- **Scenario:** Holding the trigger makes the camera drift, which REQ-126 forbids (see the warning at `CameraShakeBridge.cs:43-47`). Impact and muzzle VFX are also scaled ×1.5.
- **Fix:** Decide Heavy from `PelletCount > 1` or a non-auto fire mode, not from the impulse alone.

**RT5 — The UI can show a "guaranteed next offer" that is never offered.** CONFIRMED
- **Where:** `P/Rounds/RunTree/RunTreeSnapshot.cs:180` vs `P/Rounds/RoundUpgradeShop.cs:952-975`.
- **Defect:** `PinReachableIndex` reports non-tier steps (for example Purse) as guaranteed, but `ApplyPinGuarantee` only injects tier steps.
- **Scenario:** Pin Purse. The snapshot says it is guaranteed next, and the next offer doesn't contain it.
- **Fix:** Report −1 for steps that can't be put on a card, or advance to the first tier step.

**RT6 — The pin has no once-per-round limit (K2) and accepts nodes that can't be reached.** CONFIRMED
- **Where:** `P/Rounds/RoundUpgradeShop.cs:922-937`, `P/Rounds/RunTree/RunTreeState.cs:260` (`SetPin`), `:288-293` (`AppendPath`).
- **Defect:** Excluded, Purchased, maxed or effect-pending targets are accepted, and the path doesn't check exclusion.
- **Scenario:** After owning fork X, pin fork Y. Every later offer is forced to carry tier cards toward a node that can never be bought.
- **Fix:** Add a per-round pin counter, reset in `OnRoundEnd`. Reject targets that are Excluded, Purchased, MaxTier or Locked, and clear the pin when its target becomes excluded.

**RT7 — A new run keeps the previous run's tree until the first shop opens, so the run summary and profile milestones can read stale data.** CONFIRMED (hidden today by RT1)
- **Where:** `P/Rounds/RunDirector.cs:785-788` (see also `:217`); read at `P/Profile/ProfileProgression.cs:224-229`.
- **Scenario:** Die in round 1 of run 2. The summary carries run 1's capstones and Link II, and the one-time "capstone" and "link2" Ash milestones are granted from them. This is a user-data path once RT1 is fixed.
- **Fix:** Call `Tree.Begin(runId, …)` at run start, or read the tree only when `Tree.RunId == _runId`.

**P1 — Weapon-unlock rows on the Results screen lose their icon after the WP-069 key rename.** CONFIRMED
- **Where:** `P/Profile/RunResultsData.cs:268` → `P/UI/Runtime/Presentation/ResultsViewModel.cs:179`.
- **Defect:** The glyph id is now `"smg_1"`, so the lookup is `"Weapon.smg_1"`. The manifest `AP012-ui-manifest.json:128/139/150` only has `Weapon.AssaultRifle`, `Weapon.AutoSmg` and `Weapon.CombatShotgun`.
- **Scenario:** Every weapon unlock shows the generic Rare medal.
- **Fix:** Use `WeaponIdMigration.TryGetRuntimeId(key)` and then `UiArt.WeaponIcon(id)`.

**A1 — One slow frame stops and restarts the hybrid auto-fire loop mid-string.** CONFIRMED
- **Where:** `P/Audio/Runtime/WeaponFireAudio.cs:277` (release check) and `:243-244`.
- **Defect:** "Trigger released" is inferred from `Time.time` since the last shot exceeding 1.6 periods (about 107 ms for SMG1). There is no execution order between the director and the `WeaponController`.
- **Scenario:** A 120 ms GC hitch while SMG1 is held plays the tail, fades the loop, then restarts it with a full transient. Below about 9 fps this repeats every frame. The PlayMode test `AtesV3_HibritDongu…` has the same timing margin on a slow CI agent.
- **Fix:** Also require `Time.frameCount - lastShotFrame >= 2`, or have the weapon report trigger-up explicitly.

**AI1 — Chain-lightning hop targets carry no life identity, but the hops land later.** PLAUSIBLE (no production caller of `Resolve`/`ApplyShock` yet)
- **Where:** `P/Enemies/ChainTargetResolver.cs:22-23, :43`; `P/Enemies/ZombieShock.cs:37, :83`.
- **Scenario:** Body #3 is evicted or removed out of world before its hop lands. The pooled instance respawns as a new zombie, and the late hop shocks and damages a zombie that was never in the chain.
- **Fix:** Store the spawn ticket (life id) with each resolved target, and have `ApplyShock` and the hop damage refuse a mismatch. Or apply all hops in one frame.

**A2 — Duplicate suppression (40 ms, 1.6 m) drops fire transients at high fire-rate tiers.** CONFIRMED
- **Where:** `P/Feedback/AudioEvents.cs:176-178` (`allowSuppression: true`), window at `P/Core/Config/FeedbackConfigSection.cs:65`, rate tier at `P/Weapons/WeaponUpgradeTuning.cs:141` (up to 2.2×).
- **Scenario:** SMG1 at 2.2× (about 30 ms per shot) loses roughly every other shot's audio and loop pitch count. At about 1.6× and 60 fps, some shots are dropped too.
- **Fix:** Skip suppression for `ClassicWeaponFire` raised by the weapon. The voice pool's retrigger and instance limits already cap it.

### LOW

| # | Where | Defect → scenario | Fix | Verdict |
|---|---|---|---|---|
| P2 | `P/Profile/ProfileService.cs:379-382`, `:397`, `:404`; `P/UI/Runtime/Flow/PrototypeUiFlow.cs:746` | At the L50 cap the requirement is `int.MaxValue`; the UI shows "/2147483647 XP" and `XpIntoLevel` keeps banking. K11 keeps v4 profiles at ≥ L50, so they see it immediately. A later cap raise gives 512 level-ups in one transaction. | Add `IsAtCap` → show "MAX"; at cap, bank into `TotalXpEarned` only. | CONFIRMED |
| P3 | `P/Core/Editor/PrototypeCoreValidation.cs:63-64`; `P/Profile/RunRewardReport.cs:91` | The validator still expects the old curve (100/325; now 110/560). The empty-report default is 100. The "Validate Prototype Core" menu reports 2 failures. | Update the values; use `LevelRequirement(1)`. | CONFIRMED |
| P4 | `P/Profile/ProfileMigration.cs:113-130` (called at `:186`) | `Parse`/`Migrate` used to be pure; they now call `PrototypeConfig.Active` (`Resources.Load`) and the achievement catalog, with no try/catch. `ProfileStore.ReadFile` is documented as never throwing. A catalog exception, or an off-main-thread read, makes Load/Save throw instead of recovering. The back-grant is also recalculated on every launch with the current config until the first v5 save (it is assigned, never doubled). | Wrap in try/catch with an XP-only fallback, or inject config and catalog. | PLAUSIBLE |
| P5 | `P/Achievements/UnlockCatalog.cs:273` vs `:298` | The duplicate check runs before old names are mapped to keys, so `"AutoSmg"` and `"smg_1"` both load. The grant still happens once. | Normalise the id before `seen.Contains`. | CONFIRMED |
| P6 | `P/Profile/ProfileMigration.cs:188-190` | A forged `SaveVersion:4` document that already contains `MetaNodes` keeps them with `AshSpentTotal = 0`, i.e. free nodes. A real v4 build can't write these fields. | When `from < 5`, clear `MetaNodes`, `MetaRespecCount`, `MetaSeen`, `AshMilestones` and `WeaponMastery`. | PLAUSIBLE |
| P7 | `P/Profile/ProfileProgression.cs:245` | Mastery credits every kill in the run (including Special and unattributed kills) to the starting classic weapon. | Confirm against the M1–M5 intent. | PLAUSIBLE (design) |
| RT8 | `P/Rounds/RunTree/RunTreeState.cs:141-143` | Hearth needs a weapon-lane keystone; §2.1 says any keystone. | Count both lanes, or record the narrower rule as a decision. | CONFIRMED vs doc |
| RT9 | `P/Rounds/RunTree/RunTreeState.cs:232-235`, `:354` | After 3 stokes in a round, Hearth still shows Available with a price, but the command returns `LimitReached`. | Add a per-round-limit state to the snapshot. | CONFIRMED |
| RT10 | `P/Rounds/RunTree/RunTreeState.cs:367` | With `build == null`, gold is taken and the stack counted, but `SetHearth` is never applied. | Refuse the purchase, or re-apply `SetHearth` when a build attaches. | PLAUSIBLE |
| W3 | `P/Weapons/WeaponController.cs:323, :420, :493`; `P/Weapons/ClassicWeaponRuntime.cs:485` | A burst survives a Special pickup or a TP↔FP blend, and the rest of it fires later with no trigger held. | `CancelBurst()` on Special start/end, blend and death. | CONFIRMED |
| W4 | `P/Weapons/ClassicWeaponRuntime.cs:585, :664-665` | lmg_2's settle bloom is in absolute degrees; entering ADS or a narrower mode can make the cone 0° (better than the 35 % floor) until the next shot. | Store the settle as a fraction of the base, or clamp when read. | CONFIRMED (math) |
| W5 | `P/Weapons/Projectiles/ProjectileSystem.cs:283`; `P/Weapons/Editor/ArsenalAssetBuilder.cs:190, :207` | `TargetsLeft = 1 + Penetration` → sr_1 hits 5 bodies and lmg_2 hits 3; the direction doc and the item copy say 4 and 2. | Decide, then fix either the code or DEC-097. | PLAUSIBLE |
| W6 | `P/Rounds/RunDirector.cs:236`; `P/Weapons/ClassicWeaponRuntime.cs:310` | A same-scene debug restart keeps the old reserve and magazine (`Max(_reserve, target)`). Production reloads the scene, so it doesn't happen there. | Call `ResetForNewRun()` in `StartRun`. | CONFIRMED (debug only) |
| W7 | `P/Weapons/WeaponMechanismTimeline.cs:37, :58`; `P/Weapons/WeaponController.cs:1152` | The fallback timeline always plays 3 shell seats whatever is queued. sg_2 (break action) gets the pump timeline. | Drive `ShellSeated` from the runtime's loaded-shell count. | CONFIRMED (presentation) |
| AI2 | `P/Characters/LocomotionLean.cs:59, :76` | After the ragdoll hand-off the Animator no longer writes the spine, but the decaying lean is still multiplied onto it every LateUpdate. It compounds to about 10× at 60 fps and 22× at 144 fps (tens of degrees), frame-rate dependent. | When the gate is off, zero it and return, or skip when `!_animator.enabled`. | CONFIRMED |
| AI3 | `P/Characters/FootIkAdapter.cs:157-165, :230-232` | The foot lock survives an IK-service gap (LOD, budget, rise) and snaps toward a stale world point, a pop of up to about 0.4 m. | `ClearLock()` when `!budget`, or on a frame gap. | CONFIRMED |
| AI4 | `P/Enemies/ZombieHitResponse.cs:99-100` | Explicit-Euler knockback depends on frame rate: the 0.6 m shove travels 0.72 m at 20 fps and 0.62 m at 144 fps. | Move by the eased delta `D·(e(t+dt)−e(t))`. | CONFIRMED |
| AI5 | `P/Enemies/ZombieCorpse.cs:43-49` | The corpse-ceiling `while` loop only progresses if `Despawn()` unregisters the entry. An entry with `IsSpawned == false` hangs the game. No concrete trigger exists today. | Remove the entry before calling `Despawn()`. | PLAUSIBLE |
| AI6 | `P/Characters/RagdollProxyPool.cs:24-27, :293` | `s_tierCap` is cached before the `QualityTiers.Changed` subscription, so an early tier change is lost. | Subscribe in the getter or in a `BeforeSceneLoad` hook. | PLAUSIBLE |
| AI7 | `P/Enemies/ZombieShock.cs:57` | `_shockUntil` starts at 0, so a kill in the first 0.15 s of app time is tagged Electrocuted. | Guard with `_shockUntil > 0f`. | CONFIRMED (trivial) |
| A3 | `P/Audio/Runtime/ImpactAudio.cs:97` vs `:123` | The legacy hold is gated on `ImpactV3StoneMedium` alone, but `OnImpact` gates per id. A partial catalog plays both the legacy and the v3 sound on one hit. | Return early in `OnImpact` when `!Active`. | PLAUSIBLE |
| A4 | `P/Audio/Runtime/ImpactAudio.cs:97` | When the hold queue is full (16), the legacy cue plays immediately and its v3 impact plays too: a double-play on shotgun or many-substep frames. | When full, drop the legacy cue, or raise the capacity. | PLAUSIBLE |
| A5 | `P/Audio/Runtime/AudioEventRouter.cs:684, :698` | If one weapon's v3 foley clip is missing, the event returns handled/None: silence instead of the A/B/C fallback from 369ca731. | Return `false` when the id exists but isn't produced. | PLAUSIBLE |
| A6 | `P/Weapons/ClassicWeaponRuntime.cs:517, :395, :438`; `P/Weapons/WeaponController.cs:658` | The procedural `ReloadStart`/`Insert`/`End` cues still play on top of the v3 mag and shell foley. This predates the diff. | Make those cues return nothing when v3 foley is produced. | PLAUSIBLE |

---

## R1-A fix verdicts

| R1-A # | Verdict |
|---|---|
| #1 parallax guard | **Real and complete as specified.** Covers TP and FP. Top-down keeps its "point ahead of the muzzle" test. Degenerate cases (zero line → camera ray; no ray → facing; miss → far point) are handled. The 12° tuning is the concern (W1). |
| #2 first segment inside a collider | **Real and complete** for bullets and pellets. A root→muzzle ray catches the collider the muzzle is inside, and pellets start from that point. Sphere sweeps report initial overlaps (`HitscanEngine.cs:298`). The test covers only the helper, not an end-to-end hit. |
| #5 ProjectileId on spawn / impact / expire | **Real and complete.** `Generation` is set once per spawn and never reused. It is on Spawned, both Impact paths (world stop, body stop), and Expired (evict / range / lifetime / `Clear` on unload). A `Clear` during a step is deferred and still raises Expired exactly once. Dropped pending spawns never raised Spawned. |
| #6 #8 #13 #14 #15 #16 #19 | Real (touched set 192; feel-router group stamp; deferred spawn/clear; `EnsureExists`; gravity 0 in the builder and assets; round cone in the aim frame; clone copies live weapons). |
| #3 ragdoll pool across a scene change | **Fixed.** DDOL pool (`RagdollProxyPool.cs:298`), `root == null` guard (`:187`), proxies deactivated in `OnDestroy` (`:356`); regression test added. |
| #9 attack range fit | **Fixed.** `RangeFit` is 0 outside [min, MaxReach], and `Select` fails when the total is 0. |
| #10 grab escape reads the ungoverned force | **Fixed.** `UngovernedImpactForce` is set before `Govern` (`HitscanEngine.cs:781`) and read by the grab escape (`ZombieEnemy.cs:1318`). |
| #11 slot board statics | **Fixed in code, no regression test.** |
| #12 ragdoll cap follows the tier; far LOD gets the push only | **Fixed** (AI6 caveat). |

---

## Checked and sound

### A. Profile v5 / user data (priority 1)

- **Lossless v4 migration.** The `from < 5` block (`ProfileMigration.cs:180-194`) only initialises Ash and meta fields.
  - Runs, statistics, kills, achievements, branches, content, title, badge, seen counters, lineage, ordinal and `LastSavedUtc` are untouched.
  - `Sanitize` maps M3 weapon names to WP-069 keys (`ProfileData.cs:264-270`), then `Clean` removes duplicates such as "AssaultRifle" + "ar_1".
  - `WeaponIdMigration.FromLegacyName` (`Core/Weapons/Arsenal.cs:144`) maps ids 0/1/2 and passes unknown names through as data.
  - `Clone` copies all nine new fields.
  - For the reference profile (SaveVersion 4, 14 runs, 4 achievements, ordinal 2), all of these come through unchanged. Lineage is validated before `Migrate` (`:102`).
- **K11: the level never drops.** Nothing recalculates the level from XP; `ProfileLevel` and `XpIntoLevel` are stored.
  - New minus old requirement = 10L² + 25L − 25 > 0 for L ≥ 1, so a stored `XpIntoLevel` never crosses a new threshold.
  - The cap stops at 50, and there is no int overflow below it (L50 needs 30,000). The back-grant sums in `long` and saturates.
- **Back-grant.** floor(`TotalXpEarned` / `XpPerAsh`) + rarity Ash per catalogued achievement.
  - It is **assigned** (`AshEarnedTotal = ash`), not added, so re-running it never doubles.
  - After a v5 save, `from == 5` skips it.
  - `Migrate` is byte-idempotent on the same object (`ProfileV5Tests.cs:140-142`).
- **Crash safety.** `ProfileStore.cs` has no changes since a7f25570.
  - A v4 file is migrated in memory only; the disk stays v4 until the first save. A crash before that means the next launch migrates again.
  - Save step 4 copies the validated v4 primary to `.bak` atomically. It then writes the candidate, calls `Flush(true)`, and runs `File.Replace`.
  - Restoring a v4 `.bak` re-migrates it with assigned Ash, which is a consistent rollback, not a double grant.
  - A corrupt primary is quarantined as `.corrupt.N.json`, then the v4 or v5 backup is adopted.
- **Exactly-once run commit (WP-009H).** `ApplyAsh` runs on the candidate copy before the single `ProfileStore.Save`, in the same candidate as the ordinal advance (`ProfileService.cs:659, :681`).
  - A failed save leaves `s_data` untouched and clears `AshEarned`.
  - A duplicate token returns the stored receipt without re-applying.
  - Milestones use `Grant` on the candidate. The `ddc1` token schema is independent of the save version.
- **Downgrade.** The a7f25570-era build refuses `SaveVersion > 4` as `FutureVersion`: it keeps the primary and side files untouched, plays on an in-memory profile, and locks saving. The v5 file is **not** quarantined or overwritten; progress made in the old build is simply not saved. HEAD handles v6+ the same way.
- **JsonUtility.** v4 files always carry `SaveVersion`. Missing lists are empty via field initialisers. `List<WeaponMasteryEntry>` serializes. No dictionaries. Logs use InvariantCulture and timestamps use `"o"`.

### B. Run tree (priority 2)

- **Hearth price.** `50 + 30·HearthStacks`, with a per-run, zero-based count: 50/80/110…, 20 stacks = 6,700, matching §2.6. The cap of 20 and the per-round limit of 3 are checked before spending (`RunTreeState.cs:354`). `HearthThisRound` resets only on a new-round offer, so reopening the same round doesn't refill it.
- **Gold.** Spending goes through `TrySpend` once (open run, unique id, 0 < cost ≤ balance); the stack is granted only if the spend applied. Gold can't go negative.
- **Purse.** Pays floor(Δ earned × 0.1), with the mark set after the bonus, so it doesn't compound. Not paid again when a round is reopened.
- **Determinism.** The tree uses no RNG; the graph is built from arrays and lists, and the Dictionary/HashSet are used only for lookups. The pin override is deterministic, and the seeded test replays.
- **Allocations.** `Refresh` writes into preallocated buffers, with no LINQ or closures, and runs on `OfferChanged`, not every frame.
- **Snapshot staleness.** Every mutation raises `OfferChanged`. The 25 nodes fit the 32-node snapshot capacity. Unbacked branches (ArmorBreak, Impact, Cluster) are held out of the draw.

### C. Weapons (priority 3)

- **Burst.** Counts ammo, stops at 0 with 1 round left, and a reload clears it.
- **Shell-by-shell reload.** Firing interrupts it and keeps the loaded shells. The reserve never goes negative, and a reload is refused on a full magazine or with 0 reserve.
- **Reserve refill.** Per-round refill never lowers the reserve, and `Equip` zeroes it.
- **Fire clock.** The accumulator keeps overshoot at high RPM and low FPS, with a 3-shots-per-frame cap and a clamp, so a stall can't bank shots.
- **Movement spread.** `Speed01` can't divide by zero.
- **Recoil.** Capped, with frame-rate-independent recovery.
- **Focus/ADS.** Resets on `Equip`.
- **Numbers.** Everything except W5 matches DEC-097, the builder, the assets and the tests.
- **Allocations.** None per shot in the fire, spawn, step or event path.

### D. AI / animation lifetime (priority 4)

- **Chain resolver.** `ChainTargetResolver` dedupes, excludes the origin, skips non-chainable targets and corpses, has deterministic tie-breaks, and bounds its buffers (min(max, output, 24)). No recursion.
- **`ApplyShock`.** Refuses dead, dying and despawned bodies; ignores a repeated transaction; the hold uses a max and is capped at 2.5 s.
- **Pooled-reuse resets.** Knockback, suppression, shove charge, shock, corpse flag, body-state override, impulse, gait, lean and foot lock are all reset.
- **Accumulator decay.** `Pow(0.5, dt/halfLife)` is frame-rate independent.
- **Static resets.** Corpse list and counters, `DamageHitContext` (try/finally), out-of-world, the round log (scene-local since 1f5e3b2a), and gait/lean statics.
- **`DeathSequence`.** The camera lock is released on every exit path.
- **Guards.** `DeathImpulse.Build` guards against zero or NaN force, zero direction and negative speed. `CadenceBand` guards division by zero.
- **Double-despawn.** Guarded by `IsSpawned`. A destroyed corpse entry (fake-null) is removed.

### E. Audio (priority 5)

- **Hybrid weapons.** The loop replaces the legacy one-shot (`PrototypeAudioDirector.cs:495`), with a clean legacy fallback when clips aren't produced.
- **Legacy→v3 impact hand-off.** `ProjectileSystem.cs:311-312` raises the legacy report and then the v3 impact synchronously, so the hold always comes before the v3 impact.
  - The drain runs in `LateUpdate`, after the projectile driver (order 60).
  - Matching is by position within 0.15 m. Flesh, armour, rocket, orb and gel impacts pass through to the legacy cue.
  - Released cues skip a second hold, and cues arriving while paused are dropped.
- **Loop stops.** On death, round complete, phase None/Dead and director disable (`StopAllLoops` → `StopAll`). On pause via `AudioListener.pause`. On weapon switch or empty magazine via release.
- **Voices.** One dedicated loop source outside the pool, so voice stealing can't take it. No leaks.
- **Subscriptions.** Balanced across `OnEnable`/`OnDisable`, and safe with domain reload off.
- **Allocations and ids.** No per-shot allocations. `AudioSoundId` has no duplicate values (the new 530–550 included), and the table arithmetic is correct.

### Noticed, outside the diff

- `EnemyCombatEvents.Raised` has no `SubsystemRegistration` reset (its subscribers do unsubscribe).
- Shock maps to `Dazed` in `ZombieSpecialReactions.cs:75`, so the `Electrocuted` visual and spasm never fire from gameplay yet.
