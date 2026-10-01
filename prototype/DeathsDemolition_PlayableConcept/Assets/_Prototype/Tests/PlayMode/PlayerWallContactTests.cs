using System;
using System.Collections;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>Test koşumu için girdi kaynağı. Cihaz yok, değerler elle sürülür.</summary>
    internal sealed class SurulenGirdi : IInputSource
    {
        public Vector2 Deger;
        public Vector2? Imlec;   // null = the screen centre (the pointer every other suite leaves where it is)

        public bool IsEnabled => true;
        public Vector2 Move => Deger;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => Imlec ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// G-14 · KUSUR 2 regresyon testi — "duvara/cisme çarpınca yapışıyor".
    ///
    /// ÖLÇÜLEN DAVRANIŞ, İDDİA EDİLEN DEĞİL
    /// ------------------------------------
    /// Kullanıcı oturumunda oyuncu bir duvara değdiği anda tamamen kilitleniyordu.
    /// Kusur 2026-08-18'de sur duvarının doğu yüzünde (x = 44 m), 60 fps sabit adımla
    /// şöyle ölçüldü:
    ///
    ///     temas konumu     x = 43.6011  ->  kapsül yüzeyi 44.051, duvarın 51 mm İÇİ
    ///     y                0.030 -> 0.380 (adım yüksekliği kadar havaya kalkıyor)
    ///     collisionFlags   Sides | Above | Below
    ///     duvar boyunca kayma      dz = 0.000 m, 60 karenin 60'ı 0.0000 m
    ///     duvardan geri çekilme    dx = 0.000 m
    ///     doğrudan cc.Move(-0.5, 0, 0) -> delta (0.0000, 0.0000, 0.0000)
    ///     kontrolcü kapat/aç bile kurtarmıyordu; yalnız kapsülü elle 20 cm dışarı
    ///     taşımak çözüyordu.
    ///
    /// Kök neden <see cref="DD.Prototype.World.MeshBuilder"/>'ın sarım yönüydü; onun
    /// nöbetçisi MeshWindingTests. Bu test kök nedeni değil, OYUNCUNUN HİSSETTİĞİ
    /// davranışı ölçer: duvara değen oyuncu duvarın içine girmemeli, havaya
    /// kalkmamalı, geri çekilebilmeli ve duvar boyunca kayabilmelidir.
    ///
    /// Eşikler ölçümden değil hareket denkleminden türetilir; her birinin yanında
    /// türetme var. Böylece eşik "geçsin diye" seçilmiş olamaz.
    /// </summary>
    [TestFixture]
    public sealed class PlayerWallContactTests
    {
        /// <summary>WorldStructures.PerimeterInner — sur duvarının iç yüzü.</summary>
        private const float DuvarIcYuzuX = 44f;

        private const float DuvarIcYuzuZ = -44f;

        /// <summary>PlayerConfigSection: MoveSpeed 6.2 m/s, Acceleration 45 m/s².</summary>
        private const float TepeHiz = 6.2f;
        private const float Ivme = 45f;

        /// <summary>
        /// GERİ ÇEKİLME EŞİĞİ.
        /// Duvara dayalıyken hız (+6.2, 0, 0); hedef (-6.2, 0, 0). Hız önce ters fren
        /// (90 m/s²) ile sökülür — sıfırı 0.069 sn'de geçer — sonra ivme (45 m/s²) ile
        /// ters yönde kurulur, 0.138 sn'de tam hıza varır. 1 sn'de kat edilen mesafe:
        ///     0.5*6.2*0.069 + 0.5*6.2*0.138 + 6.2*0.793 = 0.214 + 0.428 + 4.917 = 5.56 m
        /// Eşik bunun %45'i. Kusurlu hâlde ölçülen değer 0.000 m idi; eşik ikisinin
        /// arasında kapanmaz bir boşluk bırakır. Eşik ALT sınırdır: motor hızlandıkça
        /// ölçüm büyür, eşik güvenli tarafta kalır.
        /// </summary>
        private const float GeriEsigi = 2.5f;

        /// <summary>
        /// KAYMA EŞİĞİ.
        /// 45°'lik girdide duvara paralel bileşen 6.2 * sin45 = 4.384 m/s; hızlanma
        /// payı 4.384² / (2*45) = 0.214 m. 1 sn'de ideal 4.17 m. Eşik bunun %67'si.
        /// Kusurlu hâlde ölçülen 0.000 m idi. Bu eşik ayrıca hayalet hızın nöbetçisidir:
        /// motor duvara doğru hiç harcanmamış hızı tutarsa ölçüm 2.1 m'ye düşer
        /// (2026-08-19'da gerçekten böyle düştü, bkz. PlayerMotor.DropBlockedVelocity).
        /// </summary>
        private const float KaymaEsigi = 2.8f;

        private const float SahneKurulumSaniye = 12f;

        private SurulenGirdi _girdi;
        private PlayerRig _rig;
        private CharacterController _cc;

        /// <summary>
        /// Sur duvarının doğu yüzü. Sıra bilinçli: temas -> geri çekil -> yeniden yaklaş
        /// -> kay. Her aşama, bir öncekinin oyuncuyu bıraktığı yerden değil, TEMİZ
        /// olduğu ölçülmüş bir güzergâhtan başlar; böylece eşik dünyadaki bir çalıya
        /// takılmaya değil, duvara dair davranışa bakar.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator DuzDuvaraDegenOyuncuYapismaz()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunSahnesiniHazirla();

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] duvar.temas (dogu sur duvari, x=44, 60 fps sabit adim)");

            // ------------------------------------------------------------ 1 · temas
            yield return DuvaraSur(new Vector3(40f, 0.2f, 0f), new Vector2(1f, 0f), 90);

            Vector3 temas = _rig.transform.position;
            float girinti = temas.x + _cc.radius - DuvarIcYuzuX;

            rapor.AppendLine("  temas pos=" + temas.ToString("F4") +
                             "  kapsul yuzeyi=" + (temas.x + _cc.radius).ToString("F4") +
                             "  girinti=" + (girinti * 1000f).ToString("F1") + " mm" +
                             "  flags=" + _cc.collisionFlags);

            Assert.Less(girinti, 0.001f,
                "Oyuncu duvarin ICINE girdi (" + (girinti * 1000f).ToString("F1") +
                " mm). Temas normali ters demektir; kilitlenme bunun ardindan gelir.\n" + rapor);

            Assert.Less(temas.y, 0.15f,
                "Duvara dayanan oyuncu adim yuksekligi kadar HAVAYA KALKTI (y=" +
                temas.y.ToString("F3") + "). Duz bir duvar adim tirmanisi tetiklememeli.\n" + rapor);

            Assert.IsFalse((_cc.collisionFlags & CollisionFlags.Above) != 0,
                "Duz duvara dayanirken 'Above' temas bayragi var; kapsul geometrinin " +
                "icine sikismis demektir.\n" + rapor);

            // -------------------------------------------------------- 2 · geri çekil
            // Güzergâh biliniyor: oyuncu buraya x=40'tan geldi, dönüş yolu temiz.
            _girdi.Deger = new Vector2(-1f, 0f);
            Vector3 geriBasi = _rig.transform.position;
            yield return Kare(60);

            float geri = _rig.transform.position.x - geriBasi.x;
            rapor.AppendLine("  geri cekilme dx=" + geri.ToString("F3") +
                             " m  (esik <= -" + GeriEsigi + ", kusurlu hal 0.000)");

            Assert.Less(geri, -GeriEsigi,
                "Oyuncu duvardan GERI CEKILEMIYOR (dx=" + geri.ToString("F3") +
                " m). Kapsul geometriye kilitlenmis.\n" + rapor);

            // -------------------------------------------- 3 · yeniden yaklaş ve kay
            yield return DuvaraSur(new Vector3(40f, 0.2f, 0f), new Vector2(1f, 0f), 90);

            // C-00 01.10: the cursor leads along the wall. A fixed screen-centre pointer meets the raised aim plane on the camera's
            // side (−z) and turned the body away from this +z slide, so the D-1 backward ×0.65 slowed it (dz 3.936 → 2.674 in
            // FINAL dab326ab, the other legs unchanged). This test measures the wall, not the backward speed.
            _girdi.Deger = new Vector2(0.7071f, 0.7071f);
            KaymaYonuneNisan(Vector3.forward);
            Vector3 kaymaBasi = _rig.transform.position;
            Vector3 onceki = kaymaBasi;
            int donmusKare = 0;

            for (int i = 0; i < 60; i++)
            {
                yield return null;
                KaymaYonuneNisan(Vector3.forward);
                Vector3 simdi = _rig.transform.position;
                if (Vector3.ProjectOnPlane(simdi - onceki, Vector3.up).magnitude < 0.0005f)
                {
                    donmusKare++;
                }

                onceki = simdi;
            }

            float kayma = _rig.transform.position.z - kaymaBasi.z;
            rapor.AppendLine("  " + GeriPayi(new Vector3(0.7071f, 0f, 0.7071f)));
            _girdi.Deger = Vector2.zero;
            _girdi.Imlec = null;

            rapor.AppendLine("  duvar boyunca kayma dz=" + kayma.ToString("F3") + " m" +
                             "  donmusKare=" + donmusKare + "/60" +
                             "  (esik >= " + KaymaEsigi + ", kusurlu hal 0.000 / 60-60)");
            Debug.Log(rapor.ToString());

            Assert.Greater(kayma, KaymaEsigi,
                "Duvara dayali oyuncu duvar BOYUNCA kayamiyor (dz=" + kayma.ToString("F3") +
                " m). Ucuncu sahis nisancisinin en temel his beklentisi bu.\n" + rapor);

            Assert.LessOrEqual(donmusKare, 6,
                "Duvar boyunca kayarken " + donmusKare + "/60 kare hic hareket yok.\n" + rapor);
        }

        /// <summary>
        /// Aynı davranış başka bir eksende ve başka bir duvar parçasında. Kusur tek bir
        /// mesh'e özgü değildi (dünyadaki HER kutu ters sarımlıydı), o yüzden düzeltme
        /// de tek bir yerde doğrulanmamalı.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator BaskaEksendekiDuvardaDaKayabilir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunSahnesiniHazirla();

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] duvar.temas (guney sur duvari, z=-44)");

            yield return DuvaraSur(new Vector3(8f, 0.2f, -40f), new Vector2(0f, -1f), 90);

            Vector3 temas = _rig.transform.position;
            float girinti = DuvarIcYuzuZ - (temas.z - _cc.radius);

            rapor.AppendLine("  temas pos=" + temas.ToString("F4") +
                             "  girinti=" + (girinti * 1000f).ToString("F1") + " mm" +
                             "  flags=" + _cc.collisionFlags);

            Assert.Less(girinti, 0.001f,
                "Guney duvarinda oyuncu duvarin ICINE girdi.\n" + rapor);
            Assert.Less(temas.y, 0.15f, "Guney duvarinda oyuncu havaya kalkti.\n" + rapor);

            _girdi.Deger = new Vector2(0.7071f, -0.7071f);
            KaymaYonuneNisan(Vector3.right);   // C-00 01.10: the cursor leads along the wall (see the east wall)
            Vector3 basla = _rig.transform.position;
            Vector3 onceki = basla;
            int donmus = 0;

            for (int i = 0; i < 60; i++)
            {
                yield return null;
                KaymaYonuneNisan(Vector3.right);
                Vector3 s = _rig.transform.position;
                if (Vector3.ProjectOnPlane(s - onceki, Vector3.up).magnitude < 0.0005f)
                {
                    donmus++;
                }

                onceki = s;
            }

            float kayma = _rig.transform.position.x - basla.x;
            rapor.AppendLine("  " + GeriPayi(new Vector3(0.7071f, 0f, -0.7071f)));
            _girdi.Deger = Vector2.zero;
            _girdi.Imlec = null;

            rapor.AppendLine("  duvar boyunca kayma dx=" + kayma.ToString("F3") +
                             " m  donmusKare=" + donmus + "/60");
            Debug.Log(rapor.ToString());

            Assert.Greater(kayma, KaymaEsigi,
                "Guney duvari boyunca kayilamiyor (dx=" + kayma.ToString("F3") + " m).\n" + rapor);
            Assert.LessOrEqual(donmus, 6,
                "Guney duvari boyunca " + donmus + "/60 kare hic hareket yok.\n" + rapor);
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>The pointer 4 m ahead of the body along <paramref name="yon"/>, on the aim plane — the body faces the slide.</summary>
        private void KaymaYonuneNisan(Vector3 yon)
        {
            PrototypeCameraRig cam = PrototypeCameraRig.Active;
            Camera c = cam != null ? cam.Camera : Camera.main;
            Vector3 hedef = _rig.transform.position + yon * 4f;
            hedef.y = _rig.Aim.AimPlaneY;
            Vector3 ekran = c.WorldToScreenPoint(hedef);
            _girdi.Imlec = new Vector2(ekran.x, ekran.y);
        }

        /// <summary>Logged: the body's facing against the slide input and the D-1 backward scale it gives.</summary>
        private string GeriPayi(Vector3 girdi)
        {
            Vector3 f = _rig.transform.forward;
            return "govde yonu=" + new Vector3(f.x, 0f, f.z).normalized.ToString("F2") + " geri olcegi=" + PlayerMotor.BackwardScale(girdi, f).ToString("F3");
        }

        private IEnumerator DuvaraSur(Vector3 baslangic, Vector2 yon, int kare)
        {
            _rig.Motor.Teleport(baslangic);
            _girdi.Deger = Vector2.zero;
            yield return Kare(4);

            _girdi.Deger = yon;
            yield return Kare(kare);
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            // WP-020 (WP-039 sanity P2): the wall constants (south inner face z = −44, east wall) are the M0
            // procedural blockout's; the authored AP-005 world has water, not a wall, at z −44 — pin the source.
            System.Environment.SetEnvironmentVariable(DD.Prototype.World.WorldSourceSettings.EnvironmentVariable, "Procedural");
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < SahneKurulumSaniye)
            {
                yield return null;
            }

            _rig = PlayerRegistry.Primary;
            Assert.IsNotNull(_rig, "Run sahnesinde oyuncu dogmadi.");

            _cc = _rig.GetComponent<CharacterController>();
            Assert.IsNotNull(_cc, "Oyuncuda CharacterController yok.");

            // Ölçüm yalnız oyuncu ile dünya arasında olmalı: kalabalık oyuncuyu
            // fiziksel olarak engeller (REQ-046) ve mesafeyi kirletir.
            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            yield return null;

            // Kare hızından bağımsız, tekrarlanabilir ölçüm: adım tam 1/60 sn.
            Time.captureDeltaTime = 1f / 60f;

            _girdi = new SurulenGirdi();
            _rig.Motor.SetInputSource(_girdi);

            PrototypeCameraRig cam = PrototypeCameraRig.Active;
            Assert.IsNotNull(cam, "Kamera rig yok; hareket tabani cozulemez.");
            Assert.Greater(Vector3.Dot(cam.PlanarRight, Vector3.right), 0.99f,
                "Kamera yaw'i 0 degil; testin dunya-ekseni varsayimi gecersiz.");
        }

        private static IEnumerator Kare(int adet)
        {
            for (int i = 0; i < adet; i++)
            {
                yield return null;
            }
        }

        [TearDown]
        public void Bitir()
        {
            Time.captureDeltaTime = 0f;
            System.Environment.SetEnvironmentVariable(DD.Prototype.World.WorldSourceSettings.EnvironmentVariable, null);

            if (_rig != null && _rig.Motor != null)
            {
                _rig.Motor.SetInputSource(null);
            }

            _girdi = null;
            _rig = null;
            _cc = null;
        }
    }
}
