using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 hero gates (S-06, tools/qa/r2-gates.json, measured on the default player path — no test switch).
    /// <para>
    /// H1 "hand to grip socket distance": for every enabled classic weapon, in third person, standing then walking, maxCm is the
    /// largest of two distances, in cm:
    /// <list type="bullet">
    /// <item>right — the hand's socket_weapon_r ↔ the definition's grip point (GripOffset in the mount frame), the point WeaponMountAlignment
    /// puts on the socket (C-00 05.10, H1 decision B: grip decision B of 25.09 stands — the AP-021 marker_grip is the model origin, the
    /// trigger root; each GripOffset is checked against the LOD0 grip region by Ap021ClassicModelGateTests);</item>
    /// <item>left — the left hand ↔ WeaponGripIk's goal while the grip IK carries full weight.</item>
    /// </list>
    /// Frames of reload / switch, where the clip takes the hands off, are not counted.
    /// </para>
    /// The default path has the WP-083 B2c/B2d hold on (C-00 05.10); DD_HOLD_IK=0 (the owner's switch) measures it off.
    /// [Explicit]: the R2 chain runs it by its full name; it writes r2-H1.json through R2Olcum when DD_R2_MEASURE_DIR is set and
    /// fails on the contract's limit itself.
    /// </summary>
    [TestFixture]
    public sealed class R2HeroOlcumTests
    {
        public const string H1Name = "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus";
        public const string H1bName = "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1b_SolEl_Kavrama_TumKlasikSilahlar_DurusYuruyus";
        public const string H1cName = "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1c_SolKol_Erisim_TumKlasikSilahlar_DurusYuruyus";
        private SpecialWeaponSahne _s;
        private bool _fpArmsBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // the owner's A/B switch (r2-measure-contract measuredBy.env): DD_HOLD_IK=0 measures with the WP-083 B2c/B2d hold off
            // (it is on by default since C-00 05.10)
            WeaponHoldIk.Enabled = System.Environment.GetEnvironmentVariable("DD_HOLD_IK") != "0";
            // WP-083 B2f A/B: DD_GRASP=0 measures with the support hand's grasp off (on by default)
            WeaponGripIk.GraspEnabled = System.Environment.GetEnvironmentVariable("DD_GRASP") != "0";
            // the FP arms are off on the default path (PlayerSkinnedVisual.FirstPersonV3Arms); the FP contact test turns them on
            _fpArmsBefore = PlayerSkinnedVisual.FirstPersonV3Arms;
            if (TestContext.CurrentContext.Test.Name.Contains("FPKollar")) PlayerSkinnedVisual.FirstPersonV3Arms = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // R3 (C-00 07.10, S-01 inventory): the next test starts in the process default — TopDown (user 06.10: tests run top-down only);
            // a ThirdPerson left here ran the PlayMode tests after this fixture in TP (R2FiksturKameraTests)
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
            PlayerSkinnedVisual.FirstPersonV3Arms = _fpArmsBefore;
        }

        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Transform socket = body.WeaponSocket, lHand = body.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            // C-00 05.10 (WP-082 hero v2 reach pre-study): the support arm's elbow, interior angle (180 = straight) — report-only
            // elbowMaxDeg; after the hero v2 import it becomes a check (≤ 165°, a reach margin of ≥ 1 cm)
            Transform lUpper = body.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), lLower = body.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            Transform mount = binding != null ? binding.WeaponMount : null;
            Assert.IsNotNull(mount, "silah montajı yok");
            var alignment = mount.GetComponent<WeaponMountAlignment>();
            Assert.IsNotNull(socket, "socket_weapon_r yok");
            Assert.IsNotNull(lHand, "sol el kemiği yok");
            var report = new List<string>();
            float maxRight = 0f, maxLeft = 0f, elbowMax = 0f;
            int samples = 0, leftSamples = 0, weapons = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);   // the switch-in has played out
                // the source is in the record (S-10 R2 standard): the alignment that places the grip point, a weapon model in the hand
                Assert.IsNotNull(alignment, "R2 H1: WeaponMountAlignment yok — tutamak noktası ölçülemez");
                Assert.IsNotNull(mount.Find("ClassicWeaponRig"), d.RosterKey + ": montajda silah yok");
                Assert.IsTrue(_s.Controller.ClassicRigHasAuthoredModel, d.RosterKey + ": yazılmış silah modeli takılı değil");
                weapons++;
                float right = 0f, left = 0f, elbow = 0f;
                int n = 0, nLeft = 0;
                string leftWhy = "";
                for (int f = 0; f < 180; f++)
                {
                    _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;   // stand 1 s, walk 1.5 s, stop
                    yield return null;
                    if (_s.Controller.Classic != null && _s.Controller.Classic.IsReloading) continue;
                    Vector3 grip = mount.TransformPoint(alignment.GripOffset);
                    right = Mathf.Max(right, Vector3.Distance(socket.position, grip));
                    n++;
                    WeaponGripIk ik = body.GripIk;
                    if (ik != null && ik.Weight >= 1f && !float.IsNaN(ik.LastError))
                    {
                        if (ik.LastError > left) leftWhy = SupportWhy(body);
                        left = Mathf.Max(left, ik.LastError);
                        nLeft++;
                        if (lUpper != null && lLower != null && lHand != null)
                            elbow = Mathf.Max(elbow, Vector3.Angle(lUpper.position - lLower.position, lHand.position - lLower.position));
                    }
                }

                _s.Girdi.Yon = Vector2.zero;
                maxRight = Mathf.Max(maxRight, right);
                maxLeft = Mathf.Max(maxLeft, left);
                elbowMax = Mathf.Max(elbowMax, elbow);
                samples += n;
                leftSamples += nLeft;
                report.Add(d.RosterKey + " right " + (right * 100f).ToString("F2", CultureInfo.InvariantCulture) + " (grip point)" +
                           " left " + (nLeft > 0 ? (left * 100f).ToString("F2", CultureInfo.InvariantCulture) + " (n " + nLeft + ")" : "n/a") + " cm, elbow " +
                           (nLeft > 0 ? elbow.ToString("F0", CultureInfo.InvariantCulture) + "°" : "n/a") + ", frames " + n + leftWhy);
            }

            float maxCm = Mathf.Max(maxRight, maxLeft) * 100f;
            Debug.Log("[DD-TEST] wp083.h1 hold=" + (WeaponHoldIk.Enabled ? "on" : "off") + " maxCm=" + maxCm.ToString("F2", CultureInfo.InvariantCulture) +
                      " elbowMaxDeg=" + elbowMax.ToString("F1", CultureInfo.InvariantCulture) + " :: " + string.Join(" | ", report));
            // the source is in the record before the artefact (S-10 R2 standard): weapons in the hand, frames, the support hand measured
            Assert.Greater(weapons, 0, "ölçülen silah yok");
            Assert.Greater(samples, 0, "ölçülen kare yok");
            Assert.Greater(leftSamples, 0, "sol el hiçbir silahta tam ağırlıkta ölçülmedi");
            // holdOn (S-10 06.10): the path the measure was taken on is in the record — 1 the weapon-first hold on (the default), 0 off
            R2Olcum.Yaz("H1", H1Name, new Dictionary<string, double> { { "maxCm", maxCm }, { "elbowMaxDeg", System.Math.Round(elbowMax, 1) }, { "holdOn", WeaponHoldIk.Enabled ? 1 : 0 } }, samples);
            Assert.LessOrEqual(maxCm, 1.0f, "R2 H1: el ↔ tutamak > 1 cm\n" + string.Join("\n", report));
        }

        /// <summary>
        /// WP-083 B2d (C-00 05.10, v4 frames: the upper arm turned out and back behind the body): the hold's solves swing the arms,
        /// they do not roll them. For every enabled classic weapon, standing then walking, the right (WeaponHoldIk) and the left
        /// (WeaponGripIk) upper arm's roll about its own axis against the clip's pose stays within 15° at full weight.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Tutus_UstKolRoll_KlipPozuna_15Derece()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Assert.IsNotNull(body.HoldIk, "WeaponHoldIk yok (varsayılan açık olmalı)");
            var report = new List<string>();
            float maxR = 0f, maxL = 0f;
            int nR = 0, nL = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                float r = 0f, l = 0f;
                for (int f = 0; f < 150; f++)
                {
                    _s.Girdi.Yon = f >= 50 && f < 130 ? Vector2.up : Vector2.zero;
                    yield return null;
                    if (body.HoldIk.Weight >= 1f) { r = Mathf.Max(r, body.HoldIk.UpperArmRollDegrees); nR++; }
                    if (body.GripIk != null && body.GripIk.Weight >= 1f) { l = Mathf.Max(l, body.GripIk.UpperArmRollDegrees); nL++; }
                }

                _s.Girdi.Yon = Vector2.zero;
                maxR = Mathf.Max(maxR, r);
                maxL = Mathf.Max(maxL, l);
                report.Add(d.RosterKey + " right " + r.ToString("F1", CultureInfo.InvariantCulture) + "° left " + l.ToString("F1", CultureInfo.InvariantCulture) + "°");
            }

            Debug.Log("[DD-TEST] wp083.armroll right max " + maxR.ToString("F1", CultureInfo.InvariantCulture) + "° (n " + nR + ") left max " + maxL.ToString("F1", CultureInfo.InvariantCulture) + "° (n " + nL + ") :: " + string.Join(" | ", report));
            Assert.Greater(nR, 0, "sağ kol tam ağırlıkta ölçülmedi");
            Assert.Greater(nL, 0, "sol kol tam ağırlıkta ölçülmedi");
            Assert.LessOrEqual(maxR, 15f, "sağ üst kol klip pozuna göre > 15° dönüyor\n" + string.Join("\n", report));
            Assert.LessOrEqual(maxL, 15f, "sol üst kol klip pozuna göre > 15° dönüyor\n" + string.Join("\n", report));
        }

        /// <summary>
        /// The left hand's miss, explained (at the worst frame): the goal on the kicked body vs the same point on the rig's rest frame,
        /// the arm's reach shortfall (shoulder → goal minus upper + forearm), the hold's weight and pull-back.
        /// </summary>
        private static string SupportWhy(PlayerSkinnedVisual body)
        {
            WeaponGripIk ik = body.GripIk;
            if (ik == null || !ik.TryGetSupportGoal(out Vector3 goal)) return " [why n/a]";
            Transform frame = ik.WeaponFrame;
            float bodyVsRig = frame != null && frame.parent != null ? Vector3.Distance(goal, frame.parent.TransformPoint(frame.InverseTransformPoint(goal))) : -1f;
            Transform u = body.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), l = body.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                h = body.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            float reach = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, h.position);
            float shortfall = Vector3.Distance(u.position, goal) - reach;
            string pose = frame != null ? " bodyRot=" + Quaternion.Angle(Quaternion.identity, frame.localRotation).ToString("F1", CultureInfo.InvariantCulture) + "° bodyZ=" +
                                          (frame.localPosition.z * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm" : "";
            return " [why frame=" + (frame != null ? frame.name : "-") + pose + " bodyVsRig=" + (bodyVsRig * 100f).ToString("F1", CultureInfo.InvariantCulture) +
                   "cm shortfall=" + (shortfall * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm hold=" + (body.HoldIk != null ? body.HoldIk.Weight.ToString("F2", CultureInfo.InvariantCulture) +
                   " pull=" + (body.HoldIk.PullBack * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm" : "-") + "]";
        }

        /// <summary>
        /// WP-083 B2d (C-00 05.10): the support hand stays on the weapon while it fires. Every enabled classic weapon fires ten shots
        /// (presses at the weapon's own pace); on every frame outside a reload the left hand ↔ its support goal (WeaponGripIk, at full
        /// weight; the goal rides the authored body, so the recoil kick is in it) stays within 1 cm.
        /// A weapon on <see cref="AtisBilinenKirmizi"/> is measured and reported on its own line, outside the 1 cm check.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Atis_SolEl_DestekNoktasinda_TumSilahlar()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            var report = new List<string>();
            var known = new List<string>();
            float worst = 0f;
            int measuredWeapons = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                int ammo0 = _s.Controller.Classic.Ammo, shots = 0, n = 0;
                float max = 0f;
                string why = "";
                for (int i = 0; i < 10; i++)
                {
                    int before = _s.Controller.Classic.Ammo;
                    _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                    for (int f = 0; f < 3; f++) { yield return null; Sample(); }
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    for (int f = 0; f < 15; f++) { yield return null; Sample(); }
                    if (_s.Controller.Classic.Ammo < before) shots++;
                }

                void Sample()
                {
                    if (_s.Controller.Classic.IsReloading || body.GripIk == null || body.GripIk.Weight < 1f || float.IsNaN(body.GripIk.LastError)) return;
                    if (body.GripIk.LastError > max) why = SupportWhy(body);
                    max = Mathf.Max(max, body.GripIk.LastError);
                    n++;
                }

                yield return SpecialWeaponSahne.Kareler(20);
                string line = d.RosterKey + " shots " + shots + " left max " + (max * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm (n " + n + ")" + why;
                if (AtisBilinenKirmizi.TryGetValue(d.RosterKey, out string reason))
                {
                    known.Add(line + " — " + (max <= 0.01f ? "now within 1 cm, take it off the list" : reason));
                    continue;
                }

                if (shots > 0 && n > 0) { measuredWeapons++; worst = Mathf.Max(worst, max); }
                report.Add(line);
            }

            Debug.Log("[DD-TEST] wp083.firehand worst " + (worst * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm :: " + string.Join(" | ", report));
            foreach (string k in known) Debug.Log("[DD-TEST] wp083.firehand known-red " + k);
            Assert.Greater(measuredWeapons, 0, "atış + sol el ölçülen silah yok");
            Assert.LessOrEqual(worst, 0.01f, "atış boyunca sol el destekten > 1 cm\n" + string.Join("\n", report));
        }

        /// <summary>
        /// Weapons whose fire is a known red for the support hand, with the reason. Both share one root: the rig's recoil kick inside
        /// WeaponVisualRig (S-02) carries the support point past the arm's reach; the fix is S-02's held-kick package, which moves the
        /// kick onto the hold's pivot (WP-087). The rows come off in WP-087 step D, accepted by this test at ≤ 1 cm.
        /// ar_2 (C-00 05.10, decision c + b): accumulated kick, WeaponBody 16.4 cm / 14.8°, left hand 12.63 cm (K/wp-083 b2l-r2).
        /// C came off in B2f (06.10): the contact grasp put its arm back in reach (elbow 173° → 132°), its fire 2.68 → 0.00 cm (b2f15).
        /// </summary>
        static readonly Dictionary<string, string> AtisBilinenKirmizi = new Dictionary<string, string>
        {
            { "ar_2", "known red: S-02 WeaponVisualRig accumulated kick, 12.63 cm (b2l-r2), held-kick package WP-087, row off in step D (C-00 05.10)" },
        };

        /// <summary>
        /// WP-083 B2d diagnostics: the long weapons' stock penetration (attach_stock vs the AP-006 body, nearest baked vertex and its
        /// normal — WeaponModelSliceTests' measure, ≤ 2 cm) against the support hand's error (H1, ≤ 1 cm) for candidate stock clearances.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Tani_Tutus_DipcikAcikligi_GomulmeVeSolEl()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var ids = new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Sg2, ClassicWeaponId.Ar2, ClassicWeaponId.Lmg1, ClassicWeaponId.Sr1, ClassicWeaponId.Lmg2, ClassicWeaponId.Smg2 };
            var rows = new List<string>();
            foreach (Vector3 hold in new[] { new Vector3(0.13f, -0.12f, 0.22f), new Vector3(0.16f, -0.12f, 0.22f), new Vector3(0.10f, -0.16f, 0.22f), new Vector3(0.13f, -0.16f, 0.22f) })
            {
                WeaponHoldIk.HipHold = hold;
                foreach (ClassicWeaponId id in ids)
                {
                    _s.Controller.EquipClassic(id, _s.Config);
                    yield return SpecialWeaponSahne.Kareler(70);
                    _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                    yield return null;
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    float penMax = 0f, leftMax = 0f;
                    for (int f = 0; f < 100; f++)
                    {
                        yield return null;
                        Transform st = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachStock);
                        if (st != null) penMax = Mathf.Max(penMax, Penetration(body, st.position));
                        if (f > 40 && body.GripIk != null && body.GripIk.Weight >= 1f) leftMax = Mathf.Max(leftMax, body.GripIk.LastError);
                    }

                    rows.Add(hold.ToString("F2") + " " + id + " pull " + body.HoldIk.PullBack.ToString("F3", CultureInfo.InvariantCulture) +
                             " penMax " + (penMax * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm leftMax " + (leftMax * 100f).ToString("F2", CultureInfo.InvariantCulture) + "cm");
                }
            }

            WeaponHoldIk.ResetStatics();
            Debug.Log("[DD-TEST] wp083.stockdiag " + string.Join(" | ", rows));
            Assert.Greater(rows.Count, 0);
        }

        /// <summary>
        /// WP-083 B2e diagnostics (C-00 05.10, ar_2 fire: the body's recoil kick carries the support point 20 cm, the arm 12.5 cm short):
        /// candidate ar_2 rows — the hold raised (HipHold y) and the support point moved back along the handguard (weapon −Z) — each
        /// scored on the rest hold (left error), ten presses (left error at full weight, outside reload) and the stock's penetration.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator Tani_Ar2_AtisSatiri_YukseklikVeDestek()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var rows = new List<string>();
            foreach (float up in new[] { 0f, 0.04f, 0.08f })
            {
                foreach (float back in new[] { 0f, 0.05f, 0.10f })
                {
                    WeaponHoldIk.HipHold = WeaponHoldIk.DefaultHipHold + new Vector3(0f, up, 0f);
                    _s.Controller.EquipClassic(ClassicWeaponId.Ar2, _s.Config);
                    yield return SpecialWeaponSahne.Kareler(60);
                    WeaponGripIk ik = body.GripIk;
                    Vector3 hiw0 = ik.HandInWeapon;
                    bool has = ik.HasHandInWeapon;
                    if (has) ik.SetHandInWeapon(true, hiw0 - new Vector3(0f, 0f, back));
                    float rest = 0f, fire = 0f, pen = 0f;
                    string why = "";
                    for (int f = 0; f < 40; f++)
                    {
                        yield return null;
                        if (ik.Weight >= 1f && !float.IsNaN(ik.LastError)) rest = Mathf.Max(rest, ik.LastError);
                    }

                    int shots = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        int before = _s.Controller.Classic.Ammo;
                        _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                        for (int f = 0; f < 18; f++)
                        {
                            yield return null;
                            if (f == 2) { _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false; }
                            Transform st = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachStock);
                            if (st != null) pen = Mathf.Max(pen, Penetration(body, st.position));
                            if (_s.Controller.Classic.IsReloading || ik.Weight < 1f || float.IsNaN(ik.LastError)) continue;
                            if (ik.LastError > fire) why = SupportWhy(body);
                            fire = Mathf.Max(fire, ik.LastError);
                        }

                        if (_s.Controller.Classic.Ammo < before) shots++;
                    }

                    if (has) ik.SetHandInWeapon(true, hiw0);
                    rows.Add("up " + up.ToString("F2", CultureInfo.InvariantCulture) + " back " + back.ToString("F2", CultureInfo.InvariantCulture) + " has " + has +
                             " shots " + shots + " rest " + (rest * 100f).ToString("F2", CultureInfo.InvariantCulture) + "cm fire " + (fire * 100f).ToString("F2", CultureInfo.InvariantCulture) +
                             "cm pen " + (pen * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm" + why);
                    yield return SpecialWeaponSahne.Kareler(90);
                }
            }

            WeaponHoldIk.ResetStatics();
            Debug.Log("[DD-TEST] wp083.ar2diag " + string.Join(" | ", rows));
            Assert.Greater(rows.Count, 0);
        }

        /// <summary>
        /// WP-083 B2f diagnostics (AD 06.10 MUST-2 / MUST-4, measure before the fix): both hands against the held weapon's surface for
        /// every enabled classic weapon, standing then walking, at full hold weight. The palm point (wrist → middle knuckle, 55 %, 1.2 cm
        /// out along the palm normal) and the left index / middle fingertips (the distal bone carried one more distal length) against
        /// the nearest point of the weapon's LOD0 triangles; the left palm normal's angle to the weapon frame's +Y (a handguard held from
        /// below) and +X (a vertical foregrip held from the left). The palm normal is the hand's axis that points down in the avatar's
        /// T-pose (the humanoid T-pose's palms face down).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator Tani_Eller_AvucSilahYuzeyi_TumSilahlar()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Animator a = body.Animator;
            Vector3 lPalmLocal = PalmNormalLocal(a, HumanBodyBones.LeftHand);
            int thumbAgree = 0, curlAgree = 0, normals = 0;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var rows = new List<string>();
            int weapons = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                float rGap = 0f, lGap = 0f, tipGap = 0f, upMax = 0f, sideMax = 0f, gUpMax = 0f, gSideMax = 0f, toAxisMax = 0f;
                string dir = System.Environment.GetEnvironmentVariable("DD_B2D_COMPARE_DIR");
                int n = 0;
                for (int f = 0; f < 180; f++)
                {
                    _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;
                    yield return null;
                    if (f % 5 != 0 || _s.Controller.Classic.IsReloading) continue;
                    WeaponGripIk ik = body.GripIk;
                    Transform frame = ik != null ? ik.WeaponFrame : null;
                    if (frame == null || body.HoldIk == null || body.HoldIk.Weight < 1f || ik.Weight < 1f) continue;
                    n++;
                    // the palm point on the geometric palm normal (the same palm WeaponGripIk's grasp places; the T-pose normal is
                    // reported beside it only)
                    Quaternion lInv = Quaternion.Inverse(a.GetBoneTransform(HumanBodyBones.LeftHand).rotation), rInv = Quaternion.Inverse(a.GetBoneTransform(HumanBodyBones.RightHand).rotation);
                    Vector3 rGeo = GeometricPalmNormal(a, false, out _, out _);
                    rGap = Mathf.Max(rGap, SurfaceGap(PalmPoint(a, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, rInv * rGeo), frame, verts, tris));
                    Vector3 lGeo = GeometricPalmNormal(a, true, out _, out _);
                    lGap = Mathf.Max(lGap, SurfaceGap(PalmPoint(a, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, lInv * lGeo), frame, verts, tris));
                    tipGap = Mathf.Max(tipGap, Mathf.Min(SurfaceGap(Fingertip(a, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal), frame, verts, tris),
                                                         SurfaceGap(Fingertip(a, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal), frame, verts, tris)));
                    Vector3 palm = a.GetBoneTransform(HumanBodyBones.LeftHand).rotation * lPalmLocal;
                    upMax = Mathf.Max(upMax, Vector3.Angle(palm, frame.up));
                    sideMax = Mathf.Max(sideMax, Vector3.Angle(palm, frame.right));
                    Vector3 geo = GeometricPalmNormal(a, true, out bool byThumb, out bool byCurl);
                    normals++;
                    if (byThumb) thumbAgree++;
                    if (byCurl) curlAgree++;
                    gUpMax = Mathf.Max(gUpMax, Vector3.Angle(geo, frame.up));
                    gSideMax = Mathf.Max(gSideMax, Vector3.Angle(geo, frame.right));
                    // H1b's palm measure: the palm normal against the direction from the palm to the barrel axis (Muzzle along +Z)
                    Transform muzzle = WeaponSocketContract.Find(frame, WeaponSocketContract.Muzzle);
                    Vector3 lPalm = PalmPoint(a, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, Quaternion.Inverse(a.GetBoneTransform(HumanBodyBones.LeftHand).rotation) * geo);
                    Vector3 origin = muzzle != null ? muzzle.position : frame.position;
                    Vector3 foot = origin + frame.forward * Vector3.Dot(lPalm - origin, frame.forward);
                    toAxisMax = Mathf.Max(toAxisMax, Vector3.Angle(geo, foot - lPalm));
                    if ((f == 30 || f == 105) && !string.IsNullOrEmpty(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                        var go = new GameObject("WP083_HandCam");
                        try
                        {
                            Camera cam = go.AddComponent<Camera>();
                            cam.fieldOfView = 35f;
                            cam.nearClipPlane = 0.02f;
                            // outside the weapon in the body's frame: from the character's left, a little ahead and above, looking at the
                            // left palm; a soft fill light on the hand
                            Transform bt = a.transform;
                            go.transform.position = lPalm - bt.right * 0.38f + bt.forward * 0.12f + bt.up * 0.14f;
                            go.transform.LookAt(lPalm, bt.up);
                            Light fill = go.AddComponent<Light>();
                            fill.type = LightType.Point;
                            fill.range = 0.9f;
                            fill.intensity = 0.6f;
                            Shot(dir, d.RosterKey + "_lefthand_" + (f == 30 ? "stand_" : "walk_") + (WeaponGripIk.GraspEnabled ? "B2f_after" : "B2f_before"), cam);
                        }
                        finally
                        {
                            UnityEngine.Object.Destroy(go);
                        }
                    }
                }

                _s.Girdi.Yon = Vector2.zero;
                if (n > 0) weapons++;
                rows.Add(d.RosterKey + " n " + n + " rightPalmGap " + Cm(rGap) + " leftPalmGap " + Cm(lGap) + " leftTipGap " + Cm(tipGap) + " cm leftPalmToUp " +
                         upMax.ToString("F0", CultureInfo.InvariantCulture) + "° toRight " + sideMax.ToString("F0", CultureInfo.InvariantCulture) + "° | geometric toUp " +
                         gUpMax.ToString("F0", CultureInfo.InvariantCulture) + "° toRight " + gSideMax.ToString("F0", CultureInfo.InvariantCulture) + "° palmToAxis " +
                         toAxisMax.ToString("F0", CultureInfo.InvariantCulture) + "°");
            }

            Debug.Log("[DD-TEST] wp083.handsurface grasp=" + (WeaponGripIk.GraspEnabled ? "on" : "off") + " geometric palm normal: thumb side agrees " + thumbAgree + "/" + normals + ", finger curl agrees " + curlAgree + "/" + normals +
                      " :: " + string.Join(" | ", rows));
            Assert.Greater(weapons, 0, "ölçülen silah yok");
        }

        /// <summary>
        /// WP-083 B2f (council 06.10 §1): each classic weapon's grasped-element row (WeaponGripIk.ElementOf: axis +Z handguard / +Y
        /// vertical foregrip, the section centre and radius in the weapon frame) agrees with the LOD0 section at the support station
        /// within 0.3 cm. The section: the LOD0 triangles cut at the station (the goal's coordinate along the axis), the cut points within
        /// <see cref="SectionReach"/> of the row's centre (of the goal without a row); centre = their bounds' centre, radius = half the
        /// bounds' narrower side.
        /// The measured rows are logged in C# (a missing or drifted row is red).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator KavramaSatiri_LOD0Kesitiyle_TumSilahlar() => KavramaSatiriOlc(DD.Prototype.CameraSystem.CameraMode.TopDown);

        /// <summary>The grasp rows against the LOD0 section from one game camera (R3, C-00 07.10: the top-down test above; the
        /// third-person twin, the same lines, in <see cref="R2HeroOlcumFptpTests"/> under FPTP).</summary>
        internal IEnumerator KavramaSatiriOlc(DD.Prototype.CameraSystem.CameraMode mode)
        {
            // the rows against the LOD0 section are camera-independent
            DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            var verts = new List<Vector3>();
            var rows = new List<string>();
            var bad = new List<string>();
            int weapons = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                WeaponGripIk ik = body.GripIk;
                Assert.IsNotNull(ik, "WeaponGripIk yok");
                Transform frame = ik.WeaponFrame;
                Assert.IsNotNull(frame, d.RosterKey + ": silah çerçevesi yok");
                Assert.IsTrue(ik.TryGetSupportGoal(out Vector3 g), d.RosterKey + ": destek noktası yok");
                // the row's own axis kind; the section is seeded at the row's centre on the goal's station (an off-mesh goal — the v3
                // sets' palm points on lmg_1 / lmg_2 / sr_1 / smg_2 — has no section about it), at the goal without a row
                WeaponGripIk.GraspElement row0 = WeaponGripIk.ElementOf(d.Id);
                bool vertical = row0.Valid ? row0.Vertical : d.Id == ClassicWeaponId.Rl1;
                Vector3 seed = frame.InverseTransformPoint(g);
                if (row0.Valid)
                {
                    if (vertical) { seed.x = row0.Centre.x; seed.z = row0.Centre.y; }
                    else { seed.x = row0.Centre.x; seed.y = row0.Centre.y; }
                }

                string variants = "";
                WeaponGripIk.GraspElement measured = default;
                int count = 0;
                float theta0 = vertical ? WeaponGripIk.WrapVertical : WeaponGripIk.WrapHandguard;
                // the reach follows the row (its radius + 1 cm, at least 3 cm): a fixed reach took neighbouring parts into B's / lmg_2's cut
                float rowReach = row0.Valid ? Mathf.Max(0.03f, row0.CurlRadius + 0.01f) : SectionReach;
                foreach (float reach in new[] { 0.03f, rowReach, 0.05f })
                {
                    WeaponGripIk.GraspElement m = Section(frame, seed, vertical, reach, verts, out int n, theta0);
                    variants += " reach" + (reach * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm(n " + n + " c " + m.Centre.x.ToString("F4", CultureInfo.InvariantCulture) + "," +
                                m.Centre.y.ToString("F4", CultureInfo.InvariantCulture) + " r " + m.Radius.ToString("F4", CultureInfo.InvariantCulture) + " curl " + m.CurlRadius.ToString("F4", CultureInfo.InvariantCulture) + ")";
                    if (Mathf.Approximately(reach, rowReach)) { measured = m; count = n; }
                }

                weapons++;
                WeaponGripIk.GraspElement row = WeaponGripIk.ElementOf(d.Id);
                float dev = row.Valid && row.Vertical == vertical
                    ? Mathf.Max((row.Centre - measured.Centre).magnitude, Mathf.Max(Mathf.Abs(row.Radius - measured.Radius), Mathf.Abs(row.CurlRadius - measured.CurlRadius)))
                    : float.PositiveInfinity;
                Vector3 goalLocal = frame.InverseTransformPoint(g);
                rows.Add("case ClassicWeaponId." + d.Id + ": return new GraspElement(" + (vertical ? "true" : "false") + ", new Vector2(" +
                         measured.Centre.x.ToString("F4", CultureInfo.InvariantCulture) + "f, " + measured.Centre.y.ToString("F4", CultureInfo.InvariantCulture) + "f), " +
                         measured.Radius.ToString("F4", CultureInfo.InvariantCulture) + "f, " + measured.CurlRadius.ToString("F4", CultureInfo.InvariantCulture) + "f);   // " + d.RosterKey + " goal " + goalLocal.ToString("F3") + " n " + count + variants);
                if (count < 8 || !(dev <= 0.003f)) bad.Add(d.RosterKey + " n " + count + " dev " + (float.IsInfinity(dev) ? "no row" : (dev * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm"));
            }

            Debug.Log("[DD-TEST] wp083.graspelement\n" + string.Join("\n", rows));
            Assert.Greater(weapons, 0, "ölçülen silah yok");
            Assert.IsEmpty(bad, "kavrama satırı LOD0 kesitinden > 0,3 cm ya da satır yok:\n" + string.Join("\n", bad));
        }

        /// <summary>
        /// WP-083 B2f diagnostics: the LOD0 section at each classic weapon's support station as an ASCII map (±10 cm about the weapon
        /// axis, 0.5 cm cells; '#' section, 'G' the support goal, '+' the weapon axis) — where the support hand meets the weapon, to
        /// pick the grasped element of a weapon whose goal is off the mesh.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Tani_KavramaKesiti_Haritasi_TumSilahlar()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var maps = new List<string>();
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                WeaponGripIk ik = body.GripIk;
                Transform frame = ik != null ? ik.WeaponFrame : null;
                if (frame == null || !ik.TryGetSupportGoal(out Vector3 g)) continue;
                bool vertical = d.Id == ClassicWeaponId.Rl1;
                Vector3 gl = frame.InverseTransformPoint(g);
                float station = vertical ? gl.y : gl.z;
                const int N = 41;
                const float cell = 0.005f;
                var grid = new char[N, N];
                for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) grid[i, j] = '.';
                foreach (MeshFilter mf in frame.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                    Mesh mesh = Readable(mf.sharedMesh);
                    mesh.GetVertices(verts);
                    Matrix4x4 m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    {
                        mesh.GetTriangles(tris, sm);
                        for (int t = 0; t + 2 < tris.Count; t += 3)
                        {
                            Vector3 p0 = m.MultiplyPoint3x4(verts[tris[t]]), p1 = m.MultiplyPoint3x4(verts[tris[t + 1]]), p2 = m.MultiplyPoint3x4(verts[tris[t + 2]]);
                            for (int e = 0; e < 3; e++)
                            {
                                Vector3 u = e == 0 ? p0 : e == 1 ? p1 : p2, v = e == 0 ? p1 : e == 1 ? p2 : p0;
                                float du = (vertical ? u.y : u.z) - station, dv = (vertical ? v.y : v.z) - station;
                                if ((du > 0f) == (dv > 0f) || du == dv) continue;
                                Vector3 x = Vector3.Lerp(u, v, du / (du - dv));
                                int ci = Mathf.RoundToInt(x.x / cell) + N / 2, cj = Mathf.RoundToInt((vertical ? x.z - gl.z : x.y) / cell) + N / 2;
                                if (ci >= 0 && ci < N && cj >= 0 && cj < N) grid[ci, cj] = '#';
                            }
                        }
                    }
                }

                int gi = Mathf.RoundToInt(gl.x / cell) + N / 2, gj = Mathf.RoundToInt((vertical ? 0f : gl.y) / cell) + N / 2;
                if (!vertical) grid[N / 2, N / 2] = '+';
                if (gi >= 0 && gi < N && gj >= 0 && gj < N) grid[gi, gj] = 'G';
                var sb = new System.Text.StringBuilder();
                sb.Append(d.RosterKey).Append(vertical ? " (plane y=" : " (plane z=").Append(station.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(vertical ? "; rows z from goal, +z up" : "; rows y, +y up").Append("; cols x, +x right; 0.5 cm)\n");
                for (int j = N - 1; j >= 0; j--)
                {
                    for (int i = 0; i < N; i++) sb.Append(grid[i, j]);
                    sb.Append('\n');
                }

                maps.Add(sb.ToString());
            }

            Debug.Log("[DD-TEST] wp083.sectionmap\n" + string.Join("\n", maps));
            Assert.Greater(maps.Count, 0);
        }

        private const float SectionReach = 0.04f;

        // the LOD0 section at the station: the LOD0 triangles cut by the plane through the goal across the element's axis (each edge
        // that crosses it gives a point — long low-poly handguards have no vertex near the station), the points within reach of the goal
        // in that plane; centre = their bounds' centre, radius = half the bounds' narrower side
        private static WeaponGripIk.GraspElement Section(Transform frame, Vector3 goalLocal, bool vertical, float reach, List<Vector3> verts, out int n, float theta0 = float.NaN)
        {
            n = 0;
            float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
            float station = vertical ? goalLocal.y : goalLocal.z, ga = goalLocal.x, gb = vertical ? goalLocal.z : goalLocal.y;
            var tris = new List<int>();
            var cut = new List<Vector2>();
            foreach (MeshFilter mf in frame.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                Mesh mesh = Readable(mf.sharedMesh);
                mesh.GetVertices(verts);
                Matrix4x4 m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    mesh.GetTriangles(tris, sm);
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        Vector3 p0 = m.MultiplyPoint3x4(verts[tris[t]]), p1 = m.MultiplyPoint3x4(verts[tris[t + 1]]), p2 = m.MultiplyPoint3x4(verts[tris[t + 2]]);
                        for (int e = 0; e < 3; e++)
                        {
                            Vector3 u = e == 0 ? p0 : e == 1 ? p1 : p2, v = e == 0 ? p1 : e == 1 ? p2 : p0;
                            float du = (vertical ? u.y : u.z) - station, dv = (vertical ? v.y : v.z) - station;
                            if ((du > 0f) == (dv > 0f) || du == dv) continue;
                            Vector3 x = Vector3.Lerp(u, v, du / (du - dv));
                            float a = x.x, b = vertical ? x.z : x.y;
                            if ((a - ga) * (a - ga) + (b - gb) * (b - gb) > reach * reach) continue;
                            n++;
                            cut.Add(new Vector2(a, b));
                            minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                            minB = Mathf.Min(minB, b); maxB = Mathf.Max(maxB, b);
                        }
                    }
                }
            }

            if (n == 0) return default;
            var centre = new Vector2(0.5f * (minA + maxA), 0.5f * (minB + maxB));
            float halfWidth = 0.5f * Mathf.Min(maxA - minA, maxB - minB), radius = halfWidth;
            if (!float.IsNaN(theta0))
            {
                // the contact's radius: the section's outer surface along d(θ0) from the centre (the cut points within ±12° of it) —
                // where WeaponGripIk puts the palm; an oval or tall section's surface there is not its narrower half-width
                float rad = theta0 * Mathf.Deg2Rad;
                var dir = vertical ? new Vector2(-Mathf.Cos(rad), -Mathf.Sin(rad)) : new Vector2(-Mathf.Sin(rad), -Mathf.Cos(rad));
                float best = 0f;
                foreach (Vector2 q in cut)
                {
                    Vector2 v = q - centre;
                    if (v.sqrMagnitude < 1e-8f || Vector2.Angle(v, dir) > 12f) continue;
                    best = Mathf.Max(best, Vector2.Dot(v, dir));
                }

                if (best > 0f) radius = best;
            }

            return new WeaponGripIk.GraspElement(vertical, centre, radius, halfWidth);
        }

        /// <summary>
        /// R2 H1b "support hand grasps" (C-00 06.10; council K/wp-083/KARAR-B2f-SOLEL-06.10.md): every enabled classic weapon, standing
        /// then walking, the frames with the grip IK at full weight. palmToWeaponMaxDeg — the left palm normal (the hand's geometry, on
        /// the thumb's side) against the perpendicular from the palm point to the grasped element's axis (+Z on a handguard, the grip's +Y
        /// on rl_1) through the LOD0 section centre at the palm's station (measured here, independent of the IK's row);
        /// fingertipGapMaxCm — the nearer of the index / middle fingertips to the weapon's LOD0 surface; palmGapMaxCm — the palm point to
        /// that surface. All the worst frame; reports wristClampShare, wristMaxDeg, thetaSwitchCount (per weapon, the largest).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator H1b_SolEl_Kavrama_TumKlasikSilahlar_DurusYuruyus()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Animator a = body.Animator;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var report = new List<string>();
            float palmMax = 0f, tipMax = 0f, palmGapMax = 0f, wristMax = 0f;
            int weapons = 0, frames = 0, clampedAll = 0, switchesMax = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                WeaponGripIk.GraspElement row = WeaponGripIk.ElementOf(d.Id);
                bool vertical = row.Valid ? row.Vertical : d.Id == ClassicWeaponId.Rl1;
                float palm = 0f, tip = 0f, palmGap = 0f, wrist = 0f;
                Vector3 mWantSum = Vector3.zero, mWantMax = Vector3.zero;
                int n = 0, clamped = 0;
                for (int f = 0; f < 180; f++)
                {
                    _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;
                    yield return null;
                    if (f % 3 != 0 || _s.Controller.Classic.IsReloading) continue;
                    WeaponGripIk ik = body.GripIk;
                    Transform frame = ik != null ? ik.WeaponFrame : null;
                    if (frame == null || ik.Weight < 1f || !ik.TryGetSupportGoal(out Vector3 goal)) continue;
                    n++;
                    // the palm in the hand's own frame, read once in the bind pose (WeaponGripIk.TryGetPalmFrame): the thumb, which the
                    // clip moves, does not flip it frame to frame
                    Assert.IsTrue(ik.TryGetPalmFrame(out Vector3 nLocal, out Vector3 pLocal), "el çerçevesi okunmadı");
                    Transform lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 normal = lh.rotation * nLocal, p = lh.position + lh.rotation * pLocal;
                    // the element's axis through its LOD0 section centre at the palm's station, the section seeded at the row's centre
                    // (the goal without a row) — the measure stays the LOD0 section's, not the row's
                    Vector3 gl = frame.InverseTransformPoint(goal), pl = frame.InverseTransformPoint(p);
                    if (row.Valid) { gl.x = row.Centre.x; if (vertical) gl.z = row.Centre.y; else gl.y = row.Centre.y; }
                    if (vertical) gl.y = pl.y; else gl.z = pl.z;
                    WeaponGripIk.GraspElement e = Section(frame, gl, vertical, SectionReach, verts, out int count);
                    Assert.Greater(count, 0, d.RosterKey + ": avucun istasyonunda LOD0 kesiti yok");
                    Vector3 cl = vertical ? new Vector3(e.Centre.x, pl.y, e.Centre.y) : new Vector3(e.Centre.x, e.Centre.y, pl.z);
                    Vector3 foot = frame.TransformPoint(cl);
                    palm = Mathf.Max(palm, Vector3.Angle(normal, foot - p));
                    tip = Mathf.Max(tip, Mathf.Min(SurfaceGap(Fingertip(a, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal), frame, verts, tris),
                                                   SurfaceGap(Fingertip(a, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal), frame, verts, tris)));
                    palmGap = Mathf.Max(palmGap, SurfaceGap(p, frame, verts, tris));
                    wrist = Mathf.Max(wrist, ik.LastWristDegrees);
                    if (ik.WristClamped) clamped++;
                    mWantSum += ik.LastMusclesWanted;
                    mWantMax = Vector3.Max(mWantMax, new Vector3(Mathf.Abs(ik.LastMusclesWanted.x), Mathf.Abs(ik.LastMusclesWanted.y), Mathf.Abs(ik.LastMusclesWanted.z)));
                }

                _s.Girdi.Yon = Vector2.zero;
                if (n > 0) weapons++;
                frames += n;
                palmMax = Mathf.Max(palmMax, palm);
                tipMax = Mathf.Max(tipMax, tip);
                palmGapMax = Mathf.Max(palmGapMax, palmGap);
                wristMax = Mathf.Max(wristMax, wrist);
                clampedAll += clamped;
                WeaponGripIk gik = body.GripIk;
                if (gik != null) switchesMax = Mathf.Max(switchesMax, gik.ThetaSwitchCount);
                report.Add(d.RosterKey + " n " + n + " palmToWeapon " + palm.ToString("F0", CultureInfo.InvariantCulture) + "° tipGap " + Cm(tip) + " palmGap " + Cm(palmGap) +
                           " cm wrist " + wrist.ToString("F0", CultureInfo.InvariantCulture) + "° clamped " + clamped + "/" + n + " thetaSwitches " + (gik != null ? gik.ThetaSwitchCount : -1) +
                           " muscles(twist,downUp,inOut) wanted mean " + (n > 0 ? (mWantSum / n).ToString("F2") : "-") + " |max| " + mWantMax.ToString("F2"));
            }

            Debug.Log("[DD-TEST] wp083.h1b grasp=" + (WeaponGripIk.GraspEnabled ? "on" : "off") + " palmToWeaponMaxDeg=" + palmMax.ToString("F1", CultureInfo.InvariantCulture) +
                      " fingertipGapMaxCm=" + Cm(tipMax) + " :: " + string.Join(" | ", report));
            // the source is in the record before the artefact (S-10 R2 standard): weapons measured, frames at full weight
            Assert.Greater(weapons, 0, "ölçülen silah yok");
            Assert.Greater(frames, 0, "tam ağırlıkta ölçülen kare yok");
            R2Olcum.Yaz("H1b", H1bName, new Dictionary<string, double>
            {
                { "palmToWeaponMaxDeg", System.Math.Round(palmMax, 1) }, { "fingertipGapMaxCm", System.Math.Round(tipMax * 100f, 2) },
                { "palmGapMaxCm", System.Math.Round(palmGapMax * 100f, 2) }, { "weaponsMeasured", weapons }, { "framesMeasured", frames },
                // reports: the share of measured frames the wrist gate clamped, the wrist's largest angle (°), the most wrap-target
                // changes on one weapon's stand + walk (co-sign K1: 0 standing, ≤ 1 a walk cycle)
                { "wristClampShare", System.Math.Round((double)clampedAll / frames, 3) }, { "wristMaxDeg", System.Math.Round(wristMax, 1) },
                { "thetaSwitchCount", switchesMax },
            }, frames);
        }

        /// <summary>
        /// R2 H1c "support arm reach" (C-00 06.10): every enabled classic weapon, standing then walking, the frames with the grip IK at
        /// full weight. elbowMaxDeg — the left elbow's interior angle (180 = straight), the worst frame; reachMarginMinCm — the arm's
        /// length (upper arm + forearm, read from the live skeleton) minus the straight distance shoulder (LeftUpperArm) → the wrist goal
        /// the grip IK aims for (the one that puts the palm on its contact; WeaponGripIk.LastWristGoal), the worst frame; negative = out
        /// of reach.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator H1c_SolKol_Erisim_TumKlasikSilahlar_DurusYuruyus()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Animator a = body.Animator;
            Transform u = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), l = a.GetBoneTransform(HumanBodyBones.LeftLowerArm), h = a.GetBoneTransform(HumanBodyBones.LeftHand);
            Assert.IsTrue(u != null && l != null && h != null, "sol kol kemikleri yok");
            float length = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, h.position);
            var report = new List<string>();
            float elbowMax = 0f, marginMin = float.PositiveInfinity;
            int weapons = 0, frames = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                float elbow = 0f, margin = float.PositiveInfinity;
                int n = 0;
                for (int f = 0; f < 180; f++)
                {
                    _s.Girdi.Yon = f >= 60 && f < 150 ? Vector2.up : Vector2.zero;
                    yield return null;
                    if (_s.Controller.Classic.IsReloading) continue;
                    WeaponGripIk ik = body.GripIk;
                    if (ik == null || ik.Weight < 1f || float.IsNaN(ik.LastError)) continue;
                    n++;
                    elbow = Mathf.Max(elbow, Vector3.Angle(u.position - l.position, h.position - l.position));
                    margin = Mathf.Min(margin, length - Vector3.Distance(u.position, ik.LastWristGoal));
                }

                _s.Girdi.Yon = Vector2.zero;
                if (n > 0) weapons++;
                frames += n;
                elbowMax = Mathf.Max(elbowMax, elbow);
                marginMin = Mathf.Min(marginMin, margin);
                report.Add(d.RosterKey + " n " + n + " elbow " + elbow.ToString("F0", CultureInfo.InvariantCulture) + "° margin " + Cm(margin) + " cm");
            }

            Debug.Log("[DD-TEST] wp083.h1c armLength=" + Cm(length) + "cm elbowMaxDeg=" + elbowMax.ToString("F1", CultureInfo.InvariantCulture) + " reachMarginMinCm=" + Cm(marginMin) +
                      " :: " + string.Join(" | ", report));
            Assert.Greater(weapons, 0, "ölçülen silah yok");
            Assert.Greater(frames, 0, "tam ağırlıkta ölçülen kare yok");
            R2Olcum.Yaz("H1c", H1cName, new Dictionary<string, double>
            {
                { "elbowMaxDeg", System.Math.Round(elbowMax, 1) }, { "reachMarginMinCm", System.Math.Round(marginMin * 100f, 2) },
            }, frames);
        }

        private static string Cm(float m) => (m * 100f).ToString("F1", CultureInfo.InvariantCulture);

        // the hand's local axis that points down in the avatar's T-pose (the humanoid T-pose: palms down) — the palm normal
        private static Vector3 PalmNormalLocal(Animator a, HumanBodyBones hand)
        {
            Transform h = a.GetBoneTransform(hand);
            SkeletonBone[] skeleton = a.avatar.humanDescription.skeleton;
            var chain = new List<Transform>();
            for (Transform t = h; t != null && t != a.transform; t = t.parent) chain.Add(t);
            Quaternion world = Quaternion.identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Quaternion local = chain[i].localRotation;
                for (int k = 0; k < skeleton.Length; k++) if (skeleton[k].name == chain[i].name) { local = skeleton[k].rotation; break; }
                world *= local;
            }

            return (Quaternion.Inverse(world) * Vector3.down).normalized;
        }

        // the palm normal from the hand's own geometry: the plane through the wrist and the index / little knuckles, turned to the
        // side the thumb's base lies on (palmar). byCurl: the index fingertip lies on the same side of the knuckle line (a relaxed or
        // gripping finger curls toward the palm) — the two anchors are reported, not assumed
        private static Vector3 GeometricPalmNormal(Animator a, bool left, out bool byThumb, out bool byCurl)
        {
            Transform h = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform ip = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform lp = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            Transform tp = a.GetBoneTransform(left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            Transform id = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            Vector3 n = Vector3.Cross(ip.position - h.position, lp.position - h.position).normalized;
            float thumb = Vector3.Dot(tp.position - h.position, n);
            if (thumb < 0f) n = -n;
            byThumb = Mathf.Abs(thumb) > 0.002f;
            Vector3 knuckles = 0.5f * (ip.position + lp.position);
            byCurl = Vector3.Dot(id.position - knuckles, n) > 0f;
            return n;
        }

        private static Vector3 PalmPoint(Animator a, HumanBodyBones hand, HumanBodyBones middleProximal, Vector3 palmLocal)
        {
            Transform h = a.GetBoneTransform(hand), m = a.GetBoneTransform(middleProximal);
            return Vector3.Lerp(h.position, m.position, 0.55f) + h.rotation * palmLocal * 0.012f;
        }

        private static Vector3 Fingertip(Animator a, HumanBodyBones intermediate, HumanBodyBones distal)
        {
            Transform i = a.GetBoneTransform(intermediate), d = a.GetBoneTransform(distal);
            return d.position + (d.position - i.position);
        }

        // the AP-021 weapon meshes are imported without Read/Write: a readable copy through a bone-less SkinnedMeshRenderer's
        // BakeMesh (no importer change — AnimationDiagnosticTests' pattern), once per mesh for the test
        private static readonly Dictionary<Mesh, Mesh> s_readable = new Dictionary<Mesh, Mesh>();

        private static Mesh Readable(Mesh m)
        {
            if (m.isReadable) return m;
            if (s_readable.TryGetValue(m, out Mesh copy)) return copy;
            var go = new GameObject("WP083_Bake");
            try
            {
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = m;
                copy = new Mesh();
                smr.BakeMesh(copy);
            }
            finally
            {
                Object.Destroy(go);
            }

            s_readable[m] = copy;
            return copy;
        }

        // the distance from p to the nearest point of the LOD0 triangles under root (active mesh filters; m)
        private static float SurfaceGap(Vector3 p, Transform root, List<Vector3> verts, List<int> tris)
        {
            float best = float.PositiveInfinity;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                Mesh m = Readable(mf.sharedMesh);
                Assert.Greater(m.vertexCount, 0, mf.name + ": okunabilir kopya boş (BakeMesh)");
                m.GetVertices(verts);
                Matrix4x4 w = mf.transform.localToWorldMatrix;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    m.GetTriangles(tris, s);
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        Vector3 c = ClosestOnTriangle(p, w.MultiplyPoint3x4(verts[tris[t]]), w.MultiplyPoint3x4(verts[tris[t + 1]]), w.MultiplyPoint3x4(verts[tris[t + 2]]));
                        best = Mathf.Min(best, (c - p).sqrMagnitude);
                    }
                }
            }

            return Mathf.Sqrt(best);
        }

        // Ericson, Real-Time Collision Detection §5.1.5
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>
        /// WP-083 B2e diagnostics (C-00 05.10, rl_1 on the shoulder): the shouldered row's candidates — the tube's axis above / beside
        /// the right shoulder joint (WeaponHoldIk.ShoulderRest) and the grip ahead of it (ShoulderGripAhead) — each scored on the axis's
        /// clearance from the head and from the shoulder joint, the eye's height against the axis at the head, the stock's and the
        /// tube's penetration into the body, both hands' errors; a front-right and a front frame each (DD_B2D_COMPARE_DIR).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator Tani_Rl1_OmuzSatiri_TupBasOmuz()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_B2D_COMPARE_DIR");
            Assert.IsFalse(string.IsNullOrEmpty(dir), "DD_B2D_COMPARE_DIR yok");
            // the orthographic side camera trips PlanarReflection's frustum log (an evidence frame, not the game's camera)
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var rows = new List<string>();
            _s.Controller.EquipClassic(ClassicWeaponId.Rl1, _s.Config);
            yield return SpecialWeaponSahne.Kareler(60);
            foreach (float up in new[] { 0.05f, 0.06f, 0.07f, 0.08f })
            {
                foreach (float side in new[] { -0.015f, -0.005f, 0.005f })
                {
                    WeaponHoldIk.ShoulderRest = new Vector3(side, up, 0f);
                    WeaponHoldIk.ShoulderGripAhead = 0.15f;
                    yield return SpecialWeaponSahne.Kareler(20);
                    string tag = "rl1_u" + Mathf.RoundToInt(up * 1000f) + "mm_s" + Mathf.RoundToInt(side * 1000f) + "mm_a15";
                    rows.Add(tag + " " + TubeFit(body, mount) + SupportWhy(body));
                    OrthoSide(dir, tag + "_ortho", body);
                }
            }

            WeaponHoldIk.ResetStatics();
            Debug.Log("[DD-TEST] wp083.rl1diag " + string.Join(" | ", rows));
            Assert.Greater(rows.Count, 0);
        }

        /// <summary>
        /// The shouldered tube against the body (rl_1): the tube's radius at the shoulder (the weapon mesh around its stock → muzzle
        /// axis), the body surface's gap to the tube — head vertices (≤ 16 cm from the head bone) and right-shoulder vertices (≤ 14 cm
        /// from the joint, above it) — negative inside, the sight (attach_top) against the eye height, both hands' errors.
        /// </summary>
        private static string TubeFit(PlayerSkinnedVisual body, Transform mount)
        {
            Transform st = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachStock), mz = WeaponSocketContract.Find(mount, WeaponSocketContract.Muzzle);
            if (st == null || mz == null) return "[no stock/muzzle]";
            Vector3 a = st.position, d = (mz.position - st.position).normalized;
            Transform head = body.Animator.GetBoneTransform(HumanBodyBones.Head), sh = body.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform eyeT = body.Animator.GetBoneTransform(HumanBodyBones.RightEye);
            float tFoot = Vector3.Dot(sh.position - a, d);
            // the tube's radius at the shoulder: weapon mesh vertices within ±6 cm of the shoulder's foot on the axis
            float radius = 0f;
            bool readable = false;
            foreach (MeshFilter mf in mount.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                readable = true;
                foreach (Vector3 lv in mf.sharedMesh.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(lv);
                    float t = Vector3.Dot(w - a, d);
                    if (Mathf.Abs(t - tFoot) <= 0.06f) radius = Mathf.Max(radius, Vector3.Cross(w - a, d).magnitude);
                }
            }

            if (!readable) radius = 0.05f;
            SkinnedMeshRenderer best = null;
            foreach (SkinnedMeshRenderer r in body.Renderers) if (r != null && r.sharedMesh != null && (best == null || r.sharedMesh.vertexCount > best.sharedMesh.vertexCount)) best = r;
            float headGap = float.MaxValue, shoulderGap = float.MaxValue;
            if (best != null)
            {
                var baked = new Mesh();
                best.BakeMesh(baked);
                foreach (Vector3 lv in baked.vertices)
                {
                    Vector3 w = best.transform.TransformPoint(lv);
                    float t = Vector3.Dot(w - a, d);
                    if (t < 0f || t > Vector3.Distance(a, mz.position)) continue;
                    float gap = Vector3.Cross(w - a, d).magnitude - radius;
                    if (Vector3.Distance(w, head.position) <= 0.16f) headGap = Mathf.Min(headGap, gap);
                    else if (Vector3.Distance(w, sh.position) <= 0.14f && w.y >= sh.position.y) shoulderGap = Mathf.Min(shoulderGap, gap);
                }

                UnityEngine.Object.Destroy(baked);
            }

            Vector3 eye = eyeT != null ? eyeT.position : head.position + Vector3.up * 0.08f;
            Transform top = WeaponSocketContract.Find(mount, WeaponSocketContract.AttachTop);
            float sightDy = top != null ? top.position.y - eye.y : float.NaN;
            float left = body.GripIk != null && !float.IsNaN(body.GripIk.LastError) ? body.GripIk.LastError : float.NaN;
            float right = body.HoldIk != null && !float.IsNaN(body.HoldIk.LastError) ? body.HoldIk.LastError : float.NaN;
            System.Func<float, string> cm = v => (v * 100f).ToString("F1", CultureInfo.InvariantCulture);
            return "radius " + cm(radius) + (readable ? "" : "(est)") + "cm headGap " + cm(headGap) + "cm shoulderGap " + cm(shoulderGap) + "cm sightDy " + cm(sightDy) +
                   "cm axisAboveShoulder " + cm(Vector3.Cross(sh.position - a, d).magnitude) + "cm left " + cm(left) + "cm right " + cm(right) + "cm";
        }

        // a pure side frame from the character's right, orthographic, framed on the shoulder (the tube on the shoulder, beside the head)
        private static string OrthoSide(string dir, string name, PlayerSkinnedVisual body)
        {
            var go = new GameObject("WP083_OrthoSide");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 0.45f;
                cam.nearClipPlane = 0.05f;
                Transform b = body.transform, sh = body.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Vector3 c = sh.position + Vector3.up * 0.08f + b.forward * 0.25f;
                go.transform.position = c + b.right * 2f;
                go.transform.rotation = Quaternion.LookRotation(-b.right, Vector3.up);
                return Shot(dir, name, cam);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        private static float Penetration(PlayerSkinnedVisual skinned, Vector3 point)
        {
            SkinnedMeshRenderer best = null;
            foreach (SkinnedMeshRenderer r in skinned.Renderers) if (r != null && r.sharedMesh != null && (best == null || r.sharedMesh.vertexCount > best.sharedMesh.vertexCount)) best = r;
            if (best == null) return float.NaN;
            var baked = new Mesh();
            best.BakeMesh(baked);
            Vector3[] v = baked.vertices, n = baked.normals;
            int k = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                float sq = (best.transform.TransformPoint(v[i]) - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; k = i; }
            }

            float pen = k >= 0 ? Mathf.Max(0f, -Vector3.Dot(point - best.transform.TransformPoint(v[k]), best.transform.TransformDirection(n[k]).normalized)) : float.NaN;
            Object.Destroy(baked);
            return pen;
        }

        /// <summary>
        /// WP-083 B2e (C-00 05.10: H3 contact on the path the player sees). Every magazine weapon reloads once. A MagazineContactProbe
        /// scores the left hand's socket_mag_l against the magazine seat on the Detach / Insert beats: every beat must have contact
        /// within 2 frames (the H3 contactDeviationFramesMax limit; S-02's H3 test carries the gate value). Third person on the default
        /// path. <see cref="H3Temas_FPKollar_SarjorluSilahlar_ElVurustaYuvada"/> covers first person with the arms on.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator H3Temas_TP_SarjorluSilahlar_ElVurustaYuvada()
        {
            yield return Temas(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);
        }

        /// <summary>
        /// WP-083 B2e: the same in first person with the FP arms on (PlayerSkinnedVisual.FirstPersonV3Arms; off on today's default
        /// path, where first person shows no hand; the arms stay off for the long guns). The seat is the held model's part_mag.
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator H3Temas_FPKollar_SarjorluSilahlar_ElVurustaYuvada()
        {
            yield return Temas(DD.Prototype.CameraSystem.CameraMode.FirstPerson);
        }

        private IEnumerator Temas(DD.Prototype.CameraSystem.CameraMode mode)
        {
            Time.captureDeltaTime = 1f / 60f;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            var ids = new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.Ar2, ClassicWeaponId.AutoSmg, ClassicWeaponId.Smg2, ClassicWeaponId.Lmg1, ClassicWeaponId.Lmg2, ClassicWeaponId.Sr1 };
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
            yield return SpecialWeaponSahne.Kareler(10);
            bool fp = mode == DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            var probe = new MagazineContactProbe();
            int bound = 0;
            foreach (ClassicWeaponId id in ids)
            {
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(40);
                string key = mode + " " + (_s.Controller.Classic.Definition != null ? _s.Controller.Classic.Definition.weaponId : id.ToString());
                for (int i = 0; i < 3; i++)
                {
                    _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                    yield return SpecialWeaponSahne.Kareler(8);
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    yield return SpecialWeaponSahne.Kareler(8);
                }

                _s.Girdi.DoldurBasti = true;
                yield return null;
                _s.Girdi.DoldurBasti = false;
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 2f, key + ": reload başlamadı");
                FirstPersonArms arms = body.FirstPersonArms;
                Transform socket = fp ? (arms != null && arms.Visible ? arms.MagazineSocket : null) : body.MagazineSocket;
                Transform part = _s.Controller.MagazineVisual.Part;   // the same model instance in both views
                if (socket != null && part != null) bound++;
                else Debug.Log("[DD-TEST] wp083.h3contact.unbound " + key + " arms=" + (arms != null) + " visible=" + (arms != null && arms.Visible) + " frame=" + (arms != null && arms.WeaponFrame != null) +
                               " socket=" + (socket != null) + " part=" + (part != null) + " frameKids=" + (arms != null && arms.WeaponFrame != null ? Kids(arms.WeaponFrame) : "-") +
                               " tpPart=" + (_s.Controller.MagazineVisual.Part != null ? _s.Controller.MagazineVisual.Part.parent.name : "null"));
                probe.Begin(key, part, socket, m);
                float start = Time.time;
                while (_s.Controller.Classic.IsReloading && Time.time - start < 12f)
                {
                    yield return null;
                    probe.Tick();
                }

                yield return null;
                probe.End();
                yield return SpecialWeaponSahne.Kareler(10);
            }

            Debug.Log("[DD-TEST] wp083.h3contact." + mode + " max=" + probe.MaxDeviationFrames + " f, beats " + probe.BeatsMeasured + ", bound " + bound + "/" + ids.Length + " :: " + probe.Report);
            Assert.Greater(bound, 0, mode + ": el soketi / şarjör parçası bağlanamadı (FP kolları açık mı?)");
            Assert.Greater(probe.BeatsMeasured, 0, mode + ": vuruş ölçülmedi");
            Assert.LessOrEqual(probe.MaxDeviationFrames, 2, mode + ": el vuruşta yuvada değil\n" + probe.Report);
        }

        private static string Kids(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < t.childCount && i < 8; i++) sb.Append(t.GetChild(i).name).Append(t.GetChild(i).childCount > 0 ? "/" + t.GetChild(i).GetChild(0).name : "").Append(',');
            return sb.ToString();
        }

        /// <summary>
        /// WP-083 B2d before / after frames for the user (C-00 05.10): ar_1, sg_2 (long) and rl_1, and (AD 06.10 MUST-1, the riskiest
        /// holds: C's elbow at 173°, the LMGs) C, lmg_1 and lmg_2. Each is shot with the weapon-first
        /// hold off (before) and on (after), standing and walking, from the game's third-person over-the-shoulder camera, plus one frame
        /// from the top-down game camera for ar_1, and a fixed three-quarter side view (the over-the-shoulder camera sees the hold
        /// from behind). The player is teleported to the same spot for each state and the frames run at a
        /// fixed 1/60 s, so the pose and the light match. PNG files go to DD_B2D_COMPARE_DIR (required: the run names the folder).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("FPTP")]   // R3 (C-00 07.10): the third-person over-the-shoulder before / after set — out of scope (user 06.10, top-down only)
        [Timeout(600000)]
        public IEnumerator Kare_TutusOnceSonra_TPOmuz_Ustten()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_B2D_COMPARE_DIR");
            Assert.IsFalse(string.IsNullOrEmpty(dir), "DD_B2D_COMPARE_DIR yok (kare klasörü; tanı testi — TANI-TESTI-POLITIKASI: Ignore yok)");
            System.IO.Directory.CreateDirectory(dir);
            Time.captureDeltaTime = 1f / 60f;
            Vector3 spot = _s.Rig.transform.position;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            var written = new List<string>();
            var weapons = new[]
            {
                (ClassicWeaponId.AssaultRifle, "ar_1"), (ClassicWeaponId.Sg2, "sg_2"), (ClassicWeaponId.Rl1, "rl_1"),
                (ClassicWeaponId.CombatShotgun, "C"), (ClassicWeaponId.Lmg1, "lmg_1"), (ClassicWeaponId.Lmg2, "lmg_2"),
            };
            foreach ((ClassicWeaponId id, string key) in weapons)
            {
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(40);
                foreach (bool hold in new[] { false, true })
                {
                    string tag = hold ? "after" : "before";
                    WeaponHoldIk.Enabled = hold;
                    foreach (DD.Prototype.CameraSystem.CameraMode mode in key == "ar_1"
                                 ? new[] { DD.Prototype.CameraSystem.CameraMode.ThirdPerson, DD.Prototype.CameraSystem.CameraMode.TopDown }
                                 : new[] { DD.Prototype.CameraSystem.CameraMode.ThirdPerson })
                    {
                        DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
                        _s.Rig.Motor.Teleport(spot);
                        _s.Girdi.Yon = Vector2.zero;
                        yield return SpecialWeaponSahne.Kareler(90);   // the hold's fade, the camera's follow settles
                        string view = mode == DD.Prototype.CameraSystem.CameraMode.TopDown ? "topdown" : "tp";
                        written.Add(Shot(dir, key + "_" + view + "_stand_" + tag));
                        if (mode == DD.Prototype.CameraSystem.CameraMode.TopDown) continue;
                        written.Add(SideShot(dir, key + "_side_stand_" + tag, body));
                        _s.Girdi.Yon = Vector2.up;
                        yield return SpecialWeaponSahne.Kareler(40);
                        written.Add(Shot(dir, key + "_" + view + "_walk_" + tag));
                        written.Add(SideShot(dir, key + "_side_walk_" + tag, body));
                        _s.Girdi.Yon = Vector2.zero;
                    }
                }
            }

            WeaponHoldIk.Enabled = true;
            Debug.Log("[DD-TEST] wp083.b2dcompare " + written.Count + " frames → " + dir + " :: " + string.Join(", ", written));
            Assert.AreEqual(50, written.Count, "kare sayısı");   // ar_1 2 × 5 (+ top-down), five weapons 2 × 4
        }

        /// <summary>
        /// WP-083 B2f / AP-024C2 AD MUST-1 (C-00 06.10): the weapon ↔ body frame set in game — every enabled classic weapon from the
        /// top-down game camera, aiming, at the recoil's peak (one shot, three frames on) and mid-reload; then one Special (the
        /// production RNG picks it; its id is logged). R3 (C-00 07.10): the top-down side only — the third-person frames are out of
        /// scope (user 06.10). The verdict is the frames' (stock under the shoulder plate's edge,
        /// 0 px out, 0 px mask seam); PNGs to DD_B2D_COMPARE_DIR (the run names it; scratch).
        /// </summary>
        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(1800000)]
        public IEnumerator Kare_M1_SilahGovde_TD_NisanTepmeReload() => KareM1(DD.Prototype.CameraSystem.CameraMode.TopDown);

        /// <summary>The M1 frame set from one game camera (R3, C-00 07.10: the top-down test above; the third-person twin, the same
        /// lines, in <see cref="R2HeroOlcumFptpTests"/> under FPTP).</summary>
        internal IEnumerator KareM1(DD.Prototype.CameraSystem.CameraMode camera)
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_B2D_COMPARE_DIR");
            Assert.IsFalse(string.IsNullOrEmpty(dir), "DD_B2D_COMPARE_DIR yok (kare klasörü; tanı testi)");
            System.IO.Directory.CreateDirectory(dir);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Vector3 spot = _s.Rig.transform.position;
            var written = new List<string>();
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                foreach (DD.Prototype.CameraSystem.CameraMode mode in new[] { camera })
                {
                    string cam = mode == DD.Prototype.CameraSystem.CameraMode.TopDown ? "td" : "tp";
                    DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
                    _s.Controller.EquipClassic(d.Id, _s.Config);
                    _s.Rig.Motor.Teleport(spot);
                    _s.Girdi.Yon = Vector2.zero;
                    yield return SpecialWeaponSahne.Kareler(70);
                    written.Add(Shot(dir, d.RosterKey + "_m1_" + cam + "_aim"));
                    _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                    yield return null;
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    yield return SpecialWeaponSahne.Kareler(3);
                    written.Add(Shot(dir, d.RosterKey + "_m1_" + cam + "_recoil"));
                    yield return SpecialWeaponSahne.Kareler(30);
                    if (_s.Controller.Classic != null && _s.Controller.Classic.TryBeginReload())
                    {
                        yield return SpecialWeaponSahne.Kareler(30);
                        written.Add(Shot(dir, d.RosterKey + "_m1_" + cam + "_reload"));
                        // AD B2f MUST-2: what drives the support hand on the reload frame — at grip weight 0 WeaponGripIk returns
                        // before any solve (the B2f grasp never runs), at hold weight 0 WeaponHoldIk leaves the arm to the clip
                        var bodyM1 = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
                        Debug.Log("[DD-TEST] wp083.m1.reload " + d.RosterKey + " " + cam + " gripWeight=" +
                                  (bodyM1 != null && bodyM1.GripIk != null ? bodyM1.GripIk.Weight.ToString("F3", CultureInfo.InvariantCulture) : "-") +
                                  " holdWeight=" + (bodyM1 != null && bodyM1.HoldIk != null ? bodyM1.HoldIk.Weight.ToString("F3", CultureInfo.InvariantCulture) : "-") +
                                  " reloading=" + _s.Controller.Classic.IsReloading);
                        yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 10f, d.RosterKey + " reload bitmedi");
                    }
                    else written.Add(d.RosterKey + "_m1_" + cam + "_reload(NO RELOAD)");
                }
            }

            // one Special: the production RNG picks it (sw_arc / sw_gel / …); the hold is the classic weapons' only, the Special keeps main's path
            DD.Prototype.CameraSystem.CameraModeService.Request(camera, instant: true);
            yield return _s.SpawnEt(0.5f);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            string special = "none";
            if (pickup != null)
            {
                _s.PickupaGit(pickup);
                yield return SpecialWeaponSahne.Kareler(90);
                special = WeaponEvents.Special.WeaponId.ToString();
                foreach (DD.Prototype.CameraSystem.CameraMode mode in new[] { camera })
                {
                    DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
                    yield return SpecialWeaponSahne.Kareler(40);
                    written.Add(Shot(dir, "special_" + special + "_m1_" + (mode == DD.Prototype.CameraSystem.CameraMode.TopDown ? "td" : "tp") + "_aim"));
                }
            }

            Debug.Log("[DD-TEST] wp083.m1 special=" + special + " " + written.Count + " frames → " + dir + " :: " + string.Join(", ", written));
            Assert.Greater(written.Count, 0);
        }

        /// <summary>A fixed three-quarter view from the body's front right at chest height — the same framing before and after.</summary>
        private static string SideShot(string dir, string name, PlayerSkinnedVisual body)
        {
            var go = new GameObject("WP083_SideCam");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.05f;
                Transform b = body.transform;
                Vector3 chest = b.position + Vector3.up * 1.25f;
                go.transform.position = chest + b.right * 1.7f + b.forward * 1.5f + Vector3.up * 0.15f;
                go.transform.LookAt(chest);
                return Shot(dir, name, cam);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        private static string Shot(string dir, string name, Camera cam = null)
        {
            if (cam == null) cam = Camera.main;
            Assert.IsNotNull(cam, "oyun kamerası yok");
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture, prev = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            string file = System.IO.Path.Combine(dir, name + ".png");
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prev;
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }

            return name;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }
    }
}
