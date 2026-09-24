# CLOUD-REVIEW-R1-A — combat / camera / animation correctness review

- Base: `origin/main` @ `f52647f`. It contains WP-069 B1 `0555653`, WP-070 B1–B7, WP-071 B1 `08fd46c` and WP-076 B1 `48a9073`.
- Method: I only read the code. I did not run Unity, tests or LFS assets. Prefab and hitbox geometry comes from `ZombieBodyBuilder` constants and from comments in the code.
- Path prefix: all paths below are relative to `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/`.
- Tags: **CONFIRMED** means I traced the path end to end in code. **PLAUSIBLE** means the code allows it, but it depends on runtime order, data or future callers.
- Scope: I only report correctness findings. Style is out of scope. Findings are ranked most severe first.

---

## 1. [HIGH] TP/FP aim has no parallax guard, so a close aim point makes shots fly backwards
**CONFIRMED**

**Where**
- `Weapons/PlayerWeaponBinding.cs:142-145`
- `Weapons/WeaponController.cs:486-488`
- `Camera/PrototypeCameraRig.cs:1267-1283`

**Defect**
In look modes, `AimDirection` returns `AimWorldPoint − MuzzlePosition` with no check at all. The top-down path has an "ahead of the muzzle" test. The WP-070 §4.4 rule is also missing (if the muzzle→target angle is more than 12° off the camera ray, fire along the camera ray). The centre ray starts at the camera, so its first hit can lie between the camera and the muzzle, or level with the muzzle.

