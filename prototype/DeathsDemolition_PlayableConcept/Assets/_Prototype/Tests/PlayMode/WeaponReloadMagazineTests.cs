using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-082 reload magazine contract v1 (S-02 with S-06, C-00 05.10) on the current rig: a real reload of a magazine weapon
    /// walks weapon → hand (spent) → hand (fresh) → weapon — the clip's AP-009C beats (mag_out / mag_in / chamber) plus the
    /// timeline for the beat no clip carries yet (mag_hand_attach) — and ends with the magazine in the weapon; a reload cut short
    /// (a weapon switch) puts it back at once; rl_1 starts on an empty tube. The ammo's timing is the runtime's (presentation only).
    /// Game frames (1/60).
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class WeaponReloadMagazineTests
    {
        private SpecialWeaponSahne _s;
        private float _dt0;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = _dt0;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private IEnumerator Kur()
        {
            _dt0 = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        private IEnumerator UcAtisVeReload()
        {
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
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Reload_SarjorSilahElElSilah_TamDortVurus()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            int event0 = m.FromEvent, timer0 = m.FromTimer, early0 = m.EndedEarly;
            int ammoBefore = _s.Controller.Classic.Ammo;
            yield return UcAtisVeReload();
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı");
            Assert.IsTrue(m.Active, "şarjörlü reload sahiplik makinesini başlatmalı");
            var owners = new List<MagazineOwner> { m.Owner };
            float t0 = Time.time;
            while (_s.Controller.Classic.IsReloading && Time.time - t0 < 6f)
            {
                yield return null;
                if (owners[owners.Count - 1] != m.Owner) owners.Add(m.Owner);
            }

            yield return null;
            string seq = string.Join(" → ", owners);
            Debug.Log("[DD-TEST] wp082.reload ar_1 " + seq + " | from clip " + (m.FromEvent - event0) + ", from timer " + (m.FromTimer - timer0) +
                      " | ammo " + ammoBefore + " → " + _s.Controller.Classic.Ammo + " | clip drives " + _s.Controller.ClipDrivesMechanism);
            Assert.IsFalse(_s.Controller.Classic.IsReloading, "reload bitmedi");
            Assert.IsTrue(_s.Controller.Classic.LastReloadCompleted, "reload tamamlanmadı");
            CollectionAssert.AreEqual(new[] { MagazineOwner.Weapon, MagazineOwner.HandOld, MagazineOwner.HandNew, MagazineOwner.Weapon }, owners, seq);
            Assert.IsFalse(m.Active);
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner, "şarjör silahta bitmeli");
            Assert.AreEqual(4, (m.FromEvent - event0) + (m.FromTimer - timer0), "dört vuruş tam bir kez");
            Assert.AreEqual(early0, m.EndedEarly);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ReloadKesilir_SilahDegisimi_SarjorAninaSilahta()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            int early0 = m.EndedEarly;
            yield return UcAtisVeReload();
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı");
            yield return SpecialWeaponSahne.Bekle(() => m.Owner == MagazineOwner.HandOld || m.Owner == MagazineOwner.HandNew, 3f, "şarjör ele geçmedi");
            _s.Controller.EquipClassic(ClassicWeaponId.AutoSmg, _s.Config);   // a switch mid-reload
            yield return null;
            yield return null;
            Assert.IsFalse(m.Active, "kesilen reload sahipliği kapatmalı");
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner, "şarjör elde kaldı");
            Assert.IsFalse(_s.Controller.MagazineVisual.InHand, "şarjör parçası el soketinde kaldı");
            Assert.AreEqual(early0 + 1, m.EndedEarly);
        }

        /// <summary>
        /// WP-082 visual (S-06 socket_mag_l, AP-021 part_mag): a third-person reload moves the model's magazine part weapon →
        /// the left hand's socket → weapon; it is never in neither (H3 "no frame in the air"); the one MagazineDropped is the
        /// hand_attach beat, after the detach, at the hand — no longer at mag_out; the part comes home at its authored local pose.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Gorsel_SarjorEleGiderGeriDoner_HavadaKareYok_DusmeElde()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineVisual v = _s.Controller.MagazineVisual;
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            var drops = new List<WeaponMechanismEvent>();
            var dropOwner = new List<MagazineOwner>();
            System.Action<WeaponMechanismEvent> onMech = e => { if (e.Kind == WeaponMechanismKind.MagazineDropped) { drops.Add(e); dropOwner.Add(m.Owner); } };
            WeaponEvents.Mechanism += onMech;
            try
            {
                yield return UcAtisVeReload();
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı");
                Transform part = v.Part;
                Assert.IsNotNull(part, "ar_1 modelinde part_mag yok");
                Transform home = part.parent;
                Vector3 homePos = part.localPosition;
                Quaternion homeRot = part.localRotation;
                var parents = new List<string> { "weapon" };
                int hanging = 0, inHandFrames = 0;
                float maxSocketGap = 0f;
                float t0 = Time.time;
                while (_s.Controller.Classic.IsReloading && Time.time - t0 < 6f)
                {
                    yield return null;
                    string where = part.parent == home ? "weapon" : part.parent != null && part.parent.name == DD.Prototype.Characters.MagazineSocket.Name ? "hand" : "neither";
                    if (where == "neither") hanging++;
                    if (where == "hand") { inHandFrames++; maxSocketGap = Mathf.Max(maxSocketGap, Vector3.Distance(part.position, part.parent.position)); }
                    if (parents[parents.Count - 1] != where) parents.Add(where);
                }

                yield return null;
                string seq = string.Join(" → ", parents);
                Vector3 dropPos = drops.Count > 0 ? drops[0].Position : Vector3.zero;
                Debug.Log("[DD-TEST] wp082.visual ar_1 " + seq + " | hand frames " + inHandFrames + ", neither " + hanging + ", pivot gap " + maxSocketGap.ToString("0.000") +
                          " m | drops " + drops.Count + (drops.Count > 0 ? " owner " + dropOwner[0] + " at " + dropPos.ToString("F2") : ""));
                CollectionAssert.AreEqual(new[] { "weapon", "hand", "weapon" }, parents, seq);
                Assert.AreEqual(0, hanging, "şarjör bir karede ne silahta ne elde (H3: havada)");
                Assert.Less(maxSocketGap, 0.001f, "şarjör pivotu avuçta değil");
                Assert.AreEqual(home, part.parent);
                Assert.Less(Vector3.Distance(homePos, part.localPosition), 1e-4f, "şarjör yuvasına tam dönmedi (konum)");
                Assert.Less(Quaternion.Angle(homeRot, part.localRotation), 0.01f, "şarjör yuvasına tam dönmedi (dönüş)");
                Assert.AreEqual(1, drops.Count, "tek şarjör düşmesi");
                Assert.AreEqual(MagazineOwner.HandNew, dropOwner[0], "düşme hand_attach vuruşunda (eski şarjör elden bırakılır)");
            }
            finally
            {
                WeaponEvents.Mechanism -= onMech;
            }
        }

        /// <summary>
        /// R2 H3 (C-00 05.10, owner S-02; S-10 r2-gates.json): every magazine weapon reloads once — the four beats all land
        /// (missingEvents: a beat that never landed, by the clip or by its deadline, in a completed reload; a beat from the
        /// deadline is not missing — no clip carries mag_hand_attach yet) and the magazine part is never in neither the weapon
        /// nor a hand (floatingMagazineFrames). contactDeviationFramesMax (the hand reaching the socket on the beat, ≤ 2 frames)
        /// is S-06's MagazineContactProbe (WP-083 B2c): socket_mag_l ↔ the magazine seat on the Detach / Insert beats; the probe's
        /// no-contact value when the hand never reaches the well (before S-06's contact IK). Null only when no beat was measured. Game frames (1/60).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator H3_SarjorluSilahlar_VuruslarTam_HavadaKareYok()
        {
            yield return Kur();
            var ids = new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.Ar2, ClassicWeaponId.AutoSmg, ClassicWeaponId.Smg2, ClassicWeaponId.Lmg1, ClassicWeaponId.Lmg2, ClassicWeaponId.Sr1 };
            var report = new List<string>();
            int missingAll = 0, floatingAll = 0, measured = 0;
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            WeaponMagazineVisual v = _s.Controller.MagazineVisual;
            var probe = new MagazineContactProbe();   // WP-083 B2c (S-06): contactDeviationFramesMax
            DD.Prototype.Characters.PlayerSkinnedVisual body = _s.Rig.GetComponentInChildren<DD.Prototype.Characters.PlayerSkinnedVisual>(true);
            foreach (ClassicWeaponId id in ids)
            {
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(30);
                string key = _s.Controller.Classic.Definition != null ? _s.Controller.Classic.Definition.weaponId : id.ToString();
                yield return UcAtisVeReload();
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 2f, key + ": reload başlamadı");
                int e0 = m.FromEvent, t0 = m.FromTimer;
                Transform part = v.Part;
                Transform home = part != null ? part.parent : null;
                probe.Begin(key, part, body != null ? body.MagazineSocket : null, m);
                int floating = 0, handFrames = 0;
                float start = Time.time;
                while (_s.Controller.Classic.IsReloading && Time.time - start < 12f)
                {
                    yield return null;
                    probe.Tick();
                    if (part == null) continue;
                    bool inWeapon = part.parent == home;
                    bool inHand = part.parent != null && part.parent.name == DD.Prototype.Characters.MagazineSocket.Name;
                    if (!inWeapon && !inHand) floating++;
                    if (inHand) handFrames++;
                }

                yield return null;
                probe.End();
                Assert.IsTrue(_s.Controller.Classic.LastReloadCompleted, key + ": reload tamamlanmadı");
                int fromClip = m.FromEvent - e0, fromTimer = m.FromTimer - t0;
                int missing = Mathf.Max(0, WeaponMagazineOwnership.StepCount - fromClip - fromTimer);
                missingAll += missing;
                floatingAll += floating;
                measured++;
                report.Add(key + " beats clip " + fromClip + " / deadline " + fromTimer + " / missing " + missing + ", part " + (part != null ? "hand " + handFrames + " f, floating " + floating : "none (no part_mag)"));
                yield return new WaitForSeconds(0.3f);
            }

            Debug.Log("[DD-TEST] wp082.h3 " + string.Join(" | ", report));
            Debug.Log("[DD-TEST] wp083.h3contact max=" + probe.MaxDeviationFrames + " f, beats " + probe.BeatsMeasured + ", closestMax " + probe.MaxClosestCm.ToString("F1") + " cm :: " + probe.Report);
            Assert.AreEqual(ids.Length, measured);
            R2Olcum.Yaz("H3", "DD.Prototype.Tests.PlayMode.WeaponReloadMagazineTests.H3_SarjorluSilahlar_VuruslarTam_HavadaKareYok",
                new Dictionary<string, double?> { { "missingEvents", missingAll }, { "floatingMagazineFrames", floatingAll }, { "contactDeviationFramesMax", probe.BeatsMeasured > 0 ? probe.MaxDeviationFrames : (double?)null } },
                measured, "contactDeviationFramesMax: S-06 MagazineContactProbe (socket_mag_l ↔ şarjör yuvası, Detach/Insert vuruşları ±" + MagazineContactProbe.Window + " kare); " + probe.BeatsMeasured + " vuruş");
            Assert.AreEqual(0, missingAll, "R2 H3: inmeyen reload vuruşu\n" + string.Join("\n", report));
            Assert.AreEqual(0, floatingAll, "R2 H3: şarjör havada (ne silahta ne elde)\n" + string.Join("\n", report));
        }

        /// <summary>WP-082 visual: a camera change mid-reload ends the ownership and the magazine part is home the same frame.</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Gorsel_ReloadOrtasindaKameraDegisir_SarjorYuvada()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineVisual v = _s.Controller.MagazineVisual;
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            yield return UcAtisVeReload();
            yield return SpecialWeaponSahne.Bekle(() => v.InHand, 3f, "şarjör ele geçmedi");
            Transform part = v.Part;
            DD.Prototype.CameraSystem.CameraModeService.Request(
                DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson
                    ? DD.Prototype.CameraSystem.CameraMode.ThirdPerson : DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            yield return null;
            yield return null;
            Assert.IsFalse(m.Active, "kamera değişimi sahipliği kapatmalı");
            Assert.IsFalse(v.InHand, "şarjör elde kaldı");
            Assert.AreNotEqual(DD.Prototype.Characters.MagazineSocket.Name, part.parent != null ? part.parent.name : "", "şarjör el soketinde");
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Roketatar_BosTupleBaslar()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.Rl1, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Kareler(4);
            _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 3f, "rl_1 boş tüpte reload başlamadı");
            Assert.IsTrue(m.Active);
            Assert.AreNotEqual(MagazineOwner.HandOld, m.Owner, "boş tüpte çıkarılan eski şarjör yok");
            Assert.GreaterOrEqual(m.NextStep, (int)MagazineStep.HandAttach, "rl_1 detach'sız başlar");
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 8f, "rl_1 reload bitmedi");
            yield return null;
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner);
            Assert.IsFalse(m.Active);
            Assert.IsFalse(_s.Controller.MagazineVisual.InHand, "rl_1: parça ele gitmez (tüp)");
        }
    }
}
