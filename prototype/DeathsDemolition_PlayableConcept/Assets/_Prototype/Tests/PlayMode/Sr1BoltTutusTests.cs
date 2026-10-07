using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R3 (S-02 triage; C-00 07.10 option a): sr_1's bolt cycle (Cycle_V3, the override layer's one-shot after each shot)
    /// counts as the upper body's rest, so the weapon-first hold and the support grip stay on through it — the clip's own rotation
    /// rides on the hold (WeaponMountAlignment.HoldDelta), the weapon stays at the hold pivot. Before, both gates closed for the
    /// cycle and the weapon swung to the clip's pose (~91 cm at the muzzle against the chest, top-down, S-02 r3td1). Top-down camera.
    /// Acceptance (C-00 07.10): hold weight 1 on every frame in Cycle_V3; the support hand on the handguard through it (grip weight 1,
    /// ≤ 1 cm — the clip's lh_release is not taken in the cycle); the muzzle's travel against the chest per shot (the shot and the
    /// whole bolt cycle, 2 s) ≤ 1.2 × the clip-only path's on the same metric — a regression guard: DD_HOLD_IK=0 measured 22.6 cm
    /// (r3e), the hold 25.5 cm (r3c; the difference is WP-087 C's kick the arms now carry), the gates closing ~91 cm. The frames'
    /// weights and the kick-window / after split are logged.
    /// </summary>
    [TestFixture]
    [Category("WP083")]
    public sealed class Sr1BoltTutusTests
    {
        // 1.2 × the clip-only path's 22.6 cm on this metric (r3e, 07.10). The 16 cm first proposed came from S-02's feel window (12
        // frames, back only) — another metric (C-00 07.10)
        private const float MuzzleTravelMax = 0.27f;
        private SpecialWeaponSahne _s;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
            // the owner's A/B switch, as R2HeroOlcumTests (r2-measure-contract measuredBy.env): DD_HOLD_IK=0 measures the clip-only path
            WeaponHoldIk.Enabled = System.Environment.GetEnvironmentVariable("DD_HOLD_IK") != "0";
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Sr1_BoltDongusu_TutusVeKavramaAcik_NamluKlipYolununEn120si()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            if (WeaponHoldIk.Enabled) Assert.IsNotNull(body.HoldIk, "WeaponHoldIk yok (varsayılan açık olmalı)");
            _s.Controller.EquipClassic(ClassicWeaponId.Sr1, _s.Config);
            yield return SpecialWeaponSahne.Kareler(90);
            Animator a = body.Animator;
            Transform chest = a.GetBoneTransform(HumanBodyBones.Chest);
            Transform muzzle = Muzzle(body.WeaponSocket);
            Assert.IsNotNull(chest, "göğüs kemiği yok");
            Assert.IsNotNull(muzzle, "sr_1'de Muzzle soketi yok");
            int layer = body.OverrideLayerIndex;
            Assert.GreaterOrEqual(layer, 0, "override katmanı yok");
            int cycleHash = Animator.StringToHash(C.CycleV3State);
            var report = new List<string>();
            int cycleFrames = 0, openGateMisses = 0, gripMisses = 0;
            float worstTravel = 0f, minCycleWeight = 1f, minGripWeight = 1f, worstGripCm = 0f;
            for (int shot = 0; shot < 3; shot++)
            {
                yield return SpecialWeaponSahne.Bekle(() => (!WeaponHoldIk.Enabled || body.HoldIk.Weight >= 1f) && !_s.Controller.Classic.IsReloading, 5f, "sr_1 tutuşu dinlenmede değil");
                if (!WeaponHoldIk.Enabled) yield return SpecialWeaponSahne.Kareler(30);   // the clip-only path: the previous cycle out
                Vector3 rest = InChest(chest, muzzle.position);
                _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                yield return null;
                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                float travel = 0f, kick = 0f, bolt = 0f;
                int inCycle = 0;
                for (int f = 0; f < 120; f++)   // 2 s: the shot, the bolt cycle and its exit
                {
                    yield return null;
                    float d = Vector3.Distance(InChest(chest, muzzle.position), rest);
                    travel = Mathf.Max(travel, d);
                    if (f < 12) kick = Mathf.Max(kick, d); else bolt = Mathf.Max(bolt, d);   // the kick window (S-02's 12 frames), then the bolt
                    if (a.GetCurrentAnimatorStateInfo(layer).shortNameHash != cycleHash) continue;
                    inCycle++;
                    float hw = body.HoldIk != null ? body.HoldIk.Weight : 0f;
                    minCycleWeight = Mathf.Min(minCycleWeight, hw);
                    if (WeaponHoldIk.Enabled && hw < 0.999f) openGateMisses++;
                    // the support hand stays on the handguard through the cycle (lh_release is not taken there)
                    WeaponGripIk grip = body.GripIk;
                    float gw = grip != null ? grip.Weight : 0f;
                    minGripWeight = Mathf.Min(minGripWeight, gw);
                    if (gw < 0.999f) gripMisses++;
                    else if (!float.IsNaN(grip.LastError)) worstGripCm = Mathf.Max(worstGripCm, grip.LastError * 100f);
                }

                cycleFrames += inCycle;
                worstTravel = Mathf.Max(worstTravel, travel);
                report.Add("shot " + shot + " muzzle travel " + (travel * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm (kick window " +
                           (kick * 100f).ToString("F1", CultureInfo.InvariantCulture) + ", after " + (bolt * 100f).ToString("F1", CultureInfo.InvariantCulture) + "), Cycle_V3 frames " + inCycle);
            }

            Debug.Log("[DD-TEST] wp083.sr1bolt hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " holdWeightMinInCycle=" + minCycleWeight.ToString("F3", CultureInfo.InvariantCulture) + " gripWeightMinInCycle=" +
                      minGripWeight.ToString("F3", CultureInfo.InvariantCulture) + " gripErrMaxCm=" + worstGripCm.ToString("F2", CultureInfo.InvariantCulture) + " muzzleTravelMaxCm=" +
                      (worstTravel * 100f).ToString("F1", CultureInfo.InvariantCulture) + " :: " + string.Join(" | ", report));
            // the source is there: the bolt cycle played
            Assert.Greater(cycleFrames, 0, "Cycle_V3 hiç oynamadı (sr_1 bolt döngüsü yok)");
            Assert.AreEqual(0, openGateMisses, "Cycle_V3 karesinde tutuş ağırlığı < 1 (en düşük " + minCycleWeight.ToString("F3", CultureInfo.InvariantCulture) + ")\n" + string.Join("\n", report));
            Assert.LessOrEqual(worstTravel, MuzzleTravelMax, "sr_1 namlusu atış başına göğse göre > 27 cm oynuyor (yalnız klip yolunun 1,2 katı)\n" + string.Join("\n", report));
            Assert.AreEqual(0, gripMisses, "Cycle_V3 karesinde sol el kavraması bırakıyor (en düşük ağırlık " + minGripWeight.ToString("F3", CultureInfo.InvariantCulture) + ")");
            Assert.LessOrEqual(worstGripCm, 1f, "Cycle_V3'te sol el kundaktan > 1 cm (H1 eşiği)");
        }

        // the muzzle in the chest's frame, in metres (the chest's rotation and position only, no scale)
        private static Vector3 InChest(Transform chest, Vector3 world) => Quaternion.Inverse(chest.rotation) * (world - chest.position);

        private static Transform Muzzle(Transform socket)
        {
            if (socket == null) return null;
            foreach (Transform t in socket.GetComponentsInChildren<Transform>(false))
                if (t.name == "Muzzle") return t;
            return null;
        }
    }
}
