using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 B2 · the ten weapons' behaviour on the runtime object, without a scene: burst cadence,
    /// the single-fire press buffer, the per-shell reload (shell by shell, trigger interrupts), the
    /// reserve (round refill, never taken away) and its floor, the LMG settle curve, focus / movement,
    /// the Gauss and centre-ring pellet patterns, and the weapon's head / armour multipliers on a hit.
    /// Numbers are the council's (DIRECTION §0/§1) as the ArsenalAssetBuilder writes them.
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class ArsenalB2Tests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

        private static ClassicWeaponRuntime Equip(ClassicWeaponId id)
        {
            PrototypeConfig config = Config;
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            ClassicWeaponSettings s = config.Weapons.Find(id);
            Assert.IsNotNull(s, id + " kopyası yok");
            var rt = new ClassicWeaponRuntime();
            rt.Equip(s, new WeaponModifierState());
            return rt;
        }

        [TearDown]
        public void TearDown()
        {
            DamageHitContext.Clear();
        }

        // ------------------------------------------------------------------ fire modes

        [Test]
        public void Burst_UcAtis_005Aralik_SonraGecikme026()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.Ar2);
            Assert.AreEqual(WeaponFireMode.Burst, rt.FireMode);
            const float dt = 0.005f;
            var times = new System.Collections.Generic.List<float>();
            rt.BeginBurst();
            Assert.IsTrue(rt.BurstActive);
            float t = 0f;
            float nextReady = -1f;
            while (t < 0.6f)
            {
                if (rt.BurstActive && rt.CanFire)
                {
                    rt.ConsumeShot();
                    times.Add(t);
                }
                else if (!rt.BurstActive && nextReady < 0f && rt.CanFire)
                {
                    nextReady = t;
                }

                rt.Tick(dt);
                t += dt;
            }

            Assert.AreEqual(3, times.Count, "burst = 3 atış");
            Assert.AreEqual(0.05f, times[1] - times[0], dt + 1e-4f, "burst içi 0,05 s");
            Assert.AreEqual(0.05f, times[2] - times[1], dt + 1e-4f);
            Assert.AreEqual(times[2] + 0.26f, nextReady, dt + 1e-4f, "burst arası 0,26 s");
            Assert.AreEqual(21, rt.Ammo);
        }

        [Test]
        public void Tekli_BasisTamponu_CevrimSonundaAtar_EskiBasisSoner()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.Sr1);
            Assert.AreEqual(WeaponFireMode.Single, rt.FireMode);
            rt.ConsumeShot();
            Assert.AreEqual(1.25f, rt.ShotInterval, 1e-4f, "48 RPM sürgü");

            rt.Tick(0.5f);
            rt.BufferPress();
            rt.Tick(0.3f);
            Assert.IsFalse(rt.PressBuffered, "0,25 s'den eski basış unutulur");

            rt.Tick(0.3f);   // t 1.10
            rt.BufferPress();
            rt.Tick(0.16f);  // t 1.26
            Assert.IsTrue(rt.PressBuffered && rt.CanFire, "çevrimde yapılan basış çevrim bitince atar");
            rt.ConsumeShot();
            Assert.IsFalse(rt.PressBuffered, "atış tamponu tüketir");
        }

        // ------------------------------------------------------------------ ammo

        [Test]
        public void TekFisek_KabukKabukDoler_TetikKeser_YuklenenlerKalir()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.CombatShotgun);
            for (int i = 0; i < 3; i++) rt.ConsumeShot();
            Assert.AreEqual(4, rt.Ammo);
            Assert.AreEqual(42, rt.ReserveAmmo);
            Assert.IsTrue(rt.TryBeginReload());
            Assert.AreEqual(0.75f + 3 * 0.55f, rt.ReloadDuration, 1e-4f, "taktik: giriş + 3 kabuk + kapanış");

            rt.Tick(1.0f);   // start 0.3975 + one 0.55 shell
            Assert.AreEqual(5, rt.Ammo, "ilk kabuk girdi");
            rt.Tick(0.5f);
            Assert.AreEqual(6, rt.Ammo, "ikinci kabuk");
            Assert.AreEqual(40, rt.ReserveAmmo, "kabuk başına yedekten");
            Assert.IsTrue(rt.TryInterruptShellReload(), "tetik tek-fişek dolumu keser");
            Assert.IsFalse(rt.IsReloading);
            Assert.AreEqual(6, rt.Ammo, "yüklenenler kalır");
            Assert.IsFalse(rt.LastReloadCompleted);

            ClassicWeaponRuntime empty = Equip(ClassicWeaponId.CombatShotgun);
            for (int i = 0; i < 7; i++) empty.ConsumeShot();
            Assert.IsTrue(empty.TryBeginReload());
            Assert.AreEqual(0.75f + 7 * 0.55f + 0.5f, empty.ReloadDuration, 1e-4f, "boştan: + pompa 0,5 s");
            Assert.IsFalse(empty.TryInterruptShellReload(), "boş şarjörde kesme yok");
            empty.Tick(5.2f);
            Assert.AreEqual(7, empty.Ammo);
            Assert.AreEqual(35, empty.ReserveAmmo);
            Assert.IsTrue(empty.LastReloadCompleted);
        }

        [Test]
        public void Yedek_TurBasiDolar_AsagiInmez_BosKalincaTabanBirSarjor()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.AssaultRifle);
            Assert.IsFalse(rt.ReserveInfinite);
            Assert.AreEqual(210, rt.ReserveAmmo, "tur 1 yedeği");
            rt.RefillReserveForRound(5);
            Assert.AreEqual(Mathf.RoundToInt(210f * (1f + 0.12f * 4)), rt.ReserveAmmo, "tur 5: × (1 + 0,12 × 4)");
            int r5 = rt.ReserveAmmo;
            rt.RefillReserveForRound(2);
            Assert.AreEqual(r5, rt.ReserveAmmo, "dolum hiç mermi almaz");

            int guard = 0;
            while ((rt.Ammo > 0 || rt.ReserveAmmo > 0) && guard++ < 2000)
            {
                if (rt.Ammo > 0) rt.ConsumeShot();
                else { Assert.IsTrue(rt.TryBeginReload()); rt.Tick(3f); }
            }

            Assert.AreEqual(0, rt.Ammo);
            Assert.AreEqual(0, rt.ReserveAmmo);
            Assert.IsFalse(rt.TryBeginReload(), "yüklenecek mermi yok");
            rt.Tick(2f);
            Assert.AreEqual(0.5f, rt.FloorRefill01, 0.01f, "taban sayacı 4 s'nin yarısında");
            rt.Tick(1.9f);
            Assert.AreEqual(0, rt.ReserveAmmo);
            rt.Tick(0.2f);
            Assert.AreEqual(30, rt.ReserveAmmo, "4 s sonra +1 şarjör");
            Assert.AreEqual(0f, rt.FloorRefill01);
            Assert.IsTrue(rt.TryBeginReload());
        }

        [Test]
        public void TanimsizAyar_M3Davranisi_SonsuzYedek()
        {
            var rt = new ClassicWeaponRuntime();
            rt.Equip(new ClassicWeaponSettings { MagazineSize = 5, ReloadTime = 1f, FireRate = 5f }, new WeaponModifierState());
            Assert.IsTrue(rt.ReserveInfinite);
            Assert.AreEqual(WeaponFireMode.Auto, rt.FireMode);
            for (int i = 0; i < 5; i++) rt.ConsumeShot();
            Assert.IsTrue(rt.TryBeginReload());
            Assert.AreEqual(1f, rt.ReloadDuration, 1e-5f);
            rt.Tick(1.01f);
            Assert.AreEqual(5, rt.Ammo);
            Assert.AreEqual(1f, rt.MoveSpeedMultiplier(true), "tanımsız ayar hareketi yavaşlatmaz");
        }

        // ------------------------------------------------------------------ accuracy

        [Test]
        public void Oturma_KoniDaralir_TabanPayindaDurur_BirakincaAcilir()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.Lmg2);
            float hip = rt.CurrentSpreadDegrees;
            Assert.AreEqual(3.5f, hip, 1e-4f, "tepeden hip");
            rt.ConsumeShot();
            Assert.AreEqual(3.5f - 0.40f, rt.CurrentSpreadDegrees, 1e-4f, "her atış −0,40°");
            for (int i = 0; i < 12; i++) rt.ConsumeShot();
            Assert.AreEqual(3.5f * 0.35f, rt.CurrentSpreadDegrees, 1e-4f, "taban = hip × 0,35");
            rt.Tick(2f);
            Assert.AreEqual(3.5f, rt.CurrentSpreadDegrees, 1e-4f, "bırakınca hip'e döner");
        }

        [Test]
        public void Odak_VeHareket_KoniVeHiz()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.Lmg1);
            Assert.AreEqual(1f, rt.MoveSpeedMultiplier(false), 1e-5f);
            Assert.AreEqual(0.80f, rt.MoveSpeedMultiplier(true), 1e-5f, "ateş ederken ×0,80");
            rt.SetFocus(true);
            rt.Tick(1f);
            Assert.AreEqual(1f, rt.Ads01, 1e-5f, "odak 0,45 s'de dolar");
            Assert.AreEqual(1.4f, rt.CurrentSpreadDegrees, 1e-4f, "tepeden odak konisi");
            Assert.AreEqual(0.75f, rt.MoveSpeedMultiplier(false), 1e-5f, "odak ×0,75");
            Assert.AreEqual(0.80f * 0.75f, rt.MoveSpeedMultiplier(true), 1e-5f);
            rt.SetMovement01(1f);
            Assert.AreEqual(1.4f * 1.9f, rt.CurrentSpreadDegrees, 1e-4f, "tam hızda × hareket çarpanı");
            rt.SetFocus(false);
            rt.SetMovement01(0f);
            rt.Tick(1f);
            Assert.AreEqual(0f, rt.Ads01, 1e-5f);
            Assert.AreEqual(2.4f, rt.CurrentSpreadDegrees, 1e-4f);
        }

        [Test]
        public void TepedenGeriTepme_YatayKayma_DenemedeKapanir()
        {
            ClassicWeaponRuntime rt = Equip(ClassicWeaponId.AssaultRifle);
            rt.ConsumeShot();
            Assert.AreEqual(0.55f, rt.LastRecoilPitch, 1e-5f);
            Assert.AreEqual(0.25f, rt.LastRecoilYaw, 1e-5f, "desen ilk değer");
            Assert.AreEqual(0.25f * 0.6f, rt.RecoilYawDegrees, 1e-5f, "tepeden yalnız yatay pay");
            rt.Tick(1f);
            Assert.AreEqual(0f, rt.RecoilYawDegrees, 1e-5f, "toparlanır");
        }

        [Test]
        public void Desenler_MerkezHalkalar_VeGauss_KonideKalir()
        {
            Random.InitState(3);
            ClassicWeaponRuntime sg = Equip(ClassicWeaponId.CombatShotgun);
            float cone = sg.CurrentSpreadDegrees;
            Assert.AreEqual(7.5f, cone, 1e-4f);
            Assert.AreEqual(1.0f * cone, sg.PelletRingDegrees, 1e-4f, "dış halka = 1,00 × koni");
            float jitter = 0.10f * cone;
            Vector3 aim = Vector3.forward;
            Assert.LessOrEqual(HorizontalAngle(sg.BuildShot(Vector3.zero, aim, 0).Direction), jitter + 1e-3f, "merkez pellet");
            for (int i = 1; i <= 3; i++)
            {
                Assert.LessOrEqual(HorizontalAngle(sg.BuildShot(Vector3.zero, aim, i).Direction), 0.40f * cone + jitter + 1e-3f, "iç halka @ 0,40");
            }

            float outerMax = 0f;
            for (int i = 4; i <= 7; i++)
            {
                float h = HorizontalAngle(sg.BuildShot(Vector3.zero, aim, i).Direction);
                Assert.LessOrEqual(h, cone + jitter + 1e-3f, "dış halka konide");
                outerMax = Mathf.Max(outerMax, h);
            }

            Assert.Greater(outerMax, 0.40f * cone, "dış halka koninin kenarına uzanır (eşit yay, her fazda ≥ 0,56 − titreşim)");

            ClassicWeaponRuntime sg2 = Equip(ClassicWeaponId.Sg2);
            float cone2 = sg2.CurrentSpreadDegrees;
            Assert.AreEqual(0.45f * cone2, sg2.PelletRingDegrees, 1e-4f, "Gauss σ halkası");
            double sum = 0;
            const int n = 2000;
            for (int i = 0; i < n; i++)
            {
                float h = HorizontalAngle(sg2.BuildShot(Vector3.zero, aim, i % 10).Direction);
                Assert.LessOrEqual(h, cone2 + 1e-3f, "Gauss koniye kırpılır");
                sum += h * h;
            }

            float rms = Mathf.Sqrt((float)(sum / n));
            Assert.AreEqual(0.45f * cone2, rms, 0.12f * cone2, "yatay RMS ≈ σ");
        }

        private static float HorizontalAngle(Vector3 d)
        {
            return Mathf.Abs(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------------ §6 weapon-core branches

        [Test]
        public void ArmorBreakVeImpact_KademeBasina_ZirhCarpaniVeSarsma()
        {
            PrototypeConfig config = Config;
            config.Weapons.RebuildClassicView();
            var mods = new WeaponModifierState();
            var sr = new ClassicWeaponRuntime();
            sr.Equip(config.Weapons.Find(ClassicWeaponId.Sr1), mods);
            Assert.AreEqual(1.6f, sr.Launch(0).ArmorMultiplier, 1e-4f, "sr_1 kendi zırh çarpanı");
            mods.AddTier(UpgradeBranch.ArmorBreak, 2);
            Assert.AreEqual(1.6f * 1.5f, sr.Launch(0).ArmorMultiplier, 1e-4f, "ArmorBreak +%25 / kademe");

            var sgMods = new WeaponModifierState();
            var sg2 = new ClassicWeaponRuntime();
            sg2.Equip(config.Weapons.Find(ClassicWeaponId.Sg2), sgMods);
            float pellet = sg2.BuildShot(Vector3.zero, Vector3.forward, 1).ImpactForce;
            Assert.AreEqual(DamageForce.ShotgunPellet * 0.90f / 0.85f, pellet, 1e-4f, "sg_2 pellet itkisi pompalı ölçeğinde");
            sgMods.AddTier(UpgradeBranch.Impact, 1);
            Assert.AreEqual(DamageForce.ShotgunPellet * (0.90f + 0.15f) / 0.85f, sg2.BuildShot(Vector3.zero, Vector3.forward, 1).ImpactForce, 1e-4f, "Impact +0,15 / kademe");

            var srAlone = new ClassicWeaponRuntime();
            srAlone.Equip(config.Weapons.Find(ClassicWeaponId.Sr1), new WeaponModifierState());
            Assert.LessOrEqual(srAlone.BuildShot(Vector3.zero, Vector3.forward, 0).ImpactForce, DamageForce.ExplosionCore, "tek mermi patlama çekirdeğini aşmaz");
            Assert.AreEqual(PelletBranchResolution.PerPellet, WeaponBranchContract.ResolutionOf(UpgradeBranch.Impact));
        }

        // ------------------------------------------------------------------ damage multipliers (DamageHitContext)

        [Test]
        public void KafaVeZirhCarpani_SilahBaglamiyla_DogrudanVurusta()
        {
            var body = new RegionalDamageBody();
            body.Reset(200f);
            var head = new DamageHit { Amount = 10f, Region = BodyRegion.Head };
            DamageResult plain = body.ApplyDirectHit(head, null);
            Assert.AreEqual(10f, plain.HealthDamage, 1e-4f, "bağlam yok → kural");

            body.Reset(200f);
            DamageHitContext.Set(1.25f, 1.6f, true, ClassicWeaponId.Sr1, null);
            DamageResult sniper = body.ApplyDirectHit(head, null);
            Assert.AreEqual(12.5f, sniper.HealthDamage, 1e-4f, "kafa ek çarpanı ×1,25");
            var torso = new DamageHit { Amount = 10f, Region = BodyRegion.UpperBody };
            Assert.AreEqual(10f, body.ApplyDirectHit(torso, null).HealthDamage, 1e-4f, "gövdeye kafa çarpanı yok");

            body.Reset(200f);
            body.Armor.Equip(ArmorSlot.Helmet, 50f);
            DamageResult plate = body.ApplyDirectHit(head, null);
            Assert.IsTrue(plate.WasArmored);
            Assert.AreEqual(16f, plate.ArmorDamage, 1e-4f, "zırh çarpanı ×1,6");
            Assert.AreEqual(0f, plate.HealthDamage, "zırh arkasına geçmez");

            DamageHitContext.Clear();
            Assert.AreEqual(10f, body.ApplyDirectHit(head, null).ArmorDamage, 1e-4f, "Clear → kural");
            Assert.AreEqual(1f, DamageHitContext.HeadMultiplier);
            Assert.IsFalse(DamageHitContext.FromClassicWeapon);
        }
    }
}
