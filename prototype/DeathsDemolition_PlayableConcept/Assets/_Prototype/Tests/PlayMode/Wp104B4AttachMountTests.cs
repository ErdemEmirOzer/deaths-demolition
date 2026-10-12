using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vfx = DD.Prototype.Feedback.Vfx;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §6 PlayMode) · the AP-040 weapon attachments on the classic weapon in hand, on the
    /// real run path (SpecialWeaponSahne: the Run scene's skinned hero and WeaponController), top-down, game frames (1/60).
    ///   1. ar_1 E2-M + E3 + E5 + E4: the add-ons sit on their sockets (table pose, ≤ 1 mm) while firing; the E-mag rides part_mag to the
    ///      hand on reload and comes home; part_mag is hidden under it; the add-ons join the model's LOD0;
    ///   2. a weapon switch drops the old add-ons and mounts the new weapon's (smg_2's side magazine: the E-mag points +X);
    ///   3. rl_1 full auto: the E6 belt box, the reload's Box visual, unit scale on its socket;
    ///   4. TopDown only (C-00 Q1): third person hides the add-ons and gives part_mag back; neutral stats mount nothing;
    ///   5. a measurement (never red but for missing setup): the hands' distance to every weapon's E2-L / E6;
    ///   7. the E4 / E5 glow (AD-B4 M9): slot 1 renders a block's emission (URP Lit, _EMISSION, map, own submesh), the block reaches
    ///      the draw (FlashScale 0 → 1 lifts the slot's own pixels), and the E4 window shows from the game's TD rig — straight down it
    ///      does not (AD-B4 §2/13's frame), which is logged.
    /// Tree stats come from TreeRuntime.SetTestStats. Every test is ignored while DD_WeaponAttach is not built (the builder waits for
    /// S-02's shared AP-040 materials).
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4AttachMountTests
    {
        private const float KonumTolMm = 1f;
        private const float AciTol = 0.1f;   // degrees

        private SpecialWeaponSahne _s;
        private WeaponAttachLibrary _lib;
        private WeaponAttachPresenter _sunucu;
        private ClassicWeaponId _oncekiSecim;

        [SetUp]
        public void SeciliSilahiSakla() => _oncekiSecim = WeaponRegistry.SelectedWeapon;   // EquipClassic writes it process-wide

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            TreeRuntime.ClearTestStats();
            if (_s != null) TreeEvents.RaiseStatsChanged();
            if (CameraModeService.Current != CameraMode.TopDown) CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 0f;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            _sunucu = null;
            _lib = null;
            WeaponRegistry.SelectedWeapon = _oncekiSecim;
        }

        // ------------------------------------------------------------------ 1

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Ek_EldekiSilahi_AtesVeReloaddaIzler()
        {
            KutuphaneGerekli();
            yield return Kur();
            yield return Hazirla(ClassicWeaponId.AssaultRifle, E5Istatistik());
            WeaponAttachMount m = Montaj(ClassicWeaponId.AssaultRifle);
            Transform kok = m.transform;
            Assert.IsTrue(m.Row.magOnPart, "ar_1: magOnPart bekleniyor (AP-021 part_mag)");
            EkBekle(m.MagAttachment, Yuva(_lib.e2, 1), "ar_1 E2-M şarjör");
            EkBekle(m.TwinAttachment, Yuva(_lib.e3, WeaponAttachRules.TwinSize(ClassicWeaponId.AssaultRifle) - 1), "ar_1 E3 ikiz namlu");
            EkBekle(m.RingAttachment, Yuva(_lib.e5, 0), "ar_1 E5 hız halkası");
            EkBekle(m.SideAttachment, Yuva(_lib.e4, WeaponAttachRules.ElementIndex(TreeElement.Fire)), "ar_1 E4 ateş kapsülü");
            foreach (GameObject ek in Ekler(m)) Assert.IsTrue(ek.transform.IsChildOf(kok), ek.name + ": model kökünün altında değil");

            WeaponMagazineVisual v = _s.Controller.MagazineVisual;
            Transform parca = WeaponSocketContract.Find(kok, WeaponMagazineVisual.PartName);
            Assert.IsTrue(parca != null, "ar_1 modelinde part_mag yok");
            Assert.IsTrue(m.MagSocket.IsChildOf(parca), "SOCKET_Mag part_mag'in altında değil (magOnPart)");
            List<Renderer> parcaRend = ParcaRendererlari(parca, m.MagSocket);
            Assert.Greater(parcaRend.Count, 0, "part_mag'in kendi renderer'ı yok");
            GizlilikBekle(parcaRend, true, "TD, E-şarjör takılı ve görünür");

            // the kick moves the rig body every shot: the add-ons must follow their sockets, the sockets the table
            float enMm = 0f, enAci = 0f;
            int kare = 0, atis0 = _s.Controller.ShotSerial;
            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                _s.Girdi.AtesBasti = false;
                if (v.InHand) break;   // an empty magazine reloads under a held trigger: part_mag has left home
                PozDenetle(m, "atış karesi " + i, ref enMm, ref enAci);
                kare++;
            }

            _s.Girdi.AtesBasili = false;
            int atis = _s.Controller.ShotSerial - atis0;
            Assert.Greater(atis, 0, "ateş edilmedi (girdi yolu)");
            Assert.GreaterOrEqual(kare, 10, "atış sırasında poz ölçülen kare az");

            yield return SpecialWeaponSahne.Kareler(8);
            if (!_s.Controller.Classic.IsReloading)
            {
                Assert.IsTrue(_s.Controller.Classic.NeedsReload, "atış sonrası şarjör dolu — reload başlatılamaz");
                _s.Girdi.DoldurBasti = true;
                yield return null;
                _s.Girdi.DoldurBasti = false;
            }

            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 3f, "reload başlamadı");
            yield return SpecialWeaponSahne.Bekle(() => v.InHand, 10f, "şarjör parçası ele geçmedi");
            Assert.AreSame(parca, v.Part, "reload'un taşıdığı parça modelin part_mag'i değil");
            Assert.IsTrue(m.MagAttachment != null, "E-şarjör reload'da yok oldu");
            Assert.IsTrue(m.MagAttachment.transform.IsChildOf(v.Socket), "E-şarjör part_mag ile ele gitmedi (el soketi " + v.Socket.name + ")");
            float elMm = Vector3.Distance(m.MagAttachment.transform.position, m.MagSocket.position) * 1000f;
            Assert.LessOrEqual(elMm, KonumTolMm, "elde: E-şarjör SOCKET_Mag'den " + F(elMm) + " mm kaydı");
            GizlilikBekle(parcaRend, true, "elde, E-şarjör takılı ve görünür");

            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 20f, "reload bitmedi");
            yield return null;
            Assert.IsFalse(v.InHand, "reload bitti ama şarjör parçası elde");
            Assert.IsTrue(m.MagAttachment.transform.IsChildOf(kok), "reload sonrası E-şarjör model kökünün altında değil");
            PozDenetle(m, "reload sonrası", ref enMm, ref enAci);
            GizlilikBekle(parcaRend, true, "reload sonrası");

            int lod0 = Lod0Denetle(m);
            Debug.Log("[DD-TEST] wp104b4.track ar_1 frames=" + kare + " shots=" + atis + " maxMm=" + F(enMm) + " maxDeg=" + F(enAci) +
                      " set=" + SetYazi(m.Set) + " lod0Renderers=" + lod0 + " partRenderersHidden=" + parcaRend.Count);
        }

        // ------------------------------------------------------------------ 2

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahDegisimi_EskiEkGider_smg2SolYan()
        {
            KutuphaneGerekli();
            yield return Kur();
            yield return Hazirla(ClassicWeaponId.AssaultRifle, Istatistik(3f, 3, false, 0, TreeElement.None, false));
            WeaponAttachMount eski = Montaj(ClassicWeaponId.AssaultRifle);
            GameObject eskiSarjor = eski.MagAttachment;
            Assert.IsTrue(eskiSarjor != null, "ar_1: E2 şarjör takılmadı");

            // no stats re-raise here: the WeaponEquipped refresh alone has to move the add-ons to the new model
            _s.Controller.EquipClassic(ClassicWeaponId.Smg2, _s.Config);
            yield return SpecialWeaponSahne.Kareler(3);
            Transform yeniKok = ModelKoku();
            Assert.IsTrue(yeniKok != null, "smg_2: model kökü (LODGroup) bulunamadı");
            Assert.IsTrue(eskiSarjor == null || !eskiSarjor.transform.IsChildOf(yeniKok), "ar_1'in E-şarjörü smg_2'nin modelinde kaldı");
            Assert.IsTrue(eski == null, "ar_1'in montajı (eski model) yok edilmedi");

            WeaponAttachMount yeni = _sunucu.Mount;
            string bulunan = yeni != null ? yeni.transform.name + (yeni.Row != null ? " row=" + yeni.Row.key : "") : "yok";
            Assert.IsTrue(yeni != null && yeni.transform == yeniKok,
                "smg_2: presenter yeni modelin kökünde montaj kurmadı (Mount: " + bulunan + ", kök: " + yeniKok.name + ") — WeaponEquipped anında eski " +
                "model Destroy kuyruğunda hâlâ rig gövdesinde; presenter namluyu taşıyan modeli seçmeli");
            Assert.IsTrue(yeni.Row != null && yeni.Row.id == ClassicWeaponId.Smg2, "smg_2: montajın satırı smg_2 değil");
            Assert.AreSame(yeni, WeaponAttachments.Active, "WeaponAttachments.Active yeni montaj değil");
            EkBekle(yeni.MagAttachment, Yuva(_lib.e2, 1), "smg_2 E2-M şarjör");
            Assert.IsTrue(yeni.MagAttachment.transform.IsChildOf(yeniKok), "smg_2: E-şarjör yeni modelin altında değil");

            // smg_2 feeds from the left side: AP-040's table turns SOCKET_Mag −90° about Z, so the drum (hanging on its −Y) points
            // its +Y to +X. The table is the truth; the +X reading is asserted only while the table says so.
            Vector3 olculen = yeniKok.InverseTransformDirection(yeni.MagAttachment.transform.up).normalized;
            Vector3 tablo = (yeni.Row.mag.rotation * Vector3.up).normalized;
            float tabloNokta = Vector3.Dot(olculen, tablo), artiX = Vector3.Dot(olculen, Vector3.right), tabloArtiX = Vector3.Dot(tablo, Vector3.right);
            Debug.Log("[DD-TEST] wp104b4.switch smg_2 up(root)=" + V(olculen) + " table=" + V(tablo) + " dotTable=" + F(tabloNokta) + " dotPlusX=" + F(artiX) +
                      " tablePlusX=" + F(tabloArtiX) + (tabloArtiX > 0.9f ? "" : " NOTE: table up is not +X — asserted against the table"));
            Assert.Greater(tabloNokta, 0.99f, "smg_2: E-şarjörün yukarısı tablonun SOCKET_Mag dönüşüyle uyuşmuyor — ölçülen " + V(olculen) + ", tablo " + V(tablo));
            if (tabloArtiX > 0.9f) Assert.Greater(artiX, 0.9f, "smg_2: E-şarjörün yukarısı model kökünde +X değil (sol yan şarjör) — " + V(olculen));
            float enMm = 0f, enAci = 0f;
            PozDenetle(yeni, "smg_2", ref enMm, ref enAci);
        }

        // ------------------------------------------------------------------ 3

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator rl1_FullAuto_E6_Oturur()
        {
            KutuphaneGerekli();
            yield return Kur();
            yield return Hazirla(ClassicWeaponId.Rl1, Istatistik(3f, 4, false, 0, TreeElement.None, true));
            WeaponAttachMount m = Montaj(ClassicWeaponId.Rl1);
            Transform kok = m.transform;
            Assert.AreEqual(5, (int)m.Set.Mag, "rl_1 + tam otomatik: Mag 5 (E6) bekleniyor — " + SetYazi(m.Set));
            EkBekle(m.MagAttachment, _lib.e6, "rl_1 E6 kutu");
            StringAssert.Contains("rocket_full_auto_box", m.MagAttachment.name, "rl_1: şarjör eki E6 kutusu değil");
            Assert.IsTrue(m.TwinAttachment == null && m.RingAttachment == null && m.SideAttachment == null, "rl_1: E6 dışında ek takılmamalı — " + SetYazi(m.Set));

            // the launcher never shows E1 / E2, whatever the magazine scale
            var yasak = new List<string>();
            if (_lib.e1 != null) yasak.Add(_lib.e1.name);
            if (_lib.e2 != null) foreach (GameObject e2 in _lib.e2) if (e2 != null) yasak.Add(e2.name);
            foreach (Transform t in kok.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsFalse(yasak.Contains(t.name), "rl_1: E1/E2 takılı — " + t.name);
            }

            Assert.AreEqual(MagazineVisualKind.Box, WeaponAttachments.MagazineVisualFor(ClassicWeaponId.Rl1), "rl_1: reload görseli E6 kutusu değil");

            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(m.MagAttachment != null, "rl_1: E6 0,3 sn sonra yok");
            Vector3 ek = m.MagAttachment.transform.lossyScale, soket = m.MagSocket.lossyScale;
            float fark = Mathf.Max(Mathf.Abs(ek.x - soket.x), Mathf.Max(Mathf.Abs(ek.y - soket.y), Mathf.Abs(ek.z - soket.z)));
            Assert.LessOrEqual(fark, 1e-4f, "rl_1: E6 ölçeği soketinkinden farklı — ek " + V(ek) + ", soket " + V(soket));
            Assert.LessOrEqual(Vector3.Distance(m.MagAttachment.transform.localScale, Vector3.one), 1e-4f, "rl_1: E6 yerel ölçeği 1 değil");
            float enMm = 0f, enAci = 0f;
            PozDenetle(m, "rl_1", ref enMm, ref enAci);
            Assert.AreEqual(MagazineVisualKind.Box, WeaponAttachments.MagazineVisualFor(ClassicWeaponId.Rl1), "rl_1: 0,3 sn sonra reload görseli kutu değil");
            Debug.Log("[DD-TEST] wp104b4.rl1 att=" + m.MagAttachment.name + " lossy=" + V(ek) + " socket=" + V(soket) + " maxMm=" + F(enMm) + " visual=" +
                      WeaponAttachments.MagazineVisualFor(ClassicWeaponId.Rl1));
        }

        // ------------------------------------------------------------------ 4

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator KameraModu_TdGosterir_FpGizler()
        {
            KutuphaneGerekli();
            yield return Kur();
            yield return Hazirla(ClassicWeaponId.AssaultRifle, E5Istatistik());
            WeaponAttachMount m = Montaj(ClassicWeaponId.AssaultRifle);
            Transform parca = WeaponSocketContract.Find(m.transform, WeaponMagazineVisual.PartName);
            Assert.IsTrue(parca != null, "ar_1 modelinde part_mag yok");
            List<Renderer> parcaRend = ParcaRendererlari(parca, m.MagSocket);
            Transform namlu = _s.Controller.Muzzle;
            Assert.AreEqual(4, Ekler(m).Count, "E5 durumu: dört ek bekleniyor — " + SetYazi(m.Set));

            GorunurlukBekle(m, parcaRend, true, "TD");
            Assert.IsTrue(m.PowerMuzzle != null, "E5 halkasında SOCKET_PowerMuzzle yok");
            Assert.AreSame(m.PowerMuzzle, WeaponAttachments.FlashOrigin(namlu), "TD + E5: namlu ateşi E5 halkasının namlusundan çıkmalı");

            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            yield return SpecialWeaponSahne.Kareler(2);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Current, "kamera üçüncü şahsa geçmedi (mod kilidi?)");
            GorunurlukBekle(m, parcaRend, false, "TP");
            Assert.AreSame(namlu, WeaponAttachments.FlashOrigin(namlu), "TP: ekler gizliyken namlu ateşi silahın kendi namlusundan");

            CameraModeService.Request(CameraMode.TopDown, instant: true);
            yield return SpecialWeaponSahne.Kareler(2);
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "kamera üstten görünüme dönmedi");
            GorunurlukBekle(m, parcaRend, true, "TD'ye dönüş");
            Assert.AreSame(m.PowerMuzzle, WeaponAttachments.FlashOrigin(namlu), "TD'ye dönüş: namlu ateşi yine E5 halkasından");
            Debug.Log("[DD-TEST] wp104b4.camera td→tp→td shown=" + m.Shown + " attachments=" + Ekler(m).Count + " partRenderers=" + parcaRend.Count);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator NeutralStats_EkYok()
        {
            KutuphaneGerekli();
            yield return Kur();
            yield return Hazirla(ClassicWeaponId.AssaultRifle, E5Istatistik());
            WeaponAttachMount m = Montaj(ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(4, Ekler(m).Count, "E5 durumu: dört ek bekleniyor — " + SetYazi(m.Set));
            var eskiAdlar = new List<string>();
            foreach (GameObject ek in Ekler(m)) eskiAdlar.Add(ek.name);
            Transform parca = WeaponSocketContract.Find(m.transform, WeaponMagazineVisual.PartName);
            Assert.IsTrue(parca != null, "ar_1 modelinde part_mag yok");
            List<Renderer> parcaRend = ParcaRendererlari(parca, m.MagSocket);

            TreeStats notr = TreeStats.Neutral;
            TreeRuntime.SetTestStats(in notr);
            TreeEvents.RaiseStatsChanged();
            yield return SpecialWeaponSahne.Kareler(2);

            WeaponAttachMount n = _sunucu.Mount;
            Assert.IsTrue(n != null, "nötr: montaj kayboldu (satır hâlâ var, yalnız ekler gitmeli)");
            Assert.IsTrue(n.Set.IsEmpty, "nötr istatistik boş set vermeli — " + SetYazi(n.Set));
            Assert.AreEqual(0, Ekler(n).Count, "nötr: ek kaldı");
            foreach (Transform t in n.transform.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsFalse(eskiAdlar.Contains(t.name), "nötr: yok edilmemiş ek — " + t.name);
            }

            GizlilikBekle(parcaRend, false, "nötr: E-şarjör yok, part_mag görünür");
            Transform namlu = _s.Controller.Muzzle;
            Assert.AreSame(namlu, WeaponAttachments.FlashOrigin(namlu), "nötr: namlu ateşi silahın kendi namlusundan");
            Debug.Log("[DD-TEST] wp104b4.neutral attachments=0 removed=" + eskiAdlar.Count + " flashOrigin=" + namlu.name);
        }

        // ------------------------------------------------------------------ 5

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ElTemasi_Olcum()
        {
            KutuphaneGerekli();
            yield return Kur();
            Sunucu();
            PlayerSkinnedVisual govde = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsTrue(govde != null, "skinned oyuncu yok — el teması ölçülemez");
            Animator anim = govde.Animator;
            Assert.IsTrue(anim != null && anim.isHuman, "oyuncunun insansı Animator'ı yok — el kemikleri okunamaz");
            Transform[] sag = Kemikler(anim, HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate);
            Transform[] sol = Kemikler(anim, HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal);
            Assert.IsTrue(sag.Length > 0 && sol.Length > 0, "el kemikleri yok (sağ " + sag.Length + ", sol " + sol.Length + ")");

            var csv = new StringBuilder("id,att,minR_cm,minL_cm,insideR,insideL\n");
            int olculen = 0;
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                WeaponAttachLibrary.Row row = _lib.Find(id);
                if (row == null)
                {
                    Debug.Log("[DD-TEST] wp104b4.hands id=" + id + " skipped=no-library-row");
                    continue;
                }

                string anahtar = string.IsNullOrEmpty(row.key) ? id.ToString() : row.key;
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(2);
                TreeStats st = Istatistik(3f, 4, false, 0, TreeElement.None, id == ClassicWeaponId.Rl1);   // E2-L; rl_1 full auto → E6
                TreeRuntime.SetTestStats(in st);
                TreeEvents.RaiseStatsChanged();   // SetTestStats raises nothing
                yield return SpecialWeaponSahne.Kareler(60);   // the switch-in clip ends: the aim / idle pose

                WeaponAttachMount m = _sunucu.Mount;
                if (m == null || m.Row == null || m.Row.id != id)
                {
                    Debug.Log("[DD-TEST] wp104b4.hands id=" + anahtar + " skipped=no-mount");
                    continue;
                }

                int ekSayisi = 0;
                foreach (GameObject ek in Ekler(m))
                {
                    float minR = EnKisa(sag, ek, out bool icR, out int meshR);
                    float minL = EnKisa(sol, ek, out bool icL, out _);
                    if (meshR == 0) continue;   // no LOD0 mesh to measure against
                    string satir = anahtar + "," + ek.name + "," + Cm(minR) + "," + Cm(minL) + "," + B(icR) + "," + B(icL);
                    csv.Append(satir).Append('\n');
                    Debug.Log("[DD-TEST] wp104b4.hands id=" + anahtar + " att=" + ek.name + " minR=" + Cm(minR) + " minL=" + Cm(minL) + " insideR=" + B(icR) +
                              " insideL=" + B(icL));
                    ekSayisi++;
                }

                if (ekSayisi > 0) olculen++;
                else Debug.Log("[DD-TEST] wp104b4.hands id=" + anahtar + " skipped=no-attachment set=" + SetYazi(m.Set));
            }

            string yol = Path.Combine(Application.temporaryCachePath, "wp104b4-hands.csv");
            File.WriteAllText(yol, csv.ToString());
            Debug.Log("[DD-TEST] wp104b4.hands weapons=" + olculen + " csv=" + yol);
            Assert.Greater(olculen, 0, "hiçbir silah ölçülmedi");
        }

        // ------------------------------------------------------------------ 6

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TdOkunurluk_PikselOlcum()
        {
            // C-00 Q4 (B4-PLAN §7, "measure first"): how many pixels each attachment covers on the game's TopDown camera at the
            // user's 1496×842 window and at 1920×1080 — the LOD0 meshes' bounds projected through the live camera (its aspect set to
            // the target's for the reading). A measurement: a log + CSV, never red but for missing setup.
            KutuphaneGerekli();
            yield return Kur();
            Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : null;
            Assert.IsTrue(cam != null, "oyun kamerası yok — piksel ölçülemez");
            var adimlar = new (string ad, TreeStats st)[]
            {
                ("E1", Istatistik(WeaponAttachRules.DefaultExtScale, 1, false, 0, TreeElement.None, false)),
                ("E2-S", Istatistik(3f, 2, false, 0, TreeElement.None, false)),
                ("E2-M", Istatistik(3f, 3, false, 0, TreeElement.None, false)),
                ("E2-L+E3+E5+E4", Istatistik(3f, 4, true, 4, TreeElement.Fire, false)),
            };

            var csv = new StringBuilder("weapon,step,att,px1496_w,px1496_h,px1920_w,px1920_h\n");
            int olculen = 0;
            foreach (ClassicWeaponId id in new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.Smg2, ClassicWeaponId.Lmg1 })
            {
                if (_lib.Find(id) == null) continue;
                _s.Controller.EquipClassic(id, _s.Config);
                yield return SpecialWeaponSahne.Kareler(30);
                foreach ((string ad, TreeStats st) in adimlar)
                {
                    TreeStats s = st;
                    TreeRuntime.SetTestStats(in s);
                    TreeEvents.RaiseStatsChanged();
                    yield return SpecialWeaponSahne.Kareler(3);
                    WeaponAttachMount m = _sunucu != null ? _sunucu.Mount : Sunucu().Mount;
                    if (m == null) continue;
                    foreach (GameObject ek in Ekler(m))
                    {
                        Vector2 a = Piksel(cam, ek, 1496, 842), b = Piksel(cam, ek, 1920, 1080);
                        if (a.x <= 0f) continue;
                        string key = _lib.Find(id).key;
                        csv.Append(key).Append(',').Append(ad).Append(',').Append(ek.name).Append(',').Append(P1(a.x)).Append(',').Append(P1(a.y))
                           .Append(',').Append(P1(b.x)).Append(',').Append(P1(b.y)).Append('\n');
                        Debug.Log("[DD-TEST] wp104b4.px weapon=" + key + " step=" + ad + " att=" + ek.name + " px1496=" + P1(a.x) + "x" + P1(a.y) +
                                  " px1920=" + P1(b.x) + "x" + P1(b.y));
                        olculen++;
                    }
                }
            }

            string yol = Path.Combine(Application.temporaryCachePath, "wp104b4-px.csv");
            File.WriteAllText(yol, csv.ToString());
            Debug.Log("[DD-TEST] wp104b4.px rows=" + olculen + " camera=" + cam.name + " fov=" + F(cam.fieldOfView) + " ortho=" + cam.orthographic + " csv=" + yol);
            Assert.Greater(olculen, 0, "hiçbir ek ölçülmedi");
        }

        /// <summary>The on-screen box (px) of the add-on's LOD0 meshes at <paramref name="w"/>×<paramref name="h"/> through
        /// <paramref name="cam"/> (the camera's aspect set to the target's for the reading, then reset to the window's).</summary>
        private static Vector2 Piksel(Camera cam, GameObject ek, int w, int h)
        {
            cam.aspect = (float)w / h;
            try
            {
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                bool any = false;
                foreach (MeshFilter f in ek.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null || AuthoredWeaponModel.LodIndexOf(f.name) > 0) continue;
                    Bounds b = f.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 v = cam.WorldToViewportPoint(f.transform.TransformPoint(c));
                        if (v.z <= 0f) continue;
                        min = Vector2.Min(min, new Vector2(v.x * w, v.y * h));
                        max = Vector2.Max(max, new Vector2(v.x * w, v.y * h));
                        any = true;
                    }
                }

                return any ? max - min : Vector2.zero;
            }
            finally
            {
                cam.ResetAspect();   // back to the window's own aspect
            }
        }

        private static string P1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ 7 · add-on glow (AD-B4 M9; §2/13 "binding fault")

        private const int IsimaKatmani = 30;    // an unnamed user layer: the glow camera sees the add-ons only
        private const int IsimaPx = 480;        // square raw render (no post, MSAA off)
        private const float M9HamEsik = .15f;   // AD-B4 M9: raw mean luminance over the add-on's glow pixels, FlashScale 0 → 1
        private const float RigE4MinPx = 4f;    // the E4 fire window from the game's TD rig at 1080p (AP-040 v001 ≈ 15 px)

        /// <summary>
        /// AD-B4 §2/13 read "peakLumOn = peakLumOff" in frames 27–29 as a binding fault (MPB slot, _EMISSION, property name). This
        /// pins each link where it lives, on S-06's mount and TreeAttachGlow, without the run scene:
        ///   1. every glow prefab (E4 fire / ice / venom, E5 speed / chaos): each renderer's slot 1 is M_AP040_Glow — URP Lit,
        ///      _EMISSION on, an emission map, its own submesh (<see cref="Vfx.TreeAttachGlow.GlowSlotRenders"/>);
        ///   2. the block reaches the draw: from the axis that shows most of the slot (raw, no post, the sun off so a lit white inlay
        ///      cannot clip first), FlashScale 0 → 1 at tier V lifts the slot's own pixels (found by an unlit black / white material
        ///      swap) by ≥ 0.15 mean luminance, E4 and E5;
        ///   3. the LateUpdate poll repaints on a FlashScale change with no event (the AD capture's two-frame wait is enough);
        ///   4. where the slot shows at the game's 1080p TD scale: the E4 fire window from the TD rig (DD_FeelConfig height / back /
        ///      yaw, aim up-screen) ≥ 4 px. Straight down — the AD frames' camera — is logged: 0 px on AP-040 v001, the window faces
        ///      the sides; that, not the binding, is why frames 27–29 did not move.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator EkIsimasi_YuvaCizer_BlokCizimeUlasir_TdRigindenGorunur()
        {
            KutuphaneGerekli();
            WeaponAttachLibrary.Row row = _lib.Find(ClassicWeaponId.AssaultRifle);
            Assert.IsTrue(row != null, "ar_1 satırı (DD_WeaponAttach)");

            // 1 · the slot contract on every glow prefab (V03 electric has no glow slot in AP-040 v001: TreeAttachGlow counts it slotless)
            int sozlesme = 0;
            foreach (GameObject p in IsimaPrefablari())
            {
                Assert.IsTrue(p != null, "DD_WeaponAttach: E4 / E5 yuvası boş");
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.IsTrue(Vfx.TreeAttachGlow.GlowSlotRenders(r), p.name + "/" + r.name + ": yuva 1 ışımayı çizemez — " + YuvaYazi(r));
                    sozlesme++;
                }
            }

            Assert.Greater(sozlesme, 0, "ışıma yuvalı renderer yok");

            float oncekiFlash = Vfx.GameFeelSettings.FlashScale;
            bool oncekiSis = RenderSettings.fog;
            RenderSettings.fog = false;
