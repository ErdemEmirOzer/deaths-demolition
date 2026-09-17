using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Inputs;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-042 · weapon feel v1 + roster finalization: the feel knobs stay inside the request's
    /// ceilings and the config asset carries the same A/B/C values as the code defaults; the
    /// shotgun fires a centre pellet plus an equal-arc ring (no bunching, mean on the aim); the
    /// gamepad pulse follows the equipped weapon; DEC-083 (three weapons, D data only) and the
    /// ADR-042-1 socket contract hold.
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponFeelTests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

        [TearDown]
        public void TearDown()
        {
            WeaponRoster.SetForTests(null);
        }

        // ------------------------------------------------------------------ knobs & asset parity

        [Test]
        public void HisKnoblari_BantIcinde_VeAssetKodVarsayilanlariylaBirebir()
        {
            PrototypeConfig config = Config;
            Assert.IsNotNull(config, "PrototypeConfig Resources'ta yok.");
            var defaults = new WeaponConfigSection();
            Assert.AreEqual(3, config.Weapons.ClassicWeapons.Count, "A/B/C.");
            for (int i = 0; i < config.Weapons.ClassicWeapons.Count; i++)
            {
                ClassicWeaponSettings a = config.Weapons.ClassicWeapons[i];
                ClassicWeaponSettings d = defaults.Find(a.Id);
                Assert.IsNotNull(d, a.Id + " kod varsayılanı yok.");
                string tag = a.Id + ": ";
                // Ceilings from the WP-024 request (rumble ≤ 0.3 × 120 ms) and sanity bands.
                Assert.That(a.RumbleFraction, Is.InRange(0f, 1f), tag + "RumbleFraction 0..1");
                Assert.That(a.RumbleMilliseconds, Is.InRange(0, InputPreferences.MaxRumbleMilliseconds), tag + "RumbleMilliseconds ≤ 120");
                Assert.That(a.RecoilRecoverySeconds, Is.InRange(0.02f, 0.6f), tag + "RecoilRecoverySeconds bandı");
                Assert.That(a.RecoilUpShare, Is.InRange(0f, 1f), tag + "RecoilUpShare 0..1");
                Assert.That(a.PelletRingRadiusShare, Is.InRange(0f, 1f), tag + "PelletRingRadiusShare 0..1");
                Assert.That(a.PelletRingJitterShare, Is.InRange(0f, 0.5f), tag + "PelletRingJitterShare");
                if (a.PelletCount <= 1) Assert.AreEqual(0f, a.PelletRingRadiusShare, tag + "tek mermili silahta halka yok");
                else Assert.Greater(a.PelletRingRadiusShare, 0f, tag + "pompalı halka deseni");

                // Damage / unlock untouched by the feel pass (product numbers stay).
                Assert.AreEqual(d.Damage, a.Damage, tag + "Damage değişmez");
                Assert.AreEqual(d.UnlockProfileLevel, a.UnlockProfileLevel, tag + "UnlockProfileLevel değişmez");
                Assert.AreEqual(d.FireRate, a.FireRate, tag + "FireRate değişmez");
                Assert.AreEqual(d.MagazineSize, a.MagazineSize, tag + "MagazineSize değişmez");
                Assert.AreEqual(d.ReloadTime, a.ReloadTime, tag + "ReloadTime değişmez");

                // Asset ↔ code parity for the feel knobs (the asset is what ships; the code list is the documented default).
                Assert.AreEqual(d.SpreadDegrees, a.SpreadDegrees, 1e-4f, tag + "SpreadDegrees asset≠kod");
                Assert.AreEqual(d.SpreadBloomPerShot, a.SpreadBloomPerShot, 1e-4f, tag + "SpreadBloomPerShot asset≠kod");
                Assert.AreEqual(d.SpreadBloomMax, a.SpreadBloomMax, 1e-4f, tag + "SpreadBloomMax asset≠kod");
                Assert.AreEqual(d.SpreadBloomDecay, a.SpreadBloomDecay, 1e-4f, tag + "SpreadBloomDecay asset≠kod");
                Assert.AreEqual(d.RecoilKick, a.RecoilKick, 1e-4f, tag + "RecoilKick asset≠kod");
                Assert.AreEqual(d.CameraShake, a.CameraShake, 1e-4f, tag + "CameraShake asset≠kod");
                Assert.AreEqual(d.RecoilRecoverySeconds, a.RecoilRecoverySeconds, 1e-4f, tag + "RecoilRecoverySeconds asset≠kod");
                Assert.AreEqual(d.RecoilUpShare, a.RecoilUpShare, 1e-4f, tag + "RecoilUpShare asset≠kod");
                Assert.AreEqual(d.RumbleFraction, a.RumbleFraction, 1e-4f, tag + "RumbleFraction asset≠kod");
                Assert.AreEqual(d.RumbleMilliseconds, a.RumbleMilliseconds, tag + "RumbleMilliseconds asset≠kod");
                Assert.AreEqual(d.PelletRingRadiusShare, a.PelletRingRadiusShare, 1e-4f, tag + "PelletRingRadiusShare asset≠kod");
                Assert.AreEqual(d.PelletRingJitterShare, a.PelletRingJitterShare, 1e-4f, tag + "PelletRingJitterShare asset≠kod");
            }

            // The designer's signature per weapon: A settles within its cycle, B creeps (recovery < cycle), C is heavy.
            ClassicWeaponSettings rifle = config.Weapons.Find(ClassicWeaponId.AssaultRifle);
            ClassicWeaponSettings carbine = config.Weapons.Find(ClassicWeaponId.AutoSmg);
            ClassicWeaponSettings shotgun = config.Weapons.Find(ClassicWeaponId.CombatShotgun);
            Assert.LessOrEqual(rifle.RecoilRecoverySeconds, 1f / rifle.FireRate + 0.01f, "A: geri dönüş ≈ atış döngüsü (ayrık vuruş).");
            Assert.Less(carbine.RecoilRecoverySeconds, 1f / carbine.FireRate, "B: geri dönüş döngüden kısa (hafif kick, sürünme birikimi tasarım dilinde).");
            Assert.Greater(shotgun.RecoilRecoverySeconds, rifle.RecoilRecoverySeconds * 2f, "C: ağır, uzun dönüş.");
            Assert.Greater(shotgun.RumbleFraction, rifle.RumbleFraction);
            Assert.Greater(rifle.RumbleFraction, carbine.RumbleFraction);
            Assert.Greater(shotgun.RecoilUpShare, rifle.RecoilUpShare);
        }

        // ------------------------------------------------------------------ pellet ring

        [Test]
        public void HalkaParametresi_YayUzunlugunaGore_Monoton()
        {
            float a = 4.5f, b = 4.5f * SpreadUtility.VerticalSpreadFactor;
            Assert.AreEqual(0f, SpreadUtility.RingParameterAtArcFraction(a, b, 0f), 1e-4f);
            Assert.AreEqual(Mathf.PI, SpreadUtility.RingParameterAtArcFraction(a, b, 0.5f), 0.02f, "yarı çevre = π (simetri)");
            float previous = 0f;
            for (int i = 1; i < 20; i++)
            {
                float t = SpreadUtility.RingParameterAtArcFraction(a, b, i / 20f);
                Assert.Greater(t, previous, "parametre yay kesriyle monoton artar");
                previous = t;
            }

            Assert.AreEqual(0f, SpreadUtility.RingParameterAtArcFraction(a, b, 1f), 1e-4f, "tam tur = başlangıç (halka sarar)");

            // Equal arc length on a flat ellipse ≠ equal angle: the quarter points are the axes (symmetry),
            // but an eighth of the perimeter needs MORE than 45° of parameter (the arc is slow near the wide ends).
            Assert.AreEqual(Mathf.PI / 2f, SpreadUtility.RingParameterAtArcFraction(a, b, 0.25f), 0.02f, "çeyrek çevre = 90°");
            Assert.Greater(SpreadUtility.RingParameterAtArcFraction(a, b, 0.125f), Mathf.PI / 4f + 0.1f, "sekizde bir çevre 45°'yi geçer (eşit yay ≠ eşit açı)");
        }

        [Test]
        public void PompaliHalka_MerkezArtiHalka_YigilmaYok_KoniIcinde_OrtalamaNisanda()
        {
            const float spread = 7.5f, ringShare = 0.6f, jitterShare = 0.10f;
            const int pellets = 8, pulls = 200;
            Vector3 aim = new Vector3(0.3f, 0f, 1f).normalized;
            Random.InitState(4242);
            Vector3 sum = Vector3.zero;
            float minSeparation = float.MaxValue;
            var dirs = new Vector3[pellets];
            for (int pull = 0; pull < pulls; pull++)
            {
                float phase = Random.value;
                for (int i = 0; i < pellets; i++)
                {
                    dirs[i] = SpreadUtility.ApplyRing(aim, spread, i, pellets, ringShare, jitterShare, phase);
                    Assert.LessOrEqual(Vector3.Angle(dirs[i], aim), spread + 0.01f, "pellet koni dışına çıkmaz");
                    sum += dirs[i];
                }

                // Pellet 0 = centre (only the jitter), the ring pellets sit near the ring radius horizontally.
                Assert.LessOrEqual(Vector3.Angle(dirs[0], aim), jitterShare * spread * 1.2f + 0.01f, "merkez pellet nişanda");
                for (int i = 1; i < pellets; i++)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(dirs[i], Vector3.up).normalized;
                    float yaw = Mathf.Abs(Vector3.SignedAngle(aim, flat, Vector3.up));
                    float pitch = Vector3.Angle(flat, dirs[i]);
                    float a = ringShare * spread, b = a * SpreadUtility.VerticalSpreadFactor;
                    float radial = (yaw * yaw) / (a * a) + (pitch * pitch) / (b * b);
                    Assert.That(radial, Is.InRange(0.45f, 1.9f), "halka pelleti elips üzerinde (jitter payıyla)");
                    for (int j = 1; j < i; j++)
                    {
                        minSeparation = Mathf.Min(minSeparation, Vector3.Angle(dirs[i], dirs[j]));
                    }
                }
            }

            Assert.GreaterOrEqual(minSeparation, 0.12f * spread, "halka pelletleri yığılmaz (min ayrım ≥ 0,12 × yayılma)");
            Vector3 mean = (sum / (pulls * pellets)).normalized;
            Assert.LessOrEqual(Vector3.Angle(mean, aim), 0.35f, "desenin ortalaması nişanda kalır (isabet ortalaması değişmez)");
        }

        [Test]
        public void TekMermi_VeSifirHalka_EskiDagilimiKorur()
        {
            Vector3 aim = Vector3.forward;
            Random.InitState(7);
            Assert.AreEqual(aim, SpreadUtility.ApplyRing(aim, 0f, 3, 8, 0.6f, 0.1f, 0.3f), "sıfır yayılma → nişan");
            Assert.AreEqual(aim, SpreadUtility.ApplyRing(aim, 5f, 0, 1, 0.6f, 0.1f, 0.3f), "tek pellet → nişan");
            var settings = new ClassicWeaponSettings { PelletCount = 8, SpreadDegrees = 7.5f, PelletRingRadiusShare = 0f };
            var runtime = new ClassicWeaponRuntime();
            runtime.Equip(settings, new WeaponModifierState());
            Assert.IsFalse(runtime.UsesPelletRing, "halka payı 0 → WP-002 bağımsız saçılma");
            settings.PelletRingRadiusShare = 0.6f;
            Assert.IsTrue(runtime.UsesPelletRing);
            settings.PelletCount = 1;
            Assert.IsFalse(runtime.UsesPelletRing, "tek mermi → halka yok");
        }

        [Test]
        public void Runtime_HalkaFazi_TetikBasinaBir_PelletSifirdaYenilenir()
        {
            var settings = new ClassicWeaponSettings { PelletCount = 8, SpreadDegrees = 7.5f, PelletRingRadiusShare = 0.6f, PelletRingJitterShare = 0.1f, Range = 22f };
            var runtime = new ClassicWeaponRuntime();
            runtime.Equip(settings, new WeaponModifierState());
            Random.InitState(11);
            runtime.BuildShot(Vector3.zero, Vector3.forward, 0);
            float phase = runtime.PelletRingPhase;
            for (int i = 1; i < 8; i++)
            {
                runtime.BuildShot(Vector3.zero, Vector3.forward, i);
                Assert.AreEqual(phase, runtime.PelletRingPhase, "faz tetik boyunca sabit");
            }

            runtime.BuildShot(Vector3.zero, Vector3.forward, 0);
            Assert.AreNotEqual(phase, runtime.PelletRingPhase, "yeni tetik → yeni faz");
        }

        // ------------------------------------------------------------------ rumble per weapon

        [Test]
        public void GamepadTitresim_TakiliSilahinProfili_KuşandaOnbellek_AtisteOkur()
        {
            PrototypeConfig config = Config;
            var rumble = new GamepadRumble();
            try
            {
                rumble.Subscribe();
                Assert.AreEqual(GamepadRumble.FireFraction, rumble.WeaponFireFraction, "kuşanmadan önce WP-024 varsayılanı");

                WeaponEvents.PublishWeaponEquipped(ClassicWeaponId.CombatShotgun);
                ClassicWeaponSettings c = config.Weapons.Find(ClassicWeaponId.CombatShotgun);
                Assert.AreEqual(c.RumbleFraction, rumble.WeaponFireFraction, 1e-4f, "C profili");
                Assert.AreEqual(c.RumbleMilliseconds, rumble.WeaponFireMilliseconds);

                WeaponEvents.PublishWeaponEquipped(ClassicWeaponId.AutoSmg);
                ClassicWeaponSettings b = config.Weapons.Find(ClassicWeaponId.AutoSmg);
                Assert.AreEqual(b.RumbleFraction, rumble.WeaponFireFraction, 1e-4f, "B profili");
                Assert.AreEqual(b.RumbleMilliseconds, rumble.WeaponFireMilliseconds);

                int before = rumble.PulseCount;
                AudioEvents.Raise(CombatAudioEvent.ClassicWeaponFire, Vector3.zero, 0.75f, 50);
                Assert.AreEqual(before + 1, rumble.PulseCount, "ateş cue'su bir darbe ister (gamepad olmasa da sayılır)");
            }
            finally
            {
                rumble.Unsubscribe();
                rumble.Dispose();
            }
        }

        // ------------------------------------------------------------------ DEC-083 · roster finalization

        [Test]
        public void DEC083_UcSilah_ABC_D_YalnizVeri_SecimdeYok()
        {
            Assert.AreEqual(3, WeaponRoster.Enabled.Count, "DEC-083: üç klasik silah.");
            var ids = new HashSet<ClassicWeaponId>();
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled) ids.Add(d.Id);
            Assert.IsTrue(ids.Contains(ClassicWeaponId.AssaultRifle) && ids.Contains(ClassicWeaponId.AutoSmg) && ids.Contains(ClassicWeaponId.CombatShotgun));

            ClassicWeaponDefinition heavyPistol = WeaponRoster.FindByKey("D");
            Assert.IsNotNull(heavyPistol, "D verisi kalır (kapalı).");
            Assert.IsFalse(heavyPistol.Enabled, "D etkin değil (kullanıcı kararı 2026-09-17).");
            Assert.IsFalse(heavyPistol.HasRuntimeId, "D'nin runtime kimliği yok → hiçbir ClassicWeaponId D'yi işaret etmez.");

            // Selection / shop never see D: no entry, not selectable, no HUD icon exposed.
            IWeaponRosterSource src = WeaponRosterSource.Instance;
            Assert.AreEqual(3, src.Entries.Count, "seçim listesi 3 (D yok — kilitli kart değil, yok).");
            foreach (WeaponRosterEntry e in src.Entries)
            {
                Assert.AreNotEqual(WeaponTypeClass.HeavyPistol, e.TypeClass);
                Assert.AreNotEqual(heavyPistol.HudIconId, e.HudIconId);
            }

            // The config carries exactly the three runtime weapons (no fourth ClassicWeaponSettings for D).
            Assert.AreEqual(3, Config.Weapons.ClassicWeapons.Count);
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, WeaponRoster.DefaultWeapon, "A açık / varsayılan; B, C profil seviyesiyle (DEC-008).");
        }

        // ------------------------------------------------------------------ ADR-042-1 · socket contract

        [Test]
        public void SoketSozlesmesi_TamModel_Gecer_EksikSoket_VeKaymisMarker_Yakalanir()
        {
            GameObject root = BuildContractModel(WeaponTypeClass.Rifle);
            try
            {
                List<string> v = WeaponSocketContract.Validate(root.transform, WeaponTypeClass.Rifle);
                Assert.IsEmpty(v, "tam model: " + string.Join("; ", v));

                // Foregrip marker drifts 5 cm → over the 3 cm tolerance.
                WeaponSocketContract.Find(root.transform, WeaponSocketContract.MarkerForegrip).localPosition += new Vector3(0f, 0f, 0.05f);
                v = WeaponSocketContract.Validate(root.transform, WeaponTypeClass.Rifle);
                Assert.AreEqual(1, v.Count, string.Join("; ", v));
                StringAssert.Contains(WeaponSocketContract.MarkerForegrip, v[0]);

                // A missing attachment socket is a fault; the muzzle is required.
                Object.DestroyImmediate(WeaponSocketContract.Find(root.transform, WeaponSocketContract.AttachTop).gameObject);
                Object.DestroyImmediate(WeaponSocketContract.Find(root.transform, WeaponSocketContract.Muzzle).gameObject);
                v = WeaponSocketContract.Validate(root.transform, WeaponTypeClass.Rifle);
                Assert.IsTrue(v.Exists(f => f.StartsWith(WeaponSocketContract.AttachTop)), string.Join("; ", v));
                Assert.IsTrue(v.Exists(f => f.StartsWith(WeaponSocketContract.Muzzle)), string.Join("; ", v));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SoketSozlesmesi_OlculerAP009C_RosterAdlariylaBirebir()
        {
            // The roster definitions name the same sockets the contract requires (WP-034 §3 ↔ ADR-042-1).
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                Assert.AreEqual(WeaponSocketContract.RootSocket, d.SocketRoot, d.RosterKey);
                Assert.AreEqual(WeaponSocketContract.Muzzle, d.MuzzleSocket, d.RosterKey);
                Assert.AreEqual(WeaponSocketContract.Ejection, d.EjectionSocket, d.RosterKey);
                CollectionAssert.AreEquivalent(new[] { WeaponSocketContract.AttachBarrel, WeaponSocketContract.AttachTop, WeaponSocketContract.AttachStock }, d.AttachmentSockets, d.RosterKey);
                WeaponSocketContract.Measures m = WeaponSocketContract.MeasuresOf(d.TypeClass);
                Assert.Greater(m.Length, 0f, d.RosterKey + " ölçüsü tanımlı");
                Assert.Less(m.Muzzle.z, m.Length, d.RosterKey + " namlu ucu uzunluğun içinde");
                Assert.Greater(m.Muzzle.z, m.Foregrip.z, d.RosterKey + " namlu ön kabzanın önünde");
            }

            Assert.AreEqual(1.20f, WeaponSocketContract.MeasuresOf(WeaponTypeClass.Rifle).Length, 1e-4f);
            Assert.AreEqual(0.85f, WeaponSocketContract.MeasuresOf(WeaponTypeClass.Carbine).Length, 1e-4f);
            Assert.AreEqual(0.78f, WeaponSocketContract.MeasuresOf(WeaponTypeClass.Shotgun).Length, 1e-4f);
            Assert.AreEqual(0f, WeaponSocketContract.MeasuresOf(WeaponTypeClass.HeavyPistol).Length, "D ölçüsü yok (DEC-083).");
        }

        private static GameObject BuildContractModel(WeaponTypeClass type)
        {
            WeaponSocketContract.Measures m = WeaponSocketContract.MeasuresOf(type);
            var root = new GameObject("AP013_test_blockout");
            Child(root.transform, WeaponSocketContract.Muzzle, m.Muzzle);
            Child(root.transform, WeaponSocketContract.Ejection, new Vector3(0.03f, 0.03f, 0.08f));
            Child(root.transform, WeaponSocketContract.AttachBarrel, m.Muzzle - new Vector3(0f, 0f, 0.1f));
            Child(root.transform, WeaponSocketContract.AttachTop, new Vector3(0f, 0.06f, 0.05f));
            Child(root.transform, WeaponSocketContract.AttachStock, new Vector3(0f, 0f, -0.25f));
            Child(root.transform, WeaponSocketContract.MarkerGrip, Vector3.zero);
            Child(root.transform, WeaponSocketContract.MarkerForegrip, m.Foregrip);
            Child(root.transform, WeaponSocketContract.MarkerMag, m.Mag);
            return root;
        }

        private static void Child(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
        }
    }
}