**Failure scenario**
1. In TP or FP, stand flush against a wall or door frame and fire.
2. The centre ray hits the wall about 0.3–0.4 m in front of the root.
3. The muzzle is about 0.6–0.9 m in front of the root (see the comment at `PlayerWeaponBinding.cs:149-154`). That puts it inside or past the wall.
4. `toPoint` therefore points back at the player. `ProjectileSystem.Spawn` launches along it.
5. The Player layer is not in `BulletHitMask`, so the round passes through the player.
6. The ray does not report the wall it started inside (see #2). The round flies up to `maxRange` (22–45 m) behind the player and damages zombies there.

Other ways to trigger it:
- A pole or column that `CameraOcclusionFader` has faded (`FadeCameraLine`). It is translucent but still sits on `AimRayMask`.
- A zombie hitbox at the player's shoulder, between the camera and the muzzle.

**Fix**
Implement §4.4 in `PlayerWeaponBinding.AimDirection` for look modes. Get the camera ray; `CameraAimRay` already carries `Direction`. Fall back to firing along that direction from the muzzle when either condition holds:
- `Vector3.Dot(toPoint, ray.Direction) <= 0`
- `Vector3.Angle(toPoint, ray.Direction) > 12°`

Add an EditMode test with an aim point behind the muzzle.

---

## 2. [HIGH] The first projectile segment starts at the muzzle, so shots miss colliders the muzzle is inside
**CONFIRMED** for the physics semantics.

**Where**
- `Weapons/HitscanEngine.cs:268-270`: `Physics.RaycastNonAlloc(from, …)`, where `from` is `shot.Origin` = the muzzle.
- `Weapons/Projectiles/ProjectileSystem.cs:206`
- `Weapons/WeaponController.cs:485-488`

**Defect**
A Unity raycast never reports a collider whose volume contains the ray origin. All three shipped weapons use `sweepRadius: 0` (WD_ar_1 / WD_smg_1 / WD_sg_1), so every bullet and pellet is a ray. Nothing tests the path from the root or eye to the muzzle before the projectile spawns.

**Failure scenarios**
- **Walls.** When the player hugs a wall, the muzzle is inside the wall collider. The wall is never hit, and rounds come out on the other side in every camera mode. Top-down had the same flaw with hitscan; TP and FP make it routine.
- **Point-blank bodies.** A zombie whose upper hitbox (r 0.26 m, `ZombieBodyBuilder.cs:81`) surrounds the muzzle, or stands between the root and the muzzle, cannot be hit.
- **WP-076 grab escape.** The `bite_grab` holder stands at 0.8 m or less (up to about 0.96 m in the soft band). That is where the muzzle sits. The "one hit ≥ 0.85 on the holder escapes" rule (`Enemies/ZombieEnemy.cs:1301`) is often impossible to meet by shooting the holder.

**Fix**
Before `Spawn`, sweep `BulletHitMask` from a point inside the player capsule to the muzzle:
- the eye in FP;
- `root + up·_shotRayHeight` in TP and top-down.

Use a SphereCast with a small radius, or a Linecast plus `CheckSphere` at the muzzle. If the sweep hits:
- world geometry: resolve a world impact at that point;
- a body: resolve a body hit there.

Alternatively, make the projectile's first step start from that interior point.

---

## 3. [HIGH] RagdollProxyPool dies with the scene, but pooled zombies are DontDestroyOnLoad: stale proxies throw on the next run
**CONFIRMED** static trace. It relies on Unity's Single-mode load order: old-scene objects are destroyed before `activeSceneChanged`.

**Where**
- `Characters/RagdollProxyPool.cs:242-248`: `new GameObject("RagdollProxyPool")`. It is not DDOL.
- `Characters/RagdollProxyPool.cs:144-158`: `Release`. It has no `root == null` guard.
- `Characters/RagdollProxyPool.cs:261-269`: `OnDestroy`. It nulls the slots but leaves each `Proxy.active == true`.
- `Characters/ZombieSkinnedVisual.cs:378`, `:496-503` (`EndRagdoll`) and `:666` (`ResetForSpawn`).
- `Core/Pooling/PoolService.cs:31` and `:134-139`: the DDOL root, with `ReleaseAllSpawned` run on `activeSceneChanged`.

**Defect**
Zombies live under the DDOL pool root and keep their `_ragdoll` handle. The proxy pool belongs to the Run scene.

**Failure scenario — run 1 ends**
1. The run ends (death → Results, or Pause → Quit) while any ragdoll is still active. This is likely:
   - ragdolls stay active for up to 3 s of *scaled* time;
   - the death beat slows time, and pause freezes it.
2. The Run scene unloads. The proxy GameObjects are destroyed and `OnDestroy` sets `s_active = 0`.
3. `activeSceneChanged` then triggers `ReleaseAllSpawned`, which deactivates the zombie.
4. `ZombieSkinnedVisual.OnDisable` calls `EndRagdoll`, which calls `Release(p)`.
5. `p.active` is still true, so `p.bodies[0].interpolation = …` throws `MissingReferenceException`. Unity logs it.
6. `_ragdoll` is never cleared.

**Failure scenario — run 2 spawns that instance**
1. `EnemyFactory.Spawn` calls `ZombieEnemy.Initialise`, which calls `_animator.ResetForSpawn`, then `ZombieSkinnedVisual.ResetForSpawn`, then `EndRagdoll`.
2. `Release` throws again. This time the exception escapes into `EnemyFactory.Spawn` and the spawn director.
3. The body is left checked out of the pool and half-initialised: `_alive = false`, no armour roll, no nav profile.
4. It happens again every time that instance is drawn, for up to `Capacity` (6) instances per session.

**Fix**
- Create the pool under `DontDestroyOnLoad`, parented to the same DDOL root as the zombies.
- Or, in `OnDestroy`, set `active = false` on every built proxy.
- In both cases, make `Release` bail out when `p.root == null`.

Add a PlayMode test: kill a zombie → scene reload → respawn.

---

## 4. [MED-HIGH] Special weapons now get 3D aim in TP/FP, so the Tide Jewel lands in mid-air
**CONFIRMED**

**Where**
- `Weapons/WeaponController.cs:699`: `_flattenAim` now applies only in top-down.
- `Weapons/WeaponController.cs:623-626`: `FireSpecialOnce` uses the same `ResolveAimDirection`.
- `Weapons/SpecialWeaponRuntime.cs:521-545`: `ResolveLanding`.

**Defect**
`ResolveLanding` assumes a flat throw. It places the landing at `origin + dir·ThrowRange` (22 m by default), then probes for ground from 6 m above that point, 12 m down.

**Failure scenario**
With pitch θ above horizontal and nothing in the way, the landing point is 1.4 + 22·sinθ m high. The probe reaches ground only if 22·sinθ < 4.6, so for θ above about 12° it finds nothing. The well spawns in the air and pulls the crowd towards a point nobody can reach. The Ofuda Loom is also aimed in 3D now (hitscan). That is probably intended, but needs a design check.

**Fix**
Either:
- keep Special aim planar (flatten when `_special.IsActive`); or
- raycast along the 3D aim ray and, with no hit, drop straight down from the ray end with an unbounded (world-height) probe.

Add a test at +30° pitch.

---

## 5. [MEDIUM] ProjectileEvents have no common key, so projectile-keyed consumers cannot close their state
**CONFIRMED**

**Where**
- `Weapons/Projectiles/ProjectileEvents.cs:10-33`: `ProjectileSpawnInfo` has `Index`.
- `Weapons/Projectiles/ProjectileEvents.cs:36-71`: `ProjectileImpactInfo` has `TransactionId` but no `Index`.
- `Weapons/Projectiles/ProjectileEvents.cs:85`: `Expired(int index, …)`.
- `Weapons/Projectiles/ProjectileSystem.cs:292-297`: `Clear()` raises nothing.

**Defect**
A consumer that starts per-projectile state on `Spawned` cannot match a stopping `Impact` to it. The docs name two such consumers: the S-08 flight loop and the S-07 trail. Slot indices are also reused. `Clear()` on scene unload removes live projectiles without raising any event.

**Failure scenario**
Every projectile that ends on impact leaks its flight loop or trail. Neither DDOL consumer gets a terminal event across a scene change.

**Fix**
- Add `Index` and `Generation` to `ProjectileImpactInfo`, and `Generation` to `Spawned` and `Expired`. Alternatively, add `TransactionId` to all three.
- Have `Clear()` raise `Expired` for each live projectile, or raise a `Cleared` event.

---

## 6. [MEDIUM] The touched-set cap of 8 lets one projectile damage a body twice (ADR-034-1)
**CONFIRMED** in code. It needs the Pierce/penetration and Explosion/Chain upgrades.

**Where**
- `Weapons/Projectiles/ProjectileSystem.cs:103-113`: `AddTouched` silently drops entries past 8.
- `Weapons/HitscanEngine.cs:365`: branch targets are copied into the capped set.
- `Enemies/ZombieEnemy.cs:1202-1218`: the receiver only remembers `_lastTransactionId`.

**Defect**
The branch explosion runs on the first body hit and can touch more than 8 bodies in a 40-body crowd. Only 8 are remembered. On later steps, a penetrating projectile hits bodies that its own explosion already damaged. The receiver guard lets the hit through as soon as any other transaction has touched that body in between, which is routine under automatic fire. Hitscan never had this problem: its `s_touched` list was unbounded and the whole shot resolved in one frame.

**Fix**
Pick one:
- Raise `TouchedCapacity` to cover `OverlapBufferSize`, using a pooled side buffer per projectile.
- Stop penetrating after the branch step.
- Keep a per-transaction "seen" bitset keyed by `DamageTarget.Id`, owned by the receiver registry.

---

## 7. [MEDIUM] `IsTransitioning` is global and only an enabled rig advances it
**CONFIRMED** code path. The trigger is **PLAUSIBLE** until the S-09 settings UI is wired.

**Where**
- `Camera/CameraModeService.cs:141` (`IsTransitioning`), `:204-230` (`Request`) and `:261-273` (`AdvanceTransition`, whose only caller is `PrototypeCameraRig.cs:505`).
- `Camera/PrototypeCameraRig.cs:346`: `Request(start, instant:true)` does nothing when the mode is already current.
- `Weapons/WeaponController.cs:686`: `CanFireNow`.

**Defect**
Blend progress is advanced only inside `PrototypeCameraRig.LateUpdate`, and only while the rig is enabled and `HasConfig`. It is reset only on `SubsystemRegistration`. Two cases leave `s_transition01 < 1`:
- a `Request` or `ApplySettings(applyMode:true)` made while no rig exists (a main-menu settings screen, tools);
- a scene change inside the 0.3–0.55 s blend.

**Failure scenario**
The next run's `Request(start, instant:true)` does nothing because the mode is already current. Fire, reload and the Special are swallowed while the camera swoops in from the scene-default rig pose. With no ticking rig at all (a rig-less test scene or tool scene), firing is blocked forever.

**Fix**
- In `Request(…, instant:true)`, force `s_transition01 = 1` and `s_previous = s_current` even when the mode is unchanged.
- In the rig's `OnDisable`, complete any pending transition.

---

## 8. [MED-LOW] Shotgun pellets now land over several frames, so one trigger pull gives several shakes / hit-stops
**PLAUSIBLE**

**Where**
- `Weapons/WeaponController.cs:510-518`: one `PresentationGroupId` per trigger pull.
- `Feedback/FeelEventRouter.cs:53-108`: arbitration per group, per `Flush`, and a flush happens every frame.

**Defect**
With hitscan, all 8 pellets resolved in one frame and the router picked one winner per group. As projectiles, at 120 m/s (about 2 m per frame), pellets reach bodies at different depths in different frames.

**Failure scenario**
The same group wins once per frame it lands. `CameraShakeBridge.Shake` is called 2–3 times per blast into a crowd. Hit-stop is limited only by `minIntervalSeconds`.

**Fix**
Keep a "group already resolved" stamp in `HitStopService` / the router (group id → frame / time window), or resolve feel on the first impact of a group only.

---

## 9. [LOW-MED] WP-076: the soft band stretches the commit range to 1.2× reach for every style
**CONFIRMED** in code. Needs a design decision.

**Where**
- `Enemies/AttackStyles.cs:95-133` (`Select`, the normalised draw) and `:136-144` (`RangeFit` soft band).
- `Enemies/ZombieEnemy.cs:958-971`: replaces `CanStartAttack(distance ≤ _range)`.

**Defect**
`Select` returns true whenever *any* style scores above 0. Fit only re-weights between styles; it never scales the chance of swinging at all.

**Failure scenario**
A Standard at 1.2 × 1.7 m has a Swipe fit of about 0.03, yet still swings 100 % of the time. Before this change it swung only at ≤ 1.7 m, and the "signature attack unchanged" claim assumed that. Expect more whiffs and earlier telegraphs.

**Fix**
Roll `total / Σ weight` (or `max fit`) against a commit probability. Alternatively, hard-gate commit at `MaxReach` and use the band only for style mix.

---

## 10. [LOW] WP-076: the grab escape reads the throttled impulse
**CONFIRMED**

**Where**
- `Enemies/ZombieEnemy.cs:1301`
- `Weapons/HitscanEngine.cs:762`
- `Weapons/StaggerGovernor.cs:117-145`

**Defect**
`hit.ImpactForce` is the governed value. Once a body's window reaches `BurstImpulseCeiling`, impulses are damped to `DampedFraction`, so a 0.85 shotgun pellet no longer reaches 0.85.

**Failure scenario**
Automatic fire into the holder saturates the window, and then no shotgun hit can free the player during that window. The code comment says the escape is "independent" of the stagger gate. It is gated by it.

**Fix**
Carry the raw impulse on `DamageHit`, or compare before `Govern`.

---

## 11. [LOW] WP-076: `GrabState.ResetStatics` drops event subscribers; the slot board is never reset per run
**PLAUSIBLE**

**Where**
- `Enemies/AttackStyles.cs:280`
- `Enemies/EnemyAttackState.cs:167-171`

**Defect**
- `EnemyAttackState.ResetStatics()` is called from several test setups. It nulls `GrabState.Started` / `Ended`, so any static subscriber (S-02 slow, S-09 prompt) registered earlier in the session is silently lost.
- In production, `AttackSlotBoard` / `GrabState` are never reset at run start. Correctness depends on every body passing through `OnDespawned`. That holds today via `PoolService.ReleaseAllSpawned`, but it is fragile.

**Fix**
- Split "reset counters" from "clear events".
- Reset the board and grab state on `RunEvents.RunStarted`.

---

## 12. [LOW] Ragdoll: tier cap not wired; hand-off velocity is wrong for far-LOD bodies

**Where**
- `Characters/RagdollProxyPool.cs:20`
- `Characters/ZombieSkinnedVisual.cs:466-474`
- `Characters/CharacterAnimatorLod.cs:75-103`

**Defects**
- **Tier cap — CONFIRMED.** `Capacity` is a fixed 6. `QualityTiers` never sets it, so the tier caps 10 / 6 / 2 are not in effect.
- **Hand-off velocity — PLAUSIBLE.** For far-LOD bodies the Animator is stepped by hand at 15 Hz. Bones move only on those steps, so the per-frame `(now − prev)/dt` velocity is either 0 or a spike of up to about 4×. Ragdolls are thrown inconsistently.

**Fix**
- Set `Capacity` from `QualityTiers.Pick(2, 6, 10)` on `Changed`.
- For far bodies, compute velocity over the last LOD step, or skip bone velocity.

---

## 13. [LOW] ProjectileSystem is not safe against re-entry from its own callbacks
**PLAUSIBLE**. No current listener triggers it.

**Where**
- `Weapons/Projectiles/ProjectileSystem.cs:257-277`: `Step` walks `s_order` in place.
- `Weapons/Projectiles/ProjectileSystem.cs:180-189`: eviction shifts `s_order`.
- `Weapons/Projectiles/ProjectileSystem.cs:304`: `ref ProjectileState p` is held across `ResolveProjectileSegment`.

**Defect**
`StepOne` raises `Impact` and enemy damage/death events.

**Failure scenario**
A listener that calls `ProjectileSystem.Spawn` while the pool is full causes two problems:
- It evicts `s_order[0]` and shifts the list, so one projectile skips this step.
- If the evicted slot is the one being stepped, `p` now aliases the new projectile, which is moved or killed by the rest of `StepOne`.

A listener calling `Clear()` has the same exposure.

**Fix**
Queue spawns (and clears) raised during `Step`, and apply them after the loop.

---

## 14. [LOW] ProjectileDriver is created only once per domain
**PLAUSIBLE**

**Where**
- `Weapons/Projectiles/ProjectileSystem.cs:363-373`: `EnsureHooks` is gated by `s_sceneHooked`.

**Defect**
If the DDOL `ProjectileDriver` is ever destroyed (test teardown, a future "destroy DDOL on quit to menu"), `EnsureExists` is never called again.

**Failure scenario**
Projectiles spawn but never tick or hit.

**Fix**
Call `ProjectileDriver.EnsureExists()` on every `Spawn`. It is cheap because it returns early when the instance is alive.

---

## 15. [LOW] Gravity is documented as cosmetic but moves hit regions
**CONFIRMED** from the numbers.

**Where**
- `Weapons/Editor/ArsenalAssetBuilder.cs:127`: gravity 9.81 on all three weapons.
- WP-069 direction §A1 item 6: "≤ 0.2 m at 45 m, cosmetic".

**Defect**
The drop is applied to the damage sweep.

**Failure scenario**
- SMG: 170 m/s over 38 m is 0.245 m of drop, above the documented bound.
- Top-down: WP-042's fixed 1.39 m ray height existed for region parity. The hit height now depends on range.

**Fix**
Either set `gravity = 0` for the bullet and pellet kinds (keep drop visual-only in the trail), or amend DEC / WP-042 parity with measured region tables.

---

## 16. [LOW] Look-mode spread still uses top-down geometry
**CONFIRMED** math.

**Where**
- `Weapons/ClassicWeaponRuntime.cs:432-452` (`SpreadUtility.Apply`) and `:463-500` (`ApplyRing`).

**Defects**
- Yaw is rotated about world-up. Horizontal scatter therefore shrinks by cos(pitch) when aiming up or down in TP/FP.
- `VerticalSpreadFactor = 0.35` is a top-down artefact ("vertical scatter nearly invisible from a high camera"). It is still applied, so the FP/TP "hip" cones are 0.35-flattened ellipses.

**Fix**
In look modes, build the cone in the aim direction's own frame (`right = Cross(camUp, fwd)`) with a circular or per-mode factor.

---

## 17. [LOW] Camera blends: visibility and occluder policy switch at t = 0
**PLAUSIBLE** (visual).

**Where**
- `Camera/PrototypeCameraRig.cs:801-814`
- `Camera/CameraOcclusionFader.cs:125`

**Defects**
- **FP → TP.** `ShowPlayer()` runs on the first blend frame. The body renderers and the weapon layer come back while the camera is still at the eye (near clip 0.05), so the head interior is visible and the weapon is drawn by the world camera, clipping walls. The direction doc wants the switch in the head-side 20 % of the blend.
- **TopDown → TP/FP.** `CurrentProfile` is already the target mode, so roofs that were fading snap back to opaque while the camera is still overhead.

**Fix**
Hide or show on `Transition01` thresholds in both directions, and use `Previous`'s occluder policy until about 0.5.

---

## 18. [LOW] WP-070 §5.2 death lock not wired; cursor intent depends on the rig
**CONFIRMED** by grep.

**Where**
- `CameraModeService.LockModeChanges` / `UnlockModeChanges`: no production callers.
- `CameraModeService.SetFrozen`: only called from the rig (`PrototypeCameraRig.cs:476`).

**Defect**
- A mode request during the death beat is not queued as the doc requires.
- `CursorIntent` goes stale whenever the rig is disabled.

**Fix**
- `DeathSequence` should call `LockModeChanges("death")` / `UnlockModeChanges("death")`.
- `CursorIntent` should read `RunFlowClock.IsFrozen` directly.

---

## 19. [LOW] `WeaponConfigSection.ClassicWeapons` is now a non-serialized view
**CONFIRMED**

**Where**
- `Core/Config/WeaponConfigSection.cs:424-465`

**Defects**
- `Object.Instantiate(config)` rebuilds the list from the definitions. Edits made on the source config before cloning (F1 panel, tests that tweak and then clone `Active`) are lost.
- The `ClassicWeapons:` block serialized in `Core/Resources/PrototypeConfig.asset:39-105` is now dead data. A future edit there will silently do nothing.

**Fix**
- Copy `_classicView` in a custom clone helper.
- Strip the dead YAML block, or add a validator warning.

---

## Checked and found sound

**Tunnelling**
- Consecutive step segments are contiguous, and each is swept for its full length.
- The long-frame remainder is swept in one segment.
- The 5 cm wall case holds for static colliders.
- Note: all sub-steps in a frame query the same physics state (no `SyncTransforms` / `Simulate` between them). Animated hitboxes are as stale as they were with hitscan.

**Pause / timeScale 0**
- `Tick(deltaTime <= 0)` keeps the accumulator.
- Hit-stop and the death slow-motion scale the flight.
- Rounds frozen at round end resume after the shop. Acceptable.

**Pool exhaustion (256)**
- The oldest projectile is evicted, and `Expired` is raised before the slot is reused.
- `OverflowCount` is counted.
- Worst player case is about 8 live projectiles.

**Per-frame allocations (projectile path)**
- None found in `Spawn`, `Tick`, `ResolveProjectileSegment`, event raising (struct payloads, `in`) or the interim streak. `Graphics.DrawMesh` is native.

**Damage transaction lifetime**
- `NextId` at spawn, `Resume` and `End` around each step, and there is no nesting on the driver path.
- Each pellet has its own id, as with hitscan.

**Weapon switch / player death mid-flight**
- Each projectile holds its own `ShotRequest` copy.
- Kills after the run ends are ignored by `RunDirector.IsRunLive` and the gold ledger.

**Camera math and cursor**
- **Hor+ conversion** (`PrototypeCameraRig.cs:737`): 90° horizontal @ 16:9 → 58.7° vertical is correct. The TP FOV is correctly vertical, and the viewmodel FOV follows the world FOV (phase 1).
- **Cursor lock**: pause, round-end and debug holds all free it; focus loss unlocks it; rig disable releases it; the Results / Menu scene loads disable the rig. No leak found.
- **Blend**: unscaled time, smoothstep; re-targeting mid-blend snapshots the current pose.

**WP-076 slot board**
- The Acquire / Commit / Release paths balance across all exits:
  - Recovery end;
  - `Cancel` (stagger, root, death);
  - `OnDespawned`;
  - `ResetForSpawn`;
  - escape.
- The grab ends on the timer, escape, death, despawn and stagger.
- Pause freezes the grab timer.

**WP-071**
- The layer matrix is set at runtime: Ragdoll ignores Player, Enemy, EnemyHitbox, AttackHitbox, Projectile and Pickup. TagManager has layer 17.
- When the pool is full, the authored fall keeps playing.
- The Animator is re-enabled on despawn by `CharacterAnimatorLod.OnDisable` even when the far LOD had disabled it.
- The 48 new event names map 1:1, in order, to enum values 29–76.
