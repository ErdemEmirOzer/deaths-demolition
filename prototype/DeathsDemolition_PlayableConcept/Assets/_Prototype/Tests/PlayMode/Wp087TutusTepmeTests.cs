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
    /// WP-087 (C-00 06.10; councils K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md + K/wp-083/KARAR-HELDKICK-API-06.10.KONSOLIDE.md,
    /// order A → B → C → D; test review K/wp-087/gte-test-inceleme.md) · the held weapon's kick carried by the weapon-first hold's
    /// arms. The hold off keeps the old path (the body's spring). C: top-down (*_Ustten; user 06.10) and third person (FPTP, kept as it was),
    /// every classic — the autos / bursts ten shots, the singles
    /// min(10, magazine) (rl_1 one round, C-00 06.10), then 30 recovery frames; on every frame at full hold + support weight both hands
    /// ≤ 1 cm (the left from its bone, after every LateUpdate); the hold carries THIS rig (KickSource) and applies exactly the channel's
    /// value (conservation); the visible socket moves back by what was applied and never forward / muzzle-down (observed, not the
    /// channel's own sign). The fade (an empty magazine's auto reload, a reload pressed mid-burst): the visible kick changes smoothly
    /// (|Δ²k| ≤ 1 cm away from a shot). A test frame resumes after Update and before LateUpdate: a shot fired in this Update is in
    /// ShotSerial at once but reaches the hold / rig pose only in this frame's LateUpdate, which the NEXT read sees. The feel (C-00 06.10 (b)): each classic's first shot moves the muzzle back and up
    /// ≥ 90 % of the old path's (the SMG / shotgun rows through their arms back gain). Reported: arms back / up, worst-left shortfall
    /// and residual.
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087TutusTepmeTests
    {
        private SpecialWeaponSahne _s;
        private ClassicWeaponId _oncekiSecim;

        [SetUp]
        public void Hazirla() => _oncekiSecim = WeaponRegistry.SelectedWeapon;   // EquipClassic writes it process-wide (main-ci 285b7fe8)

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            DD.Prototype.CameraSystem.CameraModeService.ResetForTests();
            WeaponHoldIk.ResetStatics();
            WeaponRegistry.SelectedWeapon = _oncekiSecim;
        }

        private static string Cm(float m) => (m * 100f).ToString("0.00", CultureInfo.InvariantCulture);
        private static string Deg(float d) => d.ToString("0.0", CultureInfo.InvariantCulture);

        private IEnumerator Kur(bool holdOn, DD.Prototype.CameraSystem.CameraMode mode)
        {
            WeaponHoldIk.Enabled = holdOn;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            DD.Prototype.CameraSystem.CameraModeService.Request(mode, instant: true);
            // top-down aims at the cursor's ground point: held north of the player, not on it (at the screen centre the aim is degenerate
            // and the facing — the hold's frame — would wander); third person keeps its original setup
            if (mode == DD.Prototype.CameraSystem.CameraMode.TopDown) _s.Girdi.Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.85f);
            Time.captureDeltaTime = 1f / 60f;
        }

        // ---------------------------------------------------------------------------------------------------------------- C

        private sealed class Olcum
        {
            public int Frames, LeftFrames, ShotFrames, BindingMiss, ConservationMiss;
            public float Right, Left, LeftIk, Back, Up, Applied, AppliedDeg, MinAppliedDeg = float.MaxValue, Shortfall, Residual;
            public float ObservedBackAtMax, Forward, ConservationWorst, ElbowL, ElbowR;
            public string Satir(string key, int shots) =>
                key + " shots " + shots + " frames " + Frames + "/" + LeftFrames + " shotFrames " + ShotFrames + " | right " + Cm(Right) + " cm, left wrist " +
                Cm(Left) + " cm (palm H1 " + Cm(LeftIk) + ") | applied " + Cm(Applied) + " cm / " + Deg(AppliedDeg) + "° (min " +
                Deg(MinAppliedDeg == float.MaxValue ? 0f : MinAppliedDeg) + "°), observed back at max " + Cm(ObservedBackAtMax) + " cm, forward " +
                Cm(Forward) + " cm | conservation worst " + (ConservationWorst * 1000f).ToString("0.000", CultureInfo.InvariantCulture) +
                " mm | elbow L " + Deg(ElbowL) + "° R " + Deg(ElbowR) + "° | worst-left shortfall " + Cm(Shortfall) + " residual " + Cm(Residual);
        }

        private static float Elbow(Animator a, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
        {
            Transform u = a.GetBoneTransform(upper), l = a.GetBoneTransform(lower), h = a.GetBoneTransform(hand);
            return Vector3.Angle(u.position - l.position, h.position - l.position);
        }

        // the visible socket in the chest's position and the player's yaw frame (z forward): the arms' kick moves it back
        private static float SocketForward(PlayerSkinnedVisual body, Transform facing)
        {
            Transform chest = body.Animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = body.Animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 f = Vector3.ProjectOnPlane(facing.forward, Vector3.up).normalized;
            return Vector3.Dot(body.WeaponSocket.position - chest.position, f);
        }

        // One frame after every LateUpdate, at full hold + support weight.
        private static void Olc(PlayerSkinnedVisual body, WeaponVisualRig rig, WeaponMountAlignment alignment, Transform facing, float baseline,
            bool shotThisFrame, Olcum o)
        {
            WeaponHoldIk hold = body.HoldIk;
            if (hold == null || !hold.Active || hold.Weight < 1f || rig == null || rig.Body == null) return;
            o.Frames++;
            if (shotThisFrame) o.ShotFrames++;
            if (!ReferenceEquals(hold.KickSource, rig)) o.BindingMiss++;
            Vector3 grip = rig.Body.TransformPoint(alignment.GripOffset);   // the model sits on the body at identity (SetModel)
            o.Right = Mathf.Max(o.Right, Vector3.Distance(body.WeaponSocket.position, grip));
            float applied = -hold.LastKickOffset.z;
            o.AppliedDeg = Mathf.Max(o.AppliedDeg, hold.LastKickDegrees);
            o.MinAppliedDeg = Mathf.Min(o.MinAppliedDeg, hold.LastKickDegrees);
            o.Back = Mathf.Max(o.Back, rig.ArmsKickBack); o.Up = Mathf.Max(o.Up, rig.ArmsKickDegrees);
            // conservation: the channel Steps before the hold reads it (LateUpdate 0 → 890), so on a frame without a new shot the value the
            // hold applied is the channel's own (the shouldered row turns only: its offset is 0 by rule)
            if (!shotThisFrame && hold.LastKickOffset.z != 0f)
            {
                float miss = Mathf.Abs(hold.LastKickOffset.z + rig.ArmsKickBack);
                o.ConservationWorst = Mathf.Max(o.ConservationWorst, miss);
                if (miss > 1e-4f) o.ConservationMiss++;
            }

            float observedBack = baseline - SocketForward(body, facing);
            o.Forward = Mathf.Max(o.Forward, -observedBack);
            if (applied > o.Applied) { o.Applied = applied; o.ObservedBackAtMax = observedBack; }
            o.ElbowL = Mathf.Max(o.ElbowL, Elbow(body.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand));
            o.ElbowR = Mathf.Max(o.ElbowR, Elbow(body.Animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
            WeaponGripIk ik = body.GripIk;
            if (ik == null || ik.Weight < 1f || !ik.TryGetSupportGoal(out _)) return;
            if (ik.WeaponFrame == null || !(ik.WeaponFrame == rig.transform || ik.WeaponFrame.IsChildOf(rig.transform))) o.BindingMiss++;
            o.LeftFrames++;
            Transform u = body.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), l = body.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                h = body.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            // WP-083 B2f (main 18320dab): with the contact grasp the WRIST goes to LastWristGoal (the palm's contact minus the hand's turn ·
            // the palm's offset) and LastError is the palm ↔ the support point (H1); without the grasp the wrist goal is the support point
            Vector3 goal = ik.LastWristGoal;
            float left = Vector3.Distance(h.position, goal);
            if (!float.IsNaN(ik.LastError)) o.LeftIk = Mathf.Max(o.LeftIk, ik.LastError);
            if (left > o.Left)
            {
                o.Left = left;
                float reach = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, h.position);
                o.Shortfall = Vector3.Distance(u.position, goal) - reach;
                o.Residual = left - Mathf.Max(0f, o.Shortfall);
            }
        }

        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYon_Ustten is the top-down run;
        // TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYon keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(1200000)]
        public IEnumerator TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYon_Ustten() => TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYonCore(DD.Prototype.CameraSystem.CameraMode.TopDown);

        [UnityTest]
        [Timeout(1200000)]
        [Category("FPTP")]
        public IEnumerator TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYon() => TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYonCore(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);

        private IEnumerator TutusAcik_TumKlasikler_IkiElTutamakta_TepmeKollarda_TekYonCore(DD.Prototype.CameraSystem.CameraMode mode)
        {
            yield return Kur(true, mode);
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var alignment = mount.GetComponent<WeaponMountAlignment>();
            Assert.IsNotNull(alignment, "WeaponMountAlignment yok");
            Transform facing = _s.Rig.transform;
            var rows = new List<string>();
            var wrong = new List<string>();
            var bos = new List<string>();
            var known = new List<string>();
            float worstLeft = 0f;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                _s.Controller.EquipClassic(d.Id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                Assert.IsTrue(_s.Controller.ClassicRigHasAuthoredModel, d.RosterKey + ": el modeli yok (GripOffset ↔ Body kimliği varsayımı)");
                WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
                if (body.HoldIk == null) { bos.Add(d.RosterKey + ": tutuş IK yok"); continue; }
                WeaponDefinition def = _s.Controller.Classic.Definition;
                Assert.IsNotNull(def, d.RosterKey + ": silah tanımı yok");
                int magazine = _s.Controller.Classic.MagazineSize;
                int minShots = def.fire.fireMode == WeaponFireMode.Single ? Mathf.Min(10, magazine) : 10;
                if (d.Id == ClassicWeaponId.Rl1) minShots = 1;   // C-00 06.10: one round + 30 recovery frames
                float baseline = 0f;
                for (int f = 0; f < 20; f++) { yield return null; baseline += SocketForward(body, facing) / 20f; }   // the pre-fire rest
                var o = new Olcum();
                int serial0 = _s.Controller.ShotSerial, budget = minShots * Mathf.CeilToInt(3600f / Mathf.Max(30f, def.fire.rpm)) + 30;
                for (int f = 0; f < budget && _s.Controller.ShotSerial - serial0 < minShots; f++)
                {
                    if (_s.Controller.Classic.IsReloading || _s.Controller.Classic.Ammo <= 0) break;
                    int before = _s.Controller.ShotSerial;
                    _s.Girdi.AtesBasili = true;
                    _s.Girdi.AtesBasti = (f % 6) == 0;   // the singles fire on held trigger too; the tap is harmless
                    yield return null;
                    Olc(body, rig, alignment, facing, baseline, _s.Controller.ShotSerial != before, o);
                }

                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                for (int f = 0; f < 30; f++)   // the recovery — a burst's last rounds still fire after the trigger is let go
                {
                    int before = _s.Controller.ShotSerial;
                    yield return null;
                    Olc(body, rig, alignment, facing, baseline, _s.Controller.ShotSerial != before, o);
                }
                int shots = _s.Controller.ShotSerial - serial0;
                string key = d.RosterKey;
                rows.Add(o.Satir(key, shots));
                if (shots < minShots || o.ShotFrames < minShots || o.LeftFrames == 0)
                    bos.Add(key + ": ölçülen atış/kare yok (atış " + shots + "/" + minShots + ", tam ağırlıkta atış karesi " + o.ShotFrames + ", sol kare " + o.LeftFrames + ")");
                if (o.BindingMiss > 0) wrong.Add(key + ": tutuş başka rig'i taşıyor ya da sol el başka rig'in çerçevesinde (" + o.BindingMiss + " kare)");
                if (o.Right > 0.01f) wrong.Add(key + ": sağ el tutamaktan " + Cm(o.Right) + " cm");
                if (o.Left > 0.01f) wrong.Add(key + ": sol bilek hedefinden " + Cm(o.Left) + " cm (shortfall " + Cm(o.Shortfall) + ", residual " + Cm(o.Residual) + ")");
                if (o.LeftIk > 0.01f) wrong.Add(key + ": sol avuç destekten (H1) " + Cm(o.LeftIk) + " cm");
                if (o.ConservationMiss > 0) wrong.Add(key + ": tutuşun uyguladığı tepme kanaldan farklı (" + o.ConservationMiss + " kare, en kötü " + (o.ConservationWorst * 1000f).ToString("0.000", CultureInfo.InvariantCulture) + " mm)");
                float capDeg = FeelRuntime.Current.Recoil.heldKickMaxDegrees;
                if (d.Id == ClassicWeaponId.Sr1)
                {
                    // C-00 06.10 (c): a known deviation — the hold applies no kick on sr_1 (S-06 / S-02 joint triage, R3); reported only
                    if (o.Applied <= 0f && o.AppliedDeg <= 0f) known.Add(key + ": tutuş tepmeyi uygulamıyor — bilinen sapma (C-00 06.10 (c), R3)");
                }
                else if (d.Id == ClassicWeaponId.Rl1)
                {
                    // the shouldered row: no translation (the shoulder takes the push), the rise about the tube's shoulder contact — up to
                    // the decided cap (the feel gate holds it to >= 90 % of the old rise); that rotation swings the grip below the tube's
                    // axis forward by its drop × sin(rise), so the socket's forward bound is not the push's one-way check here
                    if (o.Applied > 1e-4f) wrong.Add(key + ": omuz satırında öteleme " + Cm(o.Applied) + " cm (kural 0)");
                    if (o.AppliedDeg <= 0f) wrong.Add(key + ": tutuş tepmeyi uygulamadı (kayıp)");
                    if (o.AppliedDeg > capDeg + 0.05f) wrong.Add(key + ": omuz satırında açı " + Deg(o.AppliedDeg) + "° > tavan " + Deg(capDeg) + "°");
                }
                else
                {
                    if (o.Applied <= 0f) wrong.Add(key + ": tutuş tepmeyi uygulamadı (kayıp)");
                    else if (o.ObservedBackAtMax < 0.7f * o.Applied) wrong.Add(key + ": görünür soket uygulanan geri tepmenin %70'inden az geri gitti (" + Cm(o.ObservedBackAtMax) + " / " + Cm(o.Applied) + " cm)");
                    if (o.AppliedDeg > capDeg + 0.05f) wrong.Add(key + ": açı " + Deg(o.AppliedDeg) + "° > tavan " + Deg(capDeg) + "°");
                }

                if (d.Id != ClassicWeaponId.Rl1 && o.Forward > 0.005f) wrong.Add(key + ": görünür soket ileri aştı " + Cm(o.Forward) + " cm (> 0,5)");
                if (o.MinAppliedDeg < -0.5f) wrong.Add(key + ": namlu aşağı aştı " + Deg(o.MinAppliedDeg) + "°");
                worstLeft = Mathf.Max(worstLeft, o.Left);
                yield return new WaitForSeconds(0.5f);
            }

            Assert.IsEmpty(bos, "kaynak kayıtta yok\n" + string.Join("\n", bos) + "\n" + string.Join("\n", rows));
            Debug.Log("[DD-TEST] wp087.heldkick left max " + Cm(worstLeft) + " cm\n" + string.Join("\n", rows));
            foreach (string k in known) Debug.Log("[DD-TEST] wp087.heldkick known " + k);
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }

        // the muzzle in the chest's position and the player's yaw frame: forward (m) and pitch (°)
        private static void Namlu(PlayerSkinnedVisual body, WeaponVisualRig rig, Transform facing, out float fwd, out float pitch)
        {
            Transform chest = body.Animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = body.Animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 f = Vector3.ProjectOnPlane(facing.forward, Vector3.up).normalized;
            fwd = Vector3.Dot(rig.Muzzle.position - chest.position, f);
            pitch = Mathf.Asin(Mathf.Clamp(rig.Muzzle.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // one shot from rest: the muzzle's most back (m) and most up (°) against the 20-frame rest, until the next shot or 12 frames
        private IEnumerator IlkAtis(ClassicWeaponDefinition d, PlayerSkinnedVisual body, Transform mount, float[] result)
        {
            _s.Controller.EquipClassic(d.Id, _s.Config);
            yield return SpecialWeaponSahne.Kareler(60);
            WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
            Transform facing = _s.Rig.transform;
            float b0 = 0f, p0 = 0f;
            for (int f = 0; f < 20; f++) { yield return null; Namlu(body, rig, facing, out float bf, out float pf); b0 += bf / 20f; p0 += pf / 20f; }
            int serial0 = _s.Controller.ShotSerial, first = -1;
            float back = 0f, up = 0f;
            for (int f = 0; f < 90; f++)
            {
                bool firing = first < 0;
                _s.Girdi.AtesBasili = firing;
                _s.Girdi.AtesBasti = firing && (f % 6) == 0;
                yield return null;
                int shots = _s.Controller.ShotSerial - serial0;
                if (shots >= 1 && first < 0) first = f;
                if (first < 0) continue;
                if (shots > 1 || f - first > 12) break;
                Namlu(body, rig, facing, out float bf, out float pf);
                back = Mathf.Max(back, b0 - bf);
                up = Mathf.Max(up, pf - p0);
            }

            _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
            result[0] = first >= 0 ? 1f : 0f; result[1] = back; result[2] = up;
            yield return SpecialWeaponSahne.Kareler(30);
        }

        /// <summary>
        /// WP-087 C (C-00 06.10 (b)): the hold carrying the kick keeps the shot's feel — each classic's first shot from rest moves the
        /// muzzle back and up at least 90 % of the old path's (the hold off, the body's spring), in the same scene. rl_1 is the shouldered
        /// row (its translation is 0 by rule: rise only). sr_1 is reported, not gated: C-00 06.10 (c) — a known deviation, S-06 / S-02's
        /// joint triage (the hold does not apply the kick on it: UpperAtRest).
        /// </summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — His_IlkAtis_EskiYolun90i_GeriVeKalkis_Ustten is the top-down run;
        // His_IlkAtis_EskiYolun90i_GeriVeKalkis keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator His_IlkAtis_EskiYolun90i_GeriVeKalkis_Ustten() => His_IlkAtis_EskiYolun90i_GeriVeKalkisCore(DD.Prototype.CameraSystem.CameraMode.TopDown);

        [UnityTest]
        [Timeout(900000)]
        [Category("FPTP")]
        public IEnumerator His_IlkAtis_EskiYolun90i_GeriVeKalkis() => His_IlkAtis_EskiYolun90i_GeriVeKalkisCore(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);

        private IEnumerator His_IlkAtis_EskiYolun90i_GeriVeKalkisCore(DD.Prototype.CameraSystem.CameraMode mode)
        {
            // the scene is built with the hold on (a hold off at build time is never configured, so switching it on later would leave
            // both passes on the old path — c1run 06.10 read 100 % everywhere); the old path is the hold switched off at run time
            yield return Kur(true, mode);
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Assert.IsNotNull(body.HoldIk, "tutuş IK yok");
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var old = new Dictionary<ClassicWeaponId, float[]>();
            WeaponHoldIk.Enabled = false;
            yield return SpecialWeaponSahne.Kareler(30);
            Assert.IsFalse(body.HoldIk.Active, "kaynak kayıtta: eski yol için tutuş kapanmadı");
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                var r = new float[3];
                yield return IlkAtis(d, body, mount, r);
                old[d.Id] = r;
            }

            WeaponHoldIk.Enabled = true;
            yield return SpecialWeaponSahne.Kareler(30);
            var rows = new List<string>();
            var wrong = new List<string>();
            var bos = new List<string>();
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                var r = new float[3];
                yield return IlkAtis(d, body, mount, r);
                // the source is in the record: the new pass ran on the hold carrying this weapon's rig
                // sr_1: the hold does not carry its kick — C-00 06.10 (c), a known deviation (R3), reported below like its ratios
                if (d.Id != ClassicWeaponId.Sr1 && (!body.HoldIk.Active || body.HoldIk.KickSource == null)) bos.Add(d.RosterKey + ": tutuş tepmeyi taşımıyor (yeni geçiş eski yolda)");
                float[] o = old[d.Id];
                float back = o[1] > 1e-4f ? r[1] / o[1] : float.NaN, up = o[2] > 1e-3f ? r[2] / o[2] : float.NaN;
                string key = d.RosterKey, row = key + " (" + WeaponFeelIds.From(d.Id) + ", gain x" +
                    FeelRuntime.Current.Recoil.HeldKickBackGain(WeaponFeelIds.From(d.Id)).ToString("0.00", CultureInfo.InvariantCulture) + ") first shot back " +
                    Cm(r[1]) + " / old " + Cm(o[1]) + " cm = " + (back * 100f).ToString("0", CultureInfo.InvariantCulture) + " %, rise " + Deg(r[2]) +
                    " / old " + Deg(o[2]) + "° = " + (up * 100f).ToString("0", CultureInfo.InvariantCulture) + " %";
                rows.Add(row);
                if (o[0] < 1f || r[0] < 1f || o[2] <= 1e-3f) { bos.Add(key + ": atış ya da eski yol kalkışı kayıtta yok"); continue; }
                if (d.Id == ClassicWeaponId.Sr1) continue;   // C-00 06.10 (c): known deviation, reported
                if (up < 0.9f) wrong.Add(row + ": kalkış eski yolun %90'ından az");
                if (d.Id != ClassicWeaponId.Rl1 && (o[1] <= 1e-4f || back < 0.9f)) wrong.Add(row + ": geri gidiş eski yolun %90'ından az");
            }

            Assert.IsEmpty(bos, "kaynak kayıtta yok\n" + string.Join("\n", bos) + "\n" + string.Join("\n", rows));
            Debug.Log("[DD-TEST] wp087.heldkick.feel\n" + string.Join("\n", rows));
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }

        /// <summary>
        /// WP-087 (K7 + council MUST-1): the fade — ar_1 and lmg_2 hold the trigger until the magazine empties and the auto reload fades
        /// the hold out; ar_1 also presses reload mid-burst. Nothing jumps while the weight moves: on every frame the share the rig's body
        /// gave away is the hold's weight, the hold carries THIS rig and applies exactly the arms channel × its push share — the visible
        /// kick w·arms + (1 − w)·body spring is then as smooth as w and both kicks are (MoveTowards, an exact critically damped step, the
        /// spring). The visible k's |Δ²| is reported: right after a shot it is the kick's own fast return (ω²·dt²·x ≈ 2–3 cm at 60 fps on
        /// ar_1, c1run 06.10), not a fade jump — the earlier 1 cm bound on it measured that, not the fade.
        /// </summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramaz_Ustten is the top-down run;
        // Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramaz keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramaz_Ustten() => Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramazCore(DD.Prototype.CameraSystem.CameraMode.TopDown);

        [UnityTest]
        [Timeout(600000)]
        [Category("FPTP")]
        public IEnumerator Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramaz() => Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramazCore(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);

        private IEnumerator Solma_SarjorBitiminde_VeAtisOrtasindaReload_TepmeSicramazCore(DD.Prototype.CameraSystem.CameraMode mode)
        {
            yield return Kur(true, mode);
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var rows = new List<string>();
            var wrong = new List<string>();
            foreach ((ClassicWeaponId id, bool midBurst) in new[] { (ClassicWeaponId.AssaultRifle, false), (ClassicWeaponId.Lmg2, false), (ClassicWeaponId.AssaultRifle, true) })
            {
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
                WeaponHoldIk hold = body.HoldIk;
                float k1 = float.NaN, k2 = float.NaN, worst = 0f, shareMiss = 0f, applyMiss = 0f;
                int fadeFrames = 0, shotsSeen = 0, frames = 0, sinceShot = 99, sourceMiss = 0;
                bool reloadSeen = false;
                for (int f = 0; f < 900; f++)
                {
                    int before = _s.Controller.ShotSerial;
                    _s.Girdi.AtesBasili = !reloadSeen;
                    _s.Girdi.AtesBasti = !reloadSeen && (f % 6) == 0;
                    _s.Girdi.DoldurBasti = midBurst && shotsSeen == 4 && !reloadSeen;
                    yield return null;
                    bool shot = _s.Controller.ShotSerial != before;
                    if (shot) shotsSeen++;
                    if (_s.Controller.Classic.IsReloading) reloadSeen = true;
                    float w = hold != null && hold.Active ? hold.Weight : 0f;
                    if (w > 0f && w < 1f) fadeFrames++;
                    // single source through the fade (read after the last LateUpdate: the share 895 wrote is that frame's 890 weight)
                    shareMiss = Mathf.Max(shareMiss, Mathf.Abs(rig.ArmsCarryKick - w));
                    if (w > 0f)
                    {
                        if (!ReferenceEquals(hold.KickSource, rig)) sourceMiss++;
                        else if (!shot) applyMiss = Mathf.Max(applyMiss, Mathf.Abs(-hold.LastKickOffset.z - rig.ArmsKickBack * hold.KickPushShare));
                    }

                    float k = w * (hold != null ? -hold.LastKickOffset.z : 0f) - rig.Body.localPosition.z;
                    frames++;
                    sinceShot = shot ? 0 : sinceShot + 1;
                    // a shot is a step: start the second difference again — on the frame it is counted and on the next, where its kick
                    // first shows (the pose read here is the last LateUpdate's)
                    if (sinceShot <= 1) { k1 = k2 = float.NaN; }
                    else if (!float.IsNaN(k1) && !float.IsNaN(k2)) worst = Mathf.Max(worst, Mathf.Abs(k - 2f * k1 + k2));
                    k2 = k1; k1 = k;
                    if (reloadSeen && !_s.Controller.Classic.IsReloading) break;
                }

                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false; _s.Girdi.DoldurBasti = false;
                string row = id + (midBurst ? " mid-burst reload" : " empty → auto reload") + " shots " + shotsSeen + " frames " + frames + " fade frames " + fadeFrames +
                             " | share − weight worst " + shareMiss.ToString("0.00000", CultureInfo.InvariantCulture) + ", applied − channel × push share worst " +
                             (applyMiss * 1000f).ToString("0.000", CultureInfo.InvariantCulture) + " mm, source miss " + sourceMiss + " | worst |Δ²k| " + Cm(worst) + " cm (reported)";
                rows.Add(row);
                if (shotsSeen == 0 || !reloadSeen) wrong.Add(row + ": kaynak kayıtta değil (atış/reload yok)");
                if (fadeFrames == 0) wrong.Add(row + ": solma karesi yok (tutuş reload'da sönmedi?)");
                if (shareMiss > 1e-4f) wrong.Add(row + ": gövdenin bıraktığı pay tutuşun ağırlığından farklı (solmada sıçrar)");
                if (applyMiss > 1e-4f) wrong.Add(row + ": tutuşun uyguladığı tepme kanal × itme payından farklı");
                if (sourceMiss > 0) wrong.Add(row + ": solmada tutuş başka kaynağı taşıyor");
                yield return SpecialWeaponSahne.Kareler(30);
            }

            Debug.Log("[DD-TEST] wp087.heldkick.fade\n" + string.Join("\n", rows));
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        /// <summary>WP-087 (N2, C-00 06.10): a second reload of the same weapon takes the same magazine part to the hand again (G3: bound once).</summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — AyniSilahIkinciReload_AyniParcaEleGider_Ustten is the top-down run;
        // AyniSilahIkinciReload_AyniParcaEleGider keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AyniSilahIkinciReload_AyniParcaEleGider_Ustten() => AyniSilahIkinciReload_AyniParcaEleGiderCore(DD.Prototype.CameraSystem.CameraMode.TopDown);

        [UnityTest]
        [Timeout(300000)]
        [Category("FPTP")]
        public IEnumerator AyniSilahIkinciReload_AyniParcaEleGider() => AyniSilahIkinciReload_AyniParcaEleGiderCore(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);

        private IEnumerator AyniSilahIkinciReload_AyniParcaEleGiderCore(DD.Prototype.CameraSystem.CameraMode mode)
        {
            yield return Kur(true, mode);
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineVisual v = _s.Controller.MagazineVisual;
            Transform first = null;
            for (int r = 0; r < 2; r++)
            {
                int toHand0 = v.ToHandCount;
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
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı (" + r + ")");
                yield return SpecialWeaponSahne.Bekle(() => v.InHand, 3f, "parça ele geçmedi (" + r + ")");
                if (r == 0) first = v.Part; else Assert.AreSame(first, v.Part, "ikinci reload aynı parçayı taşır");
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 6f, "reload bitmedi (" + r + ")");
                yield return null;
                Assert.IsFalse(v.InHand, "parça eve döndü (" + r + ")");
                Assert.Greater(v.ToHandCount, toHand0, "parça ele gitti (" + r + ")");
            }
        }

        /// <summary>WP-087: with the hold off nothing changes — the body inside the rig kicks back as before and the arms carry none (K6).</summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — TutusKapali_GovdeEskiTepmede_Ustten is the top-down run;
        // TutusKapali_GovdeEskiTepmede keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator TutusKapali_GovdeEskiTepmede_Ustten() => TutusKapali_GovdeEskiTepmedeCore(DD.Prototype.CameraSystem.CameraMode.TopDown);

        [UnityTest]
        [Timeout(180000)]
        [Category("FPTP")]
        public IEnumerator TutusKapali_GovdeEskiTepmede() => TutusKapali_GovdeEskiTepmedeCore(DD.Prototype.CameraSystem.CameraMode.ThirdPerson);

        private IEnumerator TutusKapali_GovdeEskiTepmedeCore(DD.Prototype.CameraSystem.CameraMode mode)
        {
            yield return Kur(false, mode);
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(60);
            WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
            float bodyBack = 0f, share = 0f;
            int serial0 = _s.Controller.ShotSerial;
            _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
            yield return null;
            _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
            for (int f = 0; f < 8; f++) { yield return null; bodyBack = Mathf.Max(bodyBack, -rig.Body.localPosition.z); share = Mathf.Max(share, rig.ArmsCarryKick); }
            Debug.Log("[DD-TEST] wp087.heldkick.off body back " + Cm(bodyBack) + " cm, share " + share);
            Assert.AreEqual(1, _s.Controller.ShotSerial - serial0, "kaynak kayıtta: tek atış");
            Assert.AreEqual(0f, share, "tutuş kapalıyken kol payı 0");
            Assert.Greater(bodyBack, 0.01f, "tutuş kapalıyken gövde eskisi gibi geri teper");
        }
    }
}
