using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-042 · B (DEC-082: no pickup lock — movement and aim keep running through the grab,
    /// a hit does not cancel the Special, death ends it) and C (mechanism slots: casing per
    /// shot on A/B, at the pump on C; magazine at the reload start; the shotgun ring pattern
    /// fires through the real controller; the equipped weapon's rumble profile is live).
    /// Uses the WP-007 kit (KUR-B): live Run scene, scripted input, config copy.
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponFeelSliceTests
    {
        private const float Pencere = 0.5f;

        private SpecialWeaponSahne _s;
        private readonly List<WeaponMechanismEvent> _mechanism = new List<WeaponMechanismEvent>(64);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            WeaponEvents.Mechanism -= OnMechanism;
            _mechanism.Clear();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _mechanism.Clear();
            WeaponEvents.Mechanism += OnMechanism;
        }

        private void OnMechanism(WeaponMechanismEvent e) => _mechanism.Add(e);

        private int Count(WeaponMechanismKind kind)
        {
            int n = 0;
            for (int i = 0; i < _mechanism.Count; i++) if (_mechanism[i].Kind == kind) n++;
            return n;
        }

        private IEnumerator SpawnVeAl()
        {
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup, "Pickup yok.");
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "alma");
        }

        [UnityTest]
        [Category("WP062")]
        public IEnumerator StagedNFields_CommitAtRealRunSceneBoundary_BeforeSetup()
        {
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
            FeelConfigData staged = FeelConfig.CreateDefaults();
            staged.camera.heightMeters = 18f;
            BudgetCategoryFeelRow impact = System.Array.Find(staged.budgets.categories, row => row.id == "Impact");
            impact.capacity = 31;
            Assert.IsTrue(FeelRuntime.QueueAuthored(staged, "run-boundary", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(62062));
            Assert.IsTrue(FeelRuntime.HasPendingNextRun);
            Assert.AreNotEqual(18f, FeelRuntime.Current.Camera.heightMeters);

            yield return Kur();

            Assert.AreEqual(18f, FeelRuntime.Current.Camera.heightMeters, 1e-6f);
            Assert.AreEqual(31, FeelRuntime.Current.Budgets.CapacityFor("Impact", -1));
            Assert.IsFalse(FeelRuntime.HasPendingNextRun);
        }

        // ------------------------------------------------------------------ B · DEC-082 no pickup lock

        [UnityTest]
        public IEnumerator DEC082_AlmaSirasinda_HareketVeNisanKilitlenmez()
        {
            // BEGIN S02_R2 DEC observer locals and startup provenance
            string DecRandomState()
            {
                try
                {
                    string raw = JsonUtility.ToJson(UnityEngine.Random.state);
                    return string.IsNullOrEmpty(raw) || raw == "{}" ? "{\"status\":\"UNKNOWN:state_serializer_empty\"}" :
                        "{\"status\":\"RAW_OBSERVED_NOT_SEED_OR_REPLAY_PROOF\",\"raw\":" + raw + "}";
                }
                catch (System.Exception) { return "{\"status\":\"UNKNOWN:state_getter_or_serializer_failure\"}"; }
            }
            string decStartupUnityRandom = "{\"status\":\"NOT_REQUESTED\"}", decBeforeSpawnUnityRandom = "{\"status\":\"NOT_REQUESTED\"}";
            string decPickupBeforeMove = "null";
            bool decDiag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ddDec082Diag") >= 0;
            int decSamples = 0, decLowSamples = 0, decControlClosedSamples = 0, decFirstFrame = -1, decLastFrame = -1;
            float decLowestSpeed = float.PositiveInfinity, decObservedMaxSpeed = 0f;
            Vector3 decStartPosition = Vector3.zero, decLastPosition = Vector3.zero;
            string decStart = null, decFirst = null, decFirstLow = null, decEnd = null, decError = null;
            string decAssemblyName = "UNKNOWN", decAssemblyLocation = "UNKNOWN", decAssemblyHash = "UNKNOWN";
            string decRuntimeAssemblyName = "UNKNOWN", decRuntimeAssemblyLocation = "UNKNOWN", decRuntimeAssemblyHash = "UNKNOWN";
            string DecQuote(string value)
            {
                var text = new System.Text.StringBuilder("\"");
                foreach (char ch in value ?? "UNKNOWN")
                {
                    if (ch == '"' || ch == '\\') text.Append('\\').Append(ch);
                    else if (ch < 32) text.Append("\\u").Append(((int)ch).ToString("x4"));
                    else text.Append(ch);
                }
                return text.Append('"').ToString();
            }
            string DecFloat(float value) => float.IsNaN(value) || float.IsInfinity(value) ? DecQuote("UNKNOWN:nonfinite") : value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string DecBool(bool value) => value ? "true" : "false";
            string DecArg(string key)
            {
                string[] args = System.Environment.GetCommandLineArgs();
                int index = System.Array.IndexOf(args, key);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : "UNKNOWN";
            }
            string DecPath(Transform tr)
            {
                if (tr == null) return "null";
                var nodes = new List<string>(32);
                Transform current = tr;
                while (current != null && nodes.Count < 32)
                {
                    nodes.Add("{\"name\":" + DecQuote(current.name) + ",\"siblingIndex\":" + current.GetSiblingIndex() +
                        ",\"transformId\":" + current.GetInstanceID() + ",\"gameObjectId\":" + current.gameObject.GetInstanceID() + "}");
                    current = current.parent;
                }
                return "{\"order\":\"leaf_to_root\",\"nodes\":[" + string.Join(",", nodes) + "],\"truncated\":" + DecBool(current != null) + "}";
            }
            string DecTrs(Transform tr) => "{\"localPosition\":" + JsonUtility.ToJson(tr.localPosition) + ",\"localRotation\":" + JsonUtility.ToJson(tr.localRotation) +
                ",\"localScale\":" + JsonUtility.ToJson(tr.localScale) + ",\"worldPosition\":" + JsonUtility.ToJson(tr.position) + ",\"worldRotation\":" + JsonUtility.ToJson(tr.rotation) +
                ",\"lossyScale\":" + JsonUtility.ToJson(tr.lossyScale) + ",\"worldMatrix\":" + JsonUtility.ToJson(tr.localToWorldMatrix) + "}";
            string DecArgs()
            {
                var quoted = new List<string>();
                foreach (string arg in System.Environment.GetCommandLineArgs()) quoted.Add(DecQuote(arg));
                return "[" + string.Join(",", quoted) + "]";
            }
            string DecProvenance() => "{\"record\":\"DD-Provenance\",\"testId\":" + DecQuote(TestContext.CurrentContext.Test.ID) +
                ",\"testFullName\":" + DecQuote(TestContext.CurrentContext.Test.FullName) + ",\"perCaseSeed\":\"UNKNOWN:bound_from_XML_after_run\",\"parentGlobalSeed\":\"UNKNOWN\",\"historicalReplay\":\"NOT_PROVEN\"" +
                ",\"cliArgsOrdered\":" + DecArgs() +
                ",\"sourceCommit\":" + DecQuote(DecArg("-ddGateDiagSourceCommit")) + ",\"sourceHash\":" + DecQuote(DecArg("-ddDecDiagSourceHash")) +
                ",\"inputHash\":" + DecQuote(DecArg("-ddGateDiagInputHash")) + ",\"fixtureHash\":" + DecQuote(DecArg("-ddDecDiagFixtureHash")) + ",\"executionId\":" + DecQuote(DecArg("-ddGateDiagExecutionId")) +
                ",\"assemblyName\":" + DecQuote(decAssemblyName) + ",\"assemblyLocation\":" + DecQuote(decAssemblyLocation) + ",\"assemblySha256\":" + DecQuote(decAssemblyHash) +
                ",\"runtimeAssemblyName\":" + DecQuote(decRuntimeAssemblyName) + ",\"runtimeAssemblyLocation\":" + DecQuote(decRuntimeAssemblyLocation) + ",\"runtimeAssemblySha256\":" + DecQuote(decRuntimeAssemblyHash) + "}";
            string DecSnapshot(string stage)
            {
                PlayerMotor motor = _s.Rig.Motor;
                CharacterController cc = motor.GetComponent<CharacterController>();
                string controller = "{\"status\":\"OBSERVED_NULL\"}";
                if (cc != null)
                {
                    var scene = cc.gameObject.scene;
                    controller = "{\"type\":" + DecQuote(cc.GetType().FullName) + ",\"instanceId\":" + cc.GetInstanceID() + ",\"path\":" + DecPath(cc.transform) +
                        ",\"shape\":{\"kind\":\"CharacterController_capsule\",\"center\":" + JsonUtility.ToJson(cc.center) + ",\"radius\":" + DecFloat(cc.radius) + ",\"height\":" + DecFloat(cc.height) +
                        ",\"skinWidth\":" + DecFloat(cc.skinWidth) + ",\"stepOffset\":" + DecFloat(cc.stepOffset) + ",\"slopeLimit\":" + DecFloat(cc.slopeLimit) + ",\"minMoveDistance\":" + DecFloat(cc.minMoveDistance) + "}" +
                        ",\"bounds\":" + JsonUtility.ToJson(cc.bounds) + ",\"boundsCenter\":" + JsonUtility.ToJson(cc.bounds.center) + ",\"boundsExtents\":" + JsonUtility.ToJson(cc.bounds.extents) + ",\"trs\":" + DecTrs(cc.transform) +
                        ",\"sceneHandle\":" + scene.handle + ",\"sceneName\":" + DecQuote(scene.name) + ",\"scenePath\":" + DecQuote(scene.path) +
                        ",\"enabled\":" + DecBool(cc.enabled) + ",\"activeSelf\":" + DecBool(cc.gameObject.activeSelf) + ",\"activeInHierarchy\":" + DecBool(cc.gameObject.activeInHierarchy) +
                        ",\"isGrounded\":" + DecBool(cc.isGrounded) + ",\"collisionFlags\":" + DecQuote(cc.collisionFlags.ToString()) + ",\"velocity\":" + JsonUtility.ToJson(cc.velocity) + "}";
                }
                return "{\"unityRandomStateAtSnapshot\":" + DecRandomState() + ",\"stage\":" + DecQuote(stage) + ",\"collectionTiming\":\"public_getters_sequential_not_atomic;not_pre_post_Move\",\"frame\":" + Time.frameCount +
                    ",\"time\":" + DecFloat(Time.time) + ",\"realtime\":" + DecFloat(Time.realtimeSinceStartup) + ",\"unscaledTime\":" + DecFloat(Time.unscaledTime) + ",\"timeScale\":" + DecFloat(Time.timeScale) +
                    ",\"deltaTime\":" + DecFloat(Time.deltaTime) + ",\"fixedTime\":" + DecFloat(Time.fixedTime) + ",\"inFixedTimeStep\":" + DecBool(Time.inFixedTimeStep) +
                    ",\"position\":" + JsonUtility.ToJson(_s.Rig.transform.position) + ",\"motorId\":" + motor.GetInstanceID() + ",\"planarVelocity\":" + JsonUtility.ToJson(motor.PlanarVelocity) +
                    ",\"rawInput\":" + JsonUtility.ToJson(motor.RawInput) + ",\"scriptedInputYon\":" + JsonUtility.ToJson(_s.Girdi.Yon) +
                    ",\"motorControlEnabled\":" + DecBool(motor.ControlEnabled) + ",\"aimControlEnabled\":" + DecBool(_s.Rig.Aim.ControlEnabled) +
                    ",\"maxSpeed\":" + DecFloat(motor.MaxSpeed) + ",\"acceleration\":" + DecFloat(motor.Acceleration) + ",\"weaponSpeedMultiplier\":" + DecFloat(motor.WeaponSpeedMultiplier) +
                    ",\"specialActive\":" + DecBool(_s.Controller.SpecialWeaponActive) + ",\"frozen\":" + DecBool(RunFlowClock.IsFrozen) +
                    ",\"blockerIdentity\":\"UNKNOWN:no_public_contact_identity_getter\",\"desiredMotorVelocity\":\"UNKNOWN:no_public_actual_desired_velocity_getter\",\"nativePose\":\"UNKNOWN\",\"characterController\":" + controller + "}";
            }
            string DecSafeSnapshot(string stage)
            {
                try { return DecSnapshot(stage); }
                catch (System.Exception) { decError = "UNKNOWN:observer_getter_failure"; return "{\"status\":\"UNKNOWN:observer_getter_failure\"}"; }
            }
            if (decDiag)
            {
                try
                {
                    decStartupUnityRandom = DecRandomState();
                    var assembly = typeof(WeaponFeelSliceTests).Assembly;
                    decAssemblyName = assembly.FullName; decAssemblyLocation = assembly.Location;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        decAssemblyHash = System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(decAssemblyLocation))).Replace("-", "").ToLowerInvariant();
                    var runtimeAssembly = typeof(PlayerMotor).Assembly;
                    decRuntimeAssemblyName = runtimeAssembly.FullName; decRuntimeAssemblyLocation = runtimeAssembly.Location;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        decRuntimeAssemblyHash = System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(decRuntimeAssemblyLocation))).Replace("-", "").ToLowerInvariant();
                }
                catch (System.Exception) { decError = "UNKNOWN:startup_assembly_hash_failure"; }
            }
            // END S02_R2 DEC observer locals and startup provenance
            yield return Kur();

            // BEGIN S02_R2 DEC before spawn RNG observer
            if (decDiag) decBeforeSpawnUnityRandom = DecRandomState();
            // END S02_R2 DEC before spawn RNG observer
            // C-00 30.09 M-19: the pickup's spot is the director's roll (angle 0–360° around the player) — with the suite's
            // shared random state it moved with the test order and could land against a V6 prop. A fixed source puts it
            // in the same place every run.
            _s.Director.SetRandomSource(new SabitRastgele(0.25f));
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup);
            _s.Girdi.Yon = new Vector2(1f, 0f);
            _s.Girdi.Imlec = new Vector2(Screen.width * 0.9f, Screen.height * 0.5f);
            // BEGIN S02_R2 DEC pickup scalar observer before original teleport
            if (decDiag)
            {
                try
                {
                    decPickupBeforeMove = pickup != null ? "{\"instanceId\":" + pickup.GetInstanceID() + ",\"weaponId\":" + DecQuote(pickup.WeaponId.ToString()) +
                        ",\"weaponNumericId\":" + (int)pickup.WeaponId + ",\"position\":" + JsonUtility.ToJson(pickup.transform.position) + "}" : "{\"status\":\"OBSERVED_NULL\"}";
                }
                catch (System.Exception) { decError = "UNKNOWN:pickup_scalar_getter_failure"; }
            }
            // END S02_R2 DEC pickup scalar observer before original teleport
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "alma");

            // The AP-009C grab clip is 0.8 s long; through that window the motor keeps the stick and moves.
            float t0 = Time.time;
            Vector3 p0 = _s.Rig.transform.position;
            float maxSpeed = 0f;
            float minKomut = float.MaxValue;
            int kontrolKapali = 0, yanEngel = 0;
            CharacterController govde = _s.Rig.Motor.GetComponent<CharacterController>();
            Vector3 aim0 = _s.Rig.Aim.AimDirection;
            // BEGIN S02_R2 DEC start and try wrapper
            if (decDiag)
            {
                decStartPosition = _s.Rig.transform.position;
                decLastPosition = decStartPosition;
                decStart = DecSafeSnapshot("movement-window-start");
            }
            try
            {
            // END S02_R2 DEC start and try wrapper
            while (Time.time - t0 < 0.8f)
            {
                Assert.AreEqual(1f, _s.Rig.Motor.RawInput.x, 1e-3f, "motor girdisi alma sırasında düşmez");
                maxSpeed = Mathf.Max(maxSpeed, _s.Rig.Motor.PlanarVelocity.magnitude);
                minKomut = Mathf.Min(minKomut, _s.Rig.Motor.CommandedPlanarVelocity.magnitude);
                if (!_s.Rig.Motor.ControlEnabled) kontrolKapali++;
                if (govde != null && (govde.collisionFlags & CollisionFlags.Sides) != 0) yanEngel++;
                // BEGIN S02_R2 DEC iteration observer
                if (decDiag)
                {
                    try
                    {
                        float observedSpeed = _s.Rig.Motor.PlanarVelocity.magnitude;
                        decSamples++; decLastFrame = Time.frameCount;
                        if (decFirstFrame < 0) decFirstFrame = decLastFrame;
                        decLowestSpeed = Mathf.Min(decLowestSpeed, observedSpeed);
                        decObservedMaxSpeed = Mathf.Max(decObservedMaxSpeed, observedSpeed);
                        decLastPosition = _s.Rig.transform.position;
                        if (!_s.Rig.Motor.ControlEnabled || !_s.Rig.Aim.ControlEnabled) decControlClosedSamples++;
                        if (decFirst == null) decFirst = DecSafeSnapshot("first-movement-window-sample-after-original-maxSpeed-update");
                        if (observedSpeed <= 0.5f)
                        {
                            decLowSamples++;
                            if (decFirstLow == null) decFirstLow = DecSafeSnapshot("first-speed-at-or-below-original-assert-threshold;not-global-lowest-snapshot");
                        }
                    }
                    catch (System.Exception) { decError = "UNKNOWN:iteration_observer_failure"; }
                }
                // END S02_R2 DEC iteration observer
                yield return null;
            }

            // BEGIN S02_R2 DEC window end observer
            if (decDiag) decEnd = DecSafeSnapshot("movement-window-end-before-original-assertions");
            // END S02_R2 DEC window end observer
            // C-00 30.09 M-19: DEC-082 is "no pickup LOCK" — measured on what the motor is commanded (input × speed) every
            // frame; the body's travel is required only when nothing stopped it (a prop at the pickup: CC side contact).
            float yol = (_s.Rig.transform.position - p0).magnitude;
            string olcum = " (komut en az " + minKomut.ToString("F2") + " m/s, en yüksek hız " + maxSpeed.ToString("F2") + " m/s, yol " + yol.ToString("F2") +
                           " m, yan temas " + yanEngel + " kare, kontrol kapalı " + kontrolKapali + " kare)";
            Debug.Log("[DD-TEST] dec082" + olcum);
            Assert.AreEqual(0, kontrolKapali, "DEC-082: alma sırasında motor kontrolü kapandı" + olcum);
            Assert.Greater(minKomut, 0.5f, "DEC-082: alma sırasında motorun komut hızı düştü (kilit)" + olcum);
            Assert.IsTrue(yol > 0.3f || yanEngel > 0, "oyuncu hareket etmedi ve önünde engel de yoktu" + olcum);
            _s.Girdi.Imlec = new Vector2(Screen.width * 0.1f, Screen.height * 0.5f);
            // The aim turns at its own rate; give it up to a second (it must move at all — a lock would hold it).
            yield return SpecialWeaponSahne.Bekle(() => Vector3.Angle(aim0, _s.Rig.Aim.AimDirection) > 20f, 1f, "nişan alma sırasında imleci takip eder");
            Assert.IsTrue(_s.Controller.SpecialWeaponActive, "Special aktif kaldı");
            // BEGIN S02_R2 DEC finally observer wrapper
            }
            finally
            {
                if (decDiag)
                {
                    try
                    {
                        if (decEnd == null) decEnd = DecSafeSnapshot("failure-finally-before-window-completed;not-normal-window-end");
                        Debug.Log("[DD-DEC082-DIAG] {\"stage\":\"NOTE\",\"provenance\":" + DecProvenance() +
                            ",\"startupUnityRandomState\":" + decStartupUnityRandom + ",\"beforeSpawnUnityRandomState\":" + decBeforeSpawnUnityRandom + ",\"finallyUnityRandomState\":" + DecRandomState() +
                            ",\"pickupBeforeOriginalMove\":" + decPickupBeforeMove +
                            ",\"diagnosticError\":" + (decError != null ? DecQuote(decError) : "null") +
                            ",\"samples\":" + decSamples + ",\"lowSpeedSamples\":" + decLowSamples + ",\"controlClosedSamples\":" + decControlClosedSamples +
                            ",\"firstFrame\":" + decFirstFrame + ",\"lastFrame\":" + decLastFrame + ",\"lowestObservedSpeed\":" + DecFloat(decLowestSpeed) +
                            ",\"maximumObservedSpeed\":" + DecFloat(decObservedMaxSpeed) + ",\"originalMaxSpeedScalar\":" + DecFloat(maxSpeed) +
                            ",\"sampledDisplacement\":" + JsonUtility.ToJson(decLastPosition - decStartPosition) +
                            ",\"finalDisplacement\":" + JsonUtility.ToJson(_s.Rig.transform.position - p0) +
                            ",\"firstLowSnapshotMeaning\":\"first_threshold_low_only;global_minimum_scalar_only\",\"start\":" + (decStart ?? "null") +
                            ",\"first\":" + (decFirst ?? "null") + ",\"firstLow\":" + (decFirstLow ?? "null") + ",\"end\":" + (decEnd ?? "null") + "}");
                    }
                    catch (System.Exception) { /* The observer cannot replace the original assertion outcome. */ }
                }
            }
            // END S02_R2 DEC finally observer wrapper
        }

        [UnityTest]
        public IEnumerator AlmaSirasindaVurulma_SpecialAktifKalir_SureIsler_AtesEder()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return SpawnVeAl();
                float kalan0 = WeaponEvents.Special.ActiveRemaining;
                // A hit inside the grab window: the pickup is committed at the trigger, the clip is cosmetic.
                _s.Rig.Health.DiagnosisInvulnerable = false;   // the kit opens the player invulnerable (diagnosis); this test needs a real hit
                Assert.IsTrue(_s.Rig.Health.ApplyDamage(5f, _s.Rig.transform.position + Vector3.forward * 2f, "test.hit"), "vuruş uygulanmalı");
                yield return SpecialWeaponSahne.Kareler(6);
                Assert.IsTrue(_s.Controller.SpecialWeaponActive, "vuruş Special'ı iptal etmez");
                Assert.AreEqual(SpecialWeaponPhase.Active, WeaponEvents.Special.Phase);
                Assert.Less(WeaponEvents.Special.ActiveRemaining, kalan0, "aktif süre işlemeye devam eder");
                CollectionAssert.IsEmpty(sayac.Iptaller, "iptal yok");

                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Bekle(() => sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire) > 0, 5f, "Special ateşi");
                _s.Girdi.AtesBasili = false;
            }
        }

        [UnityTest]
        public IEnumerator AlmaSirasindaOlum_SpecialBiter_ElModeliGizlenir()
        {
            yield return Kur();
            LogAssert.ignoreFailingMessages = true;
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return SpawnVeAl();
                // WP-032C: the grab clip hands the weapon over at pickup_done (0.73 s; 0.8 s fallback) — the hand model shows then.
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "pickup_done / yedek");
                Assert.IsTrue(_s.Controller.SpecialRigVisible, "aktifken el rig'i görünür");
                _s.Rig.Health.DiagnosisInvulnerable = false;
                Assert.IsTrue(_s.Rig.Health.ApplyDamage(100000f, _s.Rig.transform.position + Vector3.forward * 2f, "test.kill"), "öldürücü vuruş uygulanmalı");
                Assert.IsTrue(_s.Rig.Health.IsDead, "oyuncu öldü");
                // Death ends the Special well before its own timer (11 s) would.
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.SpecialWeaponActive, 4f, "ölümle bitiş");
                Assert.IsFalse(_s.Controller.SpecialRigVisible, "ölümde el modeli gizlenir");
                Assert.IsTrue(sayac.Iptaller.Count > 0 || sayac.FazSirasi.Contains(SpecialWeaponPhase.None), "koşu sonu iptali / faz None");
            }
        }

        // ------------------------------------------------------------------ C · mechanism slots

        [UnityTest]
        public IEnumerator Mekanizma_A_KovanHerAtista_SarjorReloadBasinda_ZamanlayiciYedegi()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return null;
            int ammo0 = _s.Controller.Classic.Ammo;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo <= ammo0 - 3, 3f, "üç atış");
            _s.Girdi.AtesBasili = false;
            // WP-068 U12a: the HUD snapshot carries the runtime's own spread (bloom included) — the honest reticle input.
            Assert.AreEqual(_s.Controller.Classic.CurrentSpreadDegrees, WeaponEvents.Classic.CurrentSpreadDegrees, 1e-5f, "U12a: anlık yayılma snapshot'ta");
            Assert.AreEqual(1, WeaponEvents.Classic.PelletCount);
            Assert.AreEqual(0f, WeaponEvents.Classic.PelletRingDegrees, "A: halka yok");
            yield return SpecialWeaponSahne.Kareler(2);
            int shots = ammo0 - _s.Controller.Classic.Ammo;
            Assert.AreEqual(shots, Count(WeaponMechanismKind.CasingEjected), "A: her atışta bir kovan");
            for (int i = 0; i < _mechanism.Count; i++)
            {
                Assert.AreEqual(ClassicWeaponId.AssaultRifle, _mechanism[i].Weapon);
                Assert.IsFalse(_mechanism[i].FromClipEvent, "A/B kovanı atışta yayınlanır (klipten değil — WP-042C: additive fire klibi otomatik ateşte olayı düşürüyor)");
                Assert.Greater(_mechanism[i].Direction.magnitude, 0.9f, "yön birim");
            }

            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.MagazineDropped) == 1, 1.5f, "şarjör düşme (mag_out: klip ya da 0,4 s yedek)");
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.ShellSeated) == 1, 2f, "şarjör takma (mag_in: klip ya da yedek)");
            Debug.Log("[DD-TEST] wp042c.mechanism A clipDriven=" + _s.Controller.ClipDrivesMechanism + " magOutFromClip=" + _mechanism.Find(e => e.Kind == WeaponMechanismKind.MagazineDropped).FromClipEvent);
            Assert.AreEqual(1, Count(WeaponMechanismKind.MagazineDropped), "reload başına tek şarjör");
        }

        [UnityTest]
        public IEnumerator Mekanizma_C_KovanPumpta_FisekUcKez_HalkaDeseniGercekAtista()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.CombatShotgun, _s.Config);
            yield return null;
            Assert.IsTrue(_s.Controller.Classic.UsesPelletRing, "C halka deseni");
            int ammo0 = _s.Controller.Classic.Ammo;
            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo == ammo0 - 1, 2f, "tek atış");
            Assert.AreEqual(_s.Controller.Classic.PelletCount, WeaponEvents.Classic.PelletCount, "U12a: pellet sayısı snapshot'ta");
            Assert.AreEqual(_s.Controller.Classic.PelletRingDegrees, WeaponEvents.Classic.PelletRingDegrees, 1e-5f, "U12a: halka yarı açısı snapshot'ta");
            Assert.Greater(WeaponEvents.Classic.PelletRingDegrees, 0f, "C: ikinci halka");
            _s.Girdi.AtesBasti = false;
            _s.Girdi.AtesBasili = false;
            Assert.AreEqual(0, Count(WeaponMechanismKind.CasingEjected), "C: kovan atış anında değil, pump'ta");
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.CasingEjected) == 1, 1.5f, "pump (eject ≈ 0,4 s)");

            // WP-059C: the fire clip's own pump (eject f12, chamber f20 ≈ 0.67 s) may land before or during the
            // reload (the additive fire clip keeps playing under the override reload — v006b) or be cut (v005b):
            // the shot's pump contributes 0–1 chambers, the reload's pump exactly one, and the reload's is the LAST
            // mechanism event (chamber f60 after mag_in_3 f46).
            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
            float reload = _s.Controller.Classic.ReloadDuration;
            Assert.Greater(reload, 1f);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, reload + 1f, "reload biter");
            Assert.AreEqual(0, Count(WeaponMechanismKind.MagazineDropped), "C: şarjör düşmez (fişek)");
            Assert.AreEqual(3, Count(WeaponMechanismKind.ShellSeated), "C: üç fişek slotu (AP-009C mag_in / mag_in_2 / mag_in_3; klip ya da yedek)");
            Assert.That(Count(WeaponMechanismKind.Chambered), Is.InRange(1, 2), "C: atışın pump'ı (0–1) + reload'un pump chamber'ı (1)");
            Assert.AreEqual(WeaponMechanismKind.Chambered, _mechanism[_mechanism.Count - 1].Kind, "C: reload'un son mekanizma olayı chamber (pump)");
        }

        /// <summary>
        /// C-00 30.09 M-21 · the break-open contract (sg_2 Nito): no casing at the shot; the reload opens the gun and both
        /// spent shells fly (on the magazine timeline's mag_out mark, the interim step); no magazine drops.
        /// </summary>
        [UnityTest]
        public IEnumerator Mekanizma_Kirma_sg2_AtistaKovanYok_ReloaddaIkiKovan()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.Sg2, _s.Config);
            yield return null;
            int ammo0 = _s.Controller.Classic.Ammo;
            int casing0 = Count(WeaponMechanismKind.CasingEjected), mag0 = Count(WeaponMechanismKind.MagazineDropped);
            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo == ammo0 - 1, 2f, "tek atış");
            _s.Girdi.AtesBasti = false;
            _s.Girdi.AtesBasili = false;
            yield return new WaitForSeconds(0.8f);   // past any pump-back window (0.40 s)
            Assert.AreEqual(casing0, Count(WeaponMechanismKind.CasingEjected), "kırma: atışta kovan yok");

            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
            float reload = _s.Controller.Classic.ReloadDuration;
            Assert.Greater(reload, 1f);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, reload + 1f, "reload biter");
            Assert.AreEqual(casing0 + 2, Count(WeaponMechanismKind.CasingEjected), "kırma: açılışta iki kovan");
            Assert.AreEqual(mag0, Count(WeaponMechanismKind.MagazineDropped), "kırma: şarjör düşmez");
        }

        [UnityTest]
        public IEnumerator Titresim_TakiliSilahaGore_KusanmadaOnbellek()
        {
            yield return Kur();
            var rumble = new DD.Prototype.Inputs.GamepadRumble();
            try
            {
                rumble.Subscribe();
                _s.Controller.EquipClassic(ClassicWeaponId.CombatShotgun, _s.Config);
                yield return null;
                ClassicWeaponSettings c = _s.Config.Weapons.Find(ClassicWeaponId.CombatShotgun);
                Assert.AreEqual(c.RumbleFraction, rumble.WeaponFireFraction, 1e-4f);
                Assert.AreEqual(c.RumbleMilliseconds, rumble.WeaponFireMilliseconds);
                _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
                yield return null;
                ClassicWeaponSettings a = _s.Config.Weapons.Find(ClassicWeaponId.AssaultRifle);
                Assert.AreEqual(a.RumbleFraction, rumble.WeaponFireFraction, 1e-4f);
            }
            finally
            {
                rumble.Unsubscribe();
                rumble.Dispose();
            }
        }
    
        private sealed class SabitRastgele : ISpecialWeaponRandom
        {
            private readonly float _v;
            public SabitRastgele(float v) { _v = v; }
            public float Value() => _v;
            public float Range(float min, float max) => min + (max - min) * _v;
            public int Range(int min, int maxExclusive) => min;
        }
    }
}
