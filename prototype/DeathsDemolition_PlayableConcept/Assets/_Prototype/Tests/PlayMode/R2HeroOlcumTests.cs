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
    [Category("WP083")]
    public sealed class R2HeroOlcumTests
    {
        public const string H1Name = "DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus";
        private SpecialWeaponSahne _s;
        private bool _fpArmsBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // the owner's A/B switch (r2-measure-contract measuredBy.env): DD_HOLD_IK=0 measures with the WP-083 B2c/B2d hold off
            // (it is on by default since C-00 05.10)
            WeaponHoldIk.Enabled = System.Environment.GetEnvironmentVariable("DD_HOLD_IK") != "0";
            // the FP arms are off on the default path (PlayerSkinnedVisual.FirstPersonV3Arms); the FP contact test turns them on
            _fpArmsBefore = PlayerSkinnedVisual.FirstPersonV3Arms;
            if (TestContext.CurrentContext.Test.Name.Contains("FPKollar")) PlayerSkinnedVisual.FirstPersonV3Arms = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            WeaponHoldIk.ResetStatics();
            PlayerSkinnedVisual.FirstPersonV3Arms = _fpArmsBefore;
        }

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
            R2Olcum.Yaz("H1", H1Name, new Dictionary<string, double> { { "maxCm", maxCm }, { "elbowMaxDeg", System.Math.Round(elbowMax, 1) } }, samples);
            Assert.LessOrEqual(maxCm, 1.0f, "R2 H1: el ↔ tutamak > 1 cm\n" + string.Join("\n", report));
        }

        /// <summary>
        /// WP-083 B2d (C-00 05.10, v4 frames: the upper arm turned out and back behind the body): the hold's solves swing the arms,
        /// they do not roll them. For every enabled classic weapon, standing then walking, the right (WeaponHoldIk) and the left
        /// (WeaponGripIk) upper arm's roll about its own axis against the clip's pose stays within 15° at full weight.
        /// </summary>
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
        /// C (C-00 06.10): the pump shotgun's kick (WeaponBody 2.5 cm / 2.3°) on a hold at the arm's reach (H1 elbow 173°, its +6 cm
        /// row for KundakOmuz), left hand 2.68 cm (b2h, b2i, b2l-r2; 0.52 cm before the row, b2g).
        /// </summary>
        static readonly Dictionary<string, string> AtisBilinenKirmizi = new Dictionary<string, string>
        {
            { "ar_2", "known red: S-02 WeaponVisualRig accumulated kick, 12.63 cm (b2l-r2), held-kick package WP-087, row off in step D (C-00 05.10)" },
            { "C", "known red: S-02 WeaponVisualRig kick on a hold at the arm's reach, 2.68 cm (b2l-r2), held-kick package WP-087, row off in step D (C-00 06.10)" },
        };

        /// <summary>
        /// WP-083 B2d diagnostics: the long weapons' stock penetration (attach_stock vs the AP-006 body, nearest baked vertex and its
        /// normal — WeaponModelSliceTests' measure, ≤ 2 cm) against the support hand's error (H1, ≤ 1 cm) for candidate stock clearances.
        /// </summary>
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
        /// WP-083 B2e diagnostics (C-00 05.10, rl_1 on the shoulder): the shouldered row's candidates — the tube's axis above / beside
        /// the right shoulder joint (WeaponHoldIk.ShoulderRest) and the grip ahead of it (ShoulderGripAhead) — each scored on the axis's
        /// clearance from the head and from the shoulder joint, the eye's height against the axis at the head, the stock's and the
        /// tube's penetration into the body, both hands' errors; a front-right and a front frame each (DD_B2D_COMPARE_DIR).
        /// </summary>
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
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
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