#if UNITY_EDITOR
            bool oncekiAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;   // never measure the cyan placeholder of a variant in flight
#endif
            var sahip = new List<UnityEngine.Object>();
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                var kok = new GameObject("WP104 M9 ışıma kökü") { layer = IsimaKatmani };
                sahip.Add(kok);
                kok.transform.SetPositionAndRotation(new Vector3(0f, 400f, 0f), Quaternion.identity);   // barrel +Z: aim up-screen on the rig
                Light gunes = new GameObject("WP104 M9 güneş").AddComponent<Light>();
                sahip.Add(gunes.gameObject);
                gunes.type = LightType.Directional;
                gunes.transform.rotation = Quaternion.Euler(50f, 160f, 0f);   // the AD capture's sun (V6 sky)
                gunes.color = new Color(1f, .886f, .776f);
                gunes.intensity = 2f;
                gunes.shadows = LightShadows.None;
                var isimaGo = new GameObject("WP104 M9 TreeAttachGlow");
                sahip.Add(isimaGo);
                Vfx.TreeAttachGlow glow = isimaGo.AddComponent<Vfx.TreeAttachGlow>();

                WeaponAttachMount m = WeaponAttachMount.Ensure(kok.transform, row);
                Assert.IsTrue(m != null, "ar_1 montajı (çıplak kök)");
                var setV = new WeaponAttachSet { Side = TreeElement.Fire, SideTier = 5, Ring = 1, RingTier = 5 };
                m.Apply(in setV, _lib);   // S-06 raises WeaponAttachEvents.Changed → the ladder
                Assert.AreSame(m, glow.Mount, "TreeAttachGlow S-06'nın Changed olayını almadı");
                Assert.Greater(glow.GlowRenderers, 0, "E4 ateş + E5: boyanan ışıma yuvası yok");
                Assert.AreEqual(0, glow.EmissionOffRenderers, "boyanan yuvanın malzemesinde _EMISSION kapalı — blok etkisiz kalır");
                Renderer e4 = Lod0(m.SideAttachment), e5 = Lod0(m.RingAttachment);
                Assert.IsTrue(e4 != null && e5 != null, "E4 / E5 LOD0 renderer'ı yok");

                // 3 · the poll: a setting raises no event
                int boya = glow.Applies;
                Vfx.GameFeelSettings.FlashScale = .4f;
                yield return null;
                Assert.Greater(glow.Applies, boya, "FlashScale değişince LateUpdate yoklaması yeniden boyamadı");
                Assert.AreEqual(Vfx.TreeVfxLadder.AddOnEmission(Vfx.TreeVfxLadder.AddOnGlow.Side, 5, .4f, true).r, glow.SideEmission.r, 1e-5f, "E4 V × FlashScale 0,4");

                rt = new RenderTexture(IsimaPx, IsimaPx, 24, RenderTextureFormat.ARGBHalf) { name = "WP104 M9 ışıma" };   // HDR: E5's silver sits at 0.98 under the ambient alone - an 8-bit target clipped the glow away (b4-r3)
                var oku = new Texture2D(IsimaPx, IsimaPx, TextureFormat.RGBAHalf, false);
                sahip.Add(oku);
                cam = new GameObject("WP104 M9 kamera").AddComponent<Camera>();
                sahip.Add(cam.gameObject);
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.cullingMask = 1 << IsimaKatmani;
                cam.allowMSAA = false;
                cam.allowHDR = true;
                cam.nearClipPlane = .05f;
                cam.farClipPlane = 10f;
                cam.targetTexture = rt;
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.IsTrue(unlit != null, "Universal Render Pipeline/Unlit");
                Material siyah = Duz(unlit, Color.black), beyaz = Duz(unlit, Color.white);
                sahip.Add(siyah);
                sahip.Add(beyaz);

                CameraFeel td = FeelRuntime.Current.Camera;
                Vector3 rig = Quaternion.Euler(0f, td.yawDegrees, 0f) * new Vector3(0f, td.heightMeters, -td.backDistanceMeters);
                float ppmOyun = 1080f / (2f * rig.magnitude * Mathf.Tan(td.fieldOfViewDegrees * .5f * Mathf.Deg2Rad));
                var eksen = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                string[] eksenAd = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
                var satir = new StringBuilder();
                foreach ((string ad, Renderer hedef) in new[] { ("E4 ateş", e4), ("E5 hız", e5) })
                {
                    Gizle(m, hedef);
                    Cek(cam, rt, oku);   // warm-up: the variant compiled, the first frame of the RT
                    // the binding proof without the sun: a white speed inlay lit at 2.0 already clips the LDR raw frame, which would
                    // hide a working block; the rig / top readings below keep the AD capture's sun
                    gunes.enabled = false;
                    IsimaOlcumu en = default;
                    string enAd = "-";
                    for (int k = 0; k < eksen.Length; k++)
                    {
                        IsimaOlcumu o = IsimaOlc(cam, rt, oku, hedef, kok.transform.TransformDirection(eksen[k]), siyah, beyaz, glow);
                        if (o.Px > en.Px)
                        {
                            en = o;
                            enAd = eksenAd[k];
                        }
                    }

                    gunes.enabled = true;
                    IsimaOlcumu ust = IsimaOlc(cam, rt, oku, hedef, Vector3.up, siyah, beyaz, glow);
                    IsimaOlcumu rigV = IsimaOlc(cam, rt, oku, hedef, rig, siyah, beyaz, glow);

                    // 2 · the block reaches the draw (tier V, the axis showing most of the slot)
                    Assert.GreaterOrEqual(en.Px, 50, ad + ": ışıma yuvası hiçbir eksenden görünmüyor (" + en.Px + " px)");
                    Assert.GreaterOrEqual(en.Delta, M9HamEsik, ad + " V, " + enAd + " ekseni: FlashScale 0 → 1 yuva pikselleri ort. luminans " +
                        F(en.LumOff) + " → " + F(en.LumOn) + " — blok çizime ulaşmıyor (yuva / _EMISSION / _EmissionColor)");
                    float rigPx = rigV.Px1080(ppmOyun), ustPx = ust.Px1080(ppmOyun);
                    satir.Append(' ').Append(ad.Replace(' ', '_')).Append(": best=").Append(enAd).Append(" px=").Append(en.Px).Append(" dLumV=").Append(F(en.Delta))
                        .Append(" rigPx1080=").Append(P1(rigPx)).Append(" rigDLumV=").Append(F(rigV.Delta)).Append(" topPx1080=").Append(P1(ustPx))
                        .Append(" topDLumV=").Append(F(ust.Delta)).Append(';');

                    // 4 · the E4 window faces the game's camera
                    if (hedef == e4)
                    {
                        Assert.GreaterOrEqual(rigPx, RigE4MinPx, "E4 ateş penceresi TD rig açısından " + P1(rigPx) + " px (1080p) — yuva oyun kamerasına bakmıyor");
                    }
                }

                // M9 at the AD frames' set (E4 III 1.4 · E5 V 2.0), from the rig: a measurement for the AD capture, never red here
                var setIII = new WeaponAttachSet { Side = TreeElement.Fire, SideTier = 3, Ring = 1, RingTier = 5 };
                m.Apply(in setIII, _lib);
                Gizle(m, e4);
                IsimaOlcumu iii = IsimaOlc(cam, rt, oku, e4, rig, siyah, beyaz, glow);
                Debug.Log("[DD-TEST] wp104b4.glow.m9 rig=(" + F(rig.x) + ", " + F(rig.y) + ", " + F(rig.z) + ") ppm1080=" + F(ppmOyun) + satir +
                          " E4_III_rig: px1080=" + P1(iii.Px1080(ppmOyun)) + " lumOff=" + F(iii.LumOff) + " lumOn=" + F(iii.LumOn) + " dLum=" + F(iii.Delta) +
                          " (M9 raw >= " + F(M9HamEsik) + ")" + " emissionE4=" + F(glow.SideEmission.r) + " emissionE5=" + F(glow.RingEmission.r));
            }
            finally
            {
                Vfx.GameFeelSettings.FlashScale = oncekiFlash;
                RenderSettings.fog = oncekiSis;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = oncekiAsync;
#endif
                if (cam != null) cam.targetTexture = null;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }

                for (int i = sahip.Count - 1; i >= 0; i--)
                {
                    if (sahip[i] != null) UnityEngine.Object.Destroy(sahip[i]);
                }
            }
        }

        private struct IsimaOlcumu
        {
            public int Px;                  // the glow slot's visible pixels (the unlit swap)
            public float LumOff, LumOn;     // their mean raw luminance at FlashScale 0 / 1
            public float Ppm;               // render scale (px per metre)
            public float Delta => LumOn - LumOff;

            /// <summary>The slot's pixels at the game's 1080p TD scale.</summary>
            public float Px1080(float ppmOyun) => Ppm > 0f ? Px * (ppmOyun / Ppm) * (ppmOyun / Ppm) : 0f;
        }

        /// <summary>E4 fire / ice / venom and both E5 rings — the AP-040 v001 prefabs with an M_AP040_Glow slot.</summary>
        private IEnumerable<GameObject> IsimaPrefablari()
        {
            foreach (TreeElement e in new[] { TreeElement.Fire, TreeElement.Ice, TreeElement.Venom }) yield return Yuva(_lib.e4, WeaponAttachRules.ElementIndex(e));
            for (int i = 0; i < WeaponAttachLibrary.RingVariants; i++) yield return Yuva(_lib.e5, i);
        }

        private static string YuvaYazi(Renderer r)
        {
            Material[] mats = r.sharedMaterials;
            Material g = mats.Length > Vfx.TreeAttachGlow.GlowSlot ? mats[Vfx.TreeAttachGlow.GlowSlot] : null;
            MeshFilter f = r.GetComponent<MeshFilter>();
            return "slots " + mats.Length + ", slot1 " + (g != null ? g.name + " / " + (g.shader != null ? g.shader.name : "-") + " / _EMISSION " +
                   g.IsKeywordEnabled(Vfx.TreeAttachGlow.EmissionKeyword) + " / map " + (g.HasProperty("_EmissionMap") && g.GetTexture("_EmissionMap") != null) : "yok") +
                   ", submesh " + (f != null && f.sharedMesh != null ? f.sharedMesh.subMeshCount : 0);
        }

        private static Renderer Lod0(GameObject ek)
        {
            if (ek == null) return null;
            foreach (Renderer r in ek.GetComponentsInChildren<Renderer>(true))
            {
                if (AuthoredWeaponModel.LodIndexOf(r.name) <= 0) return r;
            }

            return null;
        }

        /// <summary>Only <paramref name="hedef"/> draws: the other add-ons and every LOD1+ (a bare root has no LODGroup) off.</summary>
        private static void Gizle(WeaponAttachMount m, Renderer hedef)
        {
            foreach (GameObject ek in Ekler(m))
            {
                foreach (Renderer r in ek.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = r != hedef;
            }
        }

        private static Material Duz(Shader unlit, Color c)
        {
            var mat = new Material(unlit) { name = "WP104 M9 " + c };
            mat.SetColor("_BaseColor", c);
            return mat;
        }

        /// <summary>
        /// The slot's pixels seen from <paramref name="yon"/> (towards the camera, orthographic): an unlit white slot 1 against an
        /// unlit black one finds them (slot 0 black occludes as it draws); then FlashScale 0 and 1 through TreeAttachGlow, raw.
        /// </summary>
        private static IsimaOlcumu IsimaOlc(Camera cam, RenderTexture rt, Texture2D oku, Renderer hedef, Vector3 yon, Material siyah, Material beyaz,
                                            Vfx.TreeAttachGlow glow)
        {
            yon = yon.normalized;
            Bounds b = hedef.bounds;
            cam.orthographicSize = Mathf.Max(.02f, b.extents.magnitude * 1.1f);
            cam.transform.SetPositionAndRotation(b.center + yon * 3f, Quaternion.LookRotation(-yon, Mathf.Abs(yon.y) > .99f ? Vector3.forward : Vector3.up));
            Material[] asil = hedef.sharedMaterials;
            Color[] beyazli, siyahli;
            try
            {
                hedef.sharedMaterials = new[] { siyah, beyaz };
                beyazli = Cek(cam, rt, oku);
                hedef.sharedMaterials = new[] { siyah, siyah };
                siyahli = Cek(cam, rt, oku);
            }
            finally
            {
                hedef.sharedMaterials = asil;
            }

            Vfx.GameFeelSettings.FlashScale = 0f;
            glow.Paint();
            Color[] kapali = Cek(cam, rt, oku);
            Vfx.GameFeelSettings.FlashScale = 1f;
            glow.Paint();
            Color[] acik = Cek(cam, rt, oku);
            var o = new IsimaOlcumu { Ppm = IsimaPx / (2f * cam.orthographicSize) };
            double sOff = 0, sOn = 0;
            for (int i = 0; i < acik.Length; i++)
            {
                if (Lum(beyazli[i]) - Lum(siyahli[i]) < .5f) continue;
                o.Px++;
                sOff += Lum(kapali[i]);
                sOn += Lum(acik[i]);
            }

            if (o.Px > 0)
            {
                o.LumOff = (float)(sOff / o.Px);
                o.LumOn = (float)(sOn / o.Px);
            }

            return o;
        }

        private static Color[] Cek(Camera cam, RenderTexture rt, Texture2D oku)
        {
            cam.Render();
            RenderTexture onceki = RenderTexture.active;
            RenderTexture.active = rt;
            oku.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            oku.Apply(false);
            RenderTexture.active = onceki;
            return oku.GetPixels();   // linear, unclamped
        }

        private static float Lum(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;

        // ------------------------------------------------------------------ 8 · B12 motes, twin flash, element edge (S-06)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator B12_Zerrecik_IkizAlev_ElementKenari()
        {
            // WP-104 B4-2 B12 (C-00 12.10 (a); S-07 keeps the glow ladder): the presenter's mount carries WeaponAttachB12; element motes run
            // at E4's SOCKET_ElementVFX from III, every classic shot of a twin barrel also flashes E3 and draws the element's edge ring at
            // the flash origin (E5's PowerMuzzle), Reduce Flashes halves the motes and keeps the brightness under bloom without an event.
            KutuphaneGerekli();
            float flash0 = GameFeelSettings.FlashScale;
            yield return Kur();
            try
            {
                yield return Hazirla(ClassicWeaponId.AssaultRifle, Istatistik(3f, 3, true, 4, TreeElement.Fire, false, elementKademe: 3));
                WeaponAttachMount m = Montaj(ClassicWeaponId.AssaultRifle);
                WeaponAttachB12 b = m.GetComponent<WeaponAttachB12>();
                Assert.IsTrue(b != null, "montajda WeaponAttachB12 yok (presenter eklemeli)");
                Assert.IsTrue(m.PowerMuzzle != null && m.SecondaryMuzzle != null && m.ElementVfx != null, "E5 / E3 / E4 soketleri eksik — " + SetYazi(m.Set));

                Assert.AreEqual(4f, b.MoteRate, "ateş III: saniyede 4 zerrecik");
                Assert.AreEqual(WeaponAttachB12.IntensityFor(3, 1f), b.Intensity, 1e-5f, "III parlaklığı (M9 1,4)");
                ParticleSystem zerre = m.ElementVfx.GetComponentInChildren<ParticleSystem>(true);
                Assert.IsTrue(zerre != null && zerre.isPlaying, "E4 SOCKET_ElementVFX'te zerrecik sistemi çalışmıyor");
                Assert.AreEqual(WeaponAttachB12.FireGlow, zerre.main.startColor.color, "zerrecik ateş rengi");

                // a burst of fire: the twin flash and the edge ride every shot
                int f0 = b.SecondaryFlashes, k0 = b.EdgeBursts, atis0 = _s.Controller.ShotSerial;
                _s.Girdi.AtesBasti = true;
                _s.Girdi.AtesBasili = true;
                for (int i = 0; i < 24; i++)
                {
                    yield return null;
                    _s.Girdi.AtesBasti = false;
                }

                _s.Girdi.AtesBasili = false;
                int atis = _s.Controller.ShotSerial - atis0, ikiz = b.SecondaryFlashes - f0, kenar = b.EdgeBursts - k0;
                Assert.Greater(atis, 0, "ateş edilmedi");
                Assert.AreEqual(atis, ikiz, "ikiz namlu her atışta kendi alevini vermeli (atış " + atis + ", ikiz alev " + ikiz + ")");
                Assert.AreEqual(atis, kenar, "element kenarı her atışta (atış " + atis + ", kenar " + kenar + ")");
                // the origin, not a distance: the rig has moved on (kick, aim) since the last shot of the burst
                Assert.AreSame(m.PowerMuzzle, b.LastEdgeOrigin, "kenar halkası E5'in PowerMuzzle'ında çizilmeli (" +
                               (b.LastEdgeOrigin != null ? b.LastEdgeOrigin.name : "yok") + ")");

                // Reduce Flashes: no event, the next frame re-shapes
                GameFeelSettings.FlashScale = 0.4f;
                yield return SpecialWeaponSahne.Kareler(2);
                Assert.AreEqual(2f, b.MoteRate, "Reduce Flashes: zerrecik yarısı");
                Assert.Less(b.Intensity, 1.25f, "Reduce Flashes: bloom altında");
                GameFeelSettings.FlashScale = flash0;
                yield return SpecialWeaponSahne.Kareler(2);

                // no element: no motes, no edge, the twin flash stays
                TreeStats saf = Istatistik(3f, 3, true, 4, TreeElement.None, false);
                TreeRuntime.SetTestStats(in saf);
                TreeEvents.RaiseStatsChanged();
                yield return SpecialWeaponSahne.Kareler(2);
                Assert.AreEqual(0f, b.MoteRate, "element yok: zerrecik yok");
                int k1 = b.EdgeBursts, f1 = b.SecondaryFlashes, atis1 = _s.Controller.ShotSerial;
                _s.Girdi.AtesBasti = true;
                _s.Girdi.AtesBasili = true;
                for (int i = 0; i < 12; i++)
                {
                    yield return null;
                    _s.Girdi.AtesBasti = false;
                }

                _s.Girdi.AtesBasili = false;
                Assert.AreEqual(k1, b.EdgeBursts, "element yokken kenar çizilmemeli");
                Assert.AreEqual(_s.Controller.ShotSerial - atis1, b.SecondaryFlashes - f1, "ikiz alev elementten bağımsız");
                Debug.Log("[DD-TEST] wp104b4.b12 shots=" + atis + " twinFlashes=" + ikiz + " edges=" + kenar + " edgeOrigin=" + b.LastEdgeOrigin.name +
                          " motes=" + F(WeaponAttachB12.MoteRateFor(3, 1f)) + " intensity=" + F(WeaponAttachB12.IntensityFor(3, 1f)));
            }
            finally
            {
                GameFeelSettings.FlashScale = flash0;
            }
        }

        // ------------------------------------------------------------------ setup

        private void KutuphaneGerekli()
        {
            WeaponAttachLibrary.ResetCache();
            _lib = WeaponAttachLibrary.Current;
            if (_lib == null)
            {
                Assert.Ignore("DD_WeaponAttach henüz kurulmadı: Resources/" + WeaponAttachLibrary.ResourcePath +
                              " yok — ArtAp040WeaponAttachBuilder.Build S-02'nin ortak AP-040 malzemelerini bekliyor");
            }
        }

        private IEnumerator Kur()
        {
            Time.captureDeltaTime = 1f / 60f;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            yield return null;
        }

        /// <summary>WeaponController.Awake adds the presenter (S-02 B2); the fallback only covers a controller that skipped it.</summary>
        private WeaponAttachPresenter Sunucu()
        {
            if (_sunucu == null) _sunucu = _s.Controller.GetComponent<WeaponAttachPresenter>();
            if (_sunucu == null) _sunucu = _s.Controller.gameObject.AddComponent<WeaponAttachPresenter>();
            return _sunucu;
        }

        private IEnumerator Hazirla(ClassicWeaponId id, TreeStats stats)
        {
            Sunucu();
            _s.Controller.EquipClassic(id, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            TreeRuntime.SetTestStats(in stats);
            // SetTestStats raises nothing (the equip-time refresh alone is SilahDegisimi_EskiEkGider_smg2SolYan's)
            TreeEvents.RaiseStatsChanged();
            yield return SpecialWeaponSahne.Kareler(2);
        }

        private WeaponAttachMount Montaj(ClassicWeaponId id)
        {
            WeaponAttachMount m = _sunucu.Mount;
            Assert.IsTrue(m != null, id + ": presenter montaj kurmadı (kütüphane satırı / model kökü)");
            Assert.IsTrue(m.Row != null && m.Row.id == id, id + ": montajın satırı " + (m.Row != null ? m.Row.id.ToString() : "yok"));
            Assert.AreSame(m, WeaponAttachments.Active, id + ": WeaponAttachments.Active montaj değil");
            Assert.IsTrue(m.Shown, id + ": TD'de ekler görünür olmalı");
            return m;
        }

        /// <summary>The live classic model root (S-02 B2: the root WeaponVisualRig.SetModel installed).</summary>
        private Transform ModelKoku() => _s.Controller.ClassicModelRoot;

        private static TreeStats E5Istatistik() => Istatistik(3f, 3, true, 4, TreeElement.Fire, false);

        private static TreeStats Istatistik(float sarjorOlcek, int sarjorKademe, bool ikiz, int hizKademe, TreeElement element, bool tamOtomatik,
                                            int elementKademe = 0)
        {
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Element, elementKademe);
            s.MagazineScale = sarjorOlcek;
            s.SetBranchTier(TreeBranch.Magazine, sarjorKademe);
            s.TwinBarrel = ikiz;
            s.SetBranchTier(TreeBranch.Speed, hizKademe);
            s.Elements = element;
            s.DominantElement = element;
            s.FullAuto = tamOtomatik;
            return s;
        }

        // ------------------------------------------------------------------ checks

        private static GameObject Yuva(GameObject[] dizi, int i) => dizi != null && i >= 0 && i < dizi.Length ? dizi[i] : null;

        private static void EkBekle(GameObject ek, GameObject prefab, string at)
        {
            Assert.IsTrue(prefab != null, at + ": kütüphane yuvası boş");
            Assert.IsTrue(ek != null, at + " takılmadı");
            Assert.AreEqual(prefab.name, ek.name, at + ": yanlış prefab");
        }

        private static List<GameObject> Ekler(WeaponAttachMount m)
        {
            var l = new List<GameObject>(4);
            if (m.MagAttachment != null) l.Add(m.MagAttachment);
            if (m.TwinAttachment != null) l.Add(m.TwinAttachment);
            if (m.RingAttachment != null) l.Add(m.RingAttachment);
            if (m.SideAttachment != null) l.Add(m.SideAttachment);
            return l;
        }

        /// <summary>
        /// The sockets against the table (model-root frame; SOCKET_Mag only while part_mag is home) and every add-on against its socket:
        /// E-mag and E4 on the socket itself, E3 / E5 at the combination's offset in the muzzle socket.
        /// </summary>
        private static void PozDenetle(WeaponAttachMount m, string at, ref float enMm, ref float enAci)
        {
            Transform kok = m.transform;
            WeaponAttachLibrary.Row row = m.Row;
            SoketTablo(kok, m.MagSocket, row.mag, at + " SOCKET_Mag", ref enMm, ref enAci);
            SoketTablo(kok, m.MuzzleSocket, row.muzzle, at + " SOCKET_Muzzle", ref enMm, ref enAci);
            SoketTablo(kok, m.SideSocket, row.side, at + " SOCKET_Side", ref enMm, ref enAci);
            AttachCombo c = row.combos != null && row.combos.Length == WeaponAttachLibrary.TwinVariants * WeaponAttachLibrary.RingVariants
                ? row.combos[WeaponAttachLibrary.ComboIndex(m.Set.Twin, m.Set.Ring)] : default;
            EkSokette(m.MagAttachment, m.MagSocket, Vector3.zero, at + " Mag eki", ref enMm, ref enAci);
            EkSokette(m.TwinAttachment, m.MuzzleSocket, c.e3Offset, at + " E3", ref enMm, ref enAci);
            EkSokette(m.RingAttachment, m.MuzzleSocket, c.e5Offset, at + " E5", ref enMm, ref enAci);
            EkSokette(m.SideAttachment, m.SideSocket, Vector3.zero, at + " E4", ref enMm, ref enAci);
        }

        private static void SoketTablo(Transform kok, Transform soket, in AttachPose poz, string at, ref float enMm, ref float enAci)
        {
            Assert.IsTrue(soket != null, at + ": soket yok");
            float mm = Vector3.Distance(soket.position, kok.TransformPoint(poz.position)) * 1000f;
            float aci = Quaternion.Angle(soket.rotation, kok.rotation * poz.rotation);
            enMm = Mathf.Max(enMm, mm);
            enAci = Mathf.Max(enAci, aci);
            Assert.LessOrEqual(mm, KonumTolMm, at + ": tablodan " + F(mm) + " mm");
            Assert.LessOrEqual(aci, AciTol, at + ": tablodan " + F(aci) + "°");
        }

        private static void EkSokette(GameObject ek, Transform soket, Vector3 ofset, string at, ref float enMm, ref float enAci)
        {
            if (ek == null) return;
            Assert.AreSame(soket, ek.transform.parent, at + ": soketinin çocuğu değil");
            float mm = Vector3.Distance(ek.transform.position, soket.TransformPoint(ofset)) * 1000f;
            float aci = Quaternion.Angle(ek.transform.rotation, soket.rotation);
            enMm = Mathf.Max(enMm, mm);
            enAci = Mathf.Max(enAci, aci);
            Assert.LessOrEqual(mm, KonumTolMm, at + ": soketinden " + F(mm) + " mm");
            Assert.LessOrEqual(aci, AciTol, at + ": soketinden " + F(aci) + "°");
        }

        /// <summary>part_mag's own renderers — the E-mag hangs under it (SOCKET_Mag) and is not the original magazine.</summary>
        private static List<Renderer> ParcaRendererlari(Transform parca, Transform sarjorSoketi)
        {
            var l = new List<Renderer>();
            foreach (Renderer r in parca.GetComponentsInChildren<Renderer>(true))
            {
                if (sarjorSoketi == null || !r.transform.IsChildOf(sarjorSoketi)) l.Add(r);
            }

            return l;
        }

        private static void GizlilikBekle(List<Renderer> parca, bool gizli, string at)
        {
            foreach (Renderer r in parca)
            {
                Assert.IsTrue(r != null, at + ": part_mag renderer'ı yok oldu");
                Assert.AreEqual(gizli, r.forceRenderingOff, at + ": part_mag/" + r.name + ".forceRenderingOff");
            }
        }

        private static void GorunurlukBekle(WeaponAttachMount m, List<Renderer> parca, bool gorunur, string at)
        {
            Assert.AreEqual(gorunur, m.Shown, at + ": montaj Shown");
            foreach (GameObject ek in Ekler(m)) Assert.AreEqual(gorunur, ek.activeSelf, at + ": " + ek.name + " etkinliği");
            GizlilikBekle(parca, gorunur, at);
        }

        /// <summary>Every add-on's LOD0 renderer (a _LOD0 name, or no LOD suffix) is in the model LODGroup's LOD0.</summary>
        private static int Lod0Denetle(WeaponAttachMount m)
        {
            LODGroup g = m.GetComponent<LODGroup>();
            Assert.IsTrue(g != null, "model kökünde LODGroup yok");
            LOD[] lods = g.GetLODs();
            Assert.Greater(lods.Length, 0, "LODGroup boş");
            var lod0 = new HashSet<Renderer>();
            foreach (Renderer r in lods[0].renderers) if (r != null) lod0.Add(r);
            int sayi = 0;
            foreach (GameObject ek in Ekler(m))
            {
                foreach (Renderer r in ek.GetComponentsInChildren<Renderer>(true))
                {
                    if (AuthoredWeaponModel.LodIndexOf(r.name) > 0) continue;
                    Assert.IsTrue(lod0.Contains(r), ek.name + "/" + r.name + ": modelin LODGroup LOD0'ında değil");
                    sayi++;
                }
            }

            Assert.Greater(sayi, 0, "eklerin LOD0 renderer'ı yok");
            return sayi;
        }

        // ------------------------------------------------------------------ hand measurement

        private static Transform[] Kemikler(Animator a, params HumanBodyBones[] kemikler)
        {
            var l = new List<Transform>(kemikler.Length);
            foreach (HumanBodyBones k in kemikler)
            {
                Transform t = a.GetBoneTransform(k);
                if (t != null) l.Add(t);
            }

            return l.ToArray();
        }

        /// <summary>
        /// The shortest distance (m) from the points to the add-on's LOD0 meshes as oriented boxes: each point in the renderer's local
        /// space against MeshFilter.sharedMesh.bounds, the closest box point back in world space; 0 inside.
        /// </summary>
        private static float EnKisa(Transform[] noktalar, GameObject ek, out bool icinde, out int mesh)
        {
            icinde = false;
            mesh = 0;
            float en = float.PositiveInfinity;
            foreach (MeshFilter f in ek.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || AuthoredWeaponModel.LodIndexOf(f.name) > 0) continue;
                mesh++;
                Bounds b = f.sharedMesh.bounds;
                foreach (Transform n in noktalar)
                {
                    Vector3 yerel = f.transform.InverseTransformPoint(n.position);
                    if (b.Contains(yerel))
                    {
                        icinde = true;
                        en = 0f;
                        continue;
                    }

                    float d = Vector3.Distance(n.position, f.transform.TransformPoint(b.ClosestPoint(yerel)));
                    if (d < en) en = d;
                }
            }

            return en;
        }

        // ------------------------------------------------------------------ text

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static string Cm(float metre) => float.IsInfinity(metre) ? "-" : (metre * 100f).ToString("F1", CultureInfo.InvariantCulture);

        private static string B(bool b) => b ? "true" : "false";

        private static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        private static string SetYazi(in WeaponAttachSet s) =>
            "mag " + s.Mag + " twin " + s.Twin + " ring " + s.Ring + " side " + s.Side + " sideTier " + s.SideTier + " ringTier " + s.RingTier;
    }
}
