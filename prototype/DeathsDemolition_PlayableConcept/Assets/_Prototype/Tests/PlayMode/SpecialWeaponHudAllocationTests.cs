using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · HUD Special Weapon yolunda kare başı allocation (PB-2, AP-8). WP-005 yöntemi:
    /// yönetilen yığın deltası (<c>GC.GetTotalMemory</c>) sessiz pencerede HUD açık − HUD kapalı,
    /// pozitif kontrol ile (bilerek ayıran döngü sayacı yükseltir; Mono'da
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> hep 0). Pencerede gen0 GC koşarsa ölçüm
    /// yinelenir. Kare adımı çok küçük tutulur ki görüntülenen onda-saniye / metre değişmesin ve
    /// yalnız "kare başı" maliyet ölçülsün; eşik WP-005'in HUD eşiğidir (test parametresi).
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponHudAllocationTests
    {
        private const float Pencere = 0.5f;
        private const int OlcumKaresi = 600;
        private const float SessizAdim = 0.0005f;
        private const long EsikBayt = 128 * 1024;
        private const int CiftKaresi = 100;   // active window: 6 interleaved HUD on / off pairs of 100 frames = the old 600 per state
        private const int CiftSayisi = 6;

        private SpecialWeaponSahne _s;
        private int? _frameRateBefore;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_frameRateBefore.HasValue)
            {
                Application.targetFrameRate = _frameRateBefore.Value; // restore what the runner had (WP-052 note)
                _frameRateBefore = null;
            }
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        /// <summary>[PB-2][AP-8] Aktif (timer) penceresinde HUD'ın kare başı yığın maliyeti eşik altında (pozitif kontrol canlı); yerde (marker + blink) penceresi gözlem olarak loglanır (WP-005 canvas yüzeyi, HEAD ile aynı).</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudSpecialYolu_SessizKarede_YiginBuyumez_PozitifKontrollu()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");

            yield return SayacPozitifKontrol();

            // ---- yerde: marker (mesafe metni) + blink pip
            yield return _s.SpawnEt(Pencere);
            Time.captureDeltaTime = SessizAdim;
            long yerdeAcik = 0, yerdeKapali = 0;
            yield return AyirmaOlc(OlcumKaresi, v => yerdeAcik = v);
            hud.enabled = false;
            yield return AyirmaOlc(OlcumKaresi, v => yerdeKapali = v);
            hud.enabled = true;
            Time.captureDeltaTime = 0f;
            long yerdeFark = yerdeAcik - yerdeKapali;
            Debug.Log("[DD-TEST] wp007.perf groundHudOn=" + yerdeAcik + " groundHudOff=" + yerdeKapali + " delta=" + yerdeFark +
                      " (B/frame≈" + (yerdeFark / (float)OlcumKaresi).ToString("0.0") + ")");
            Assert.AreEqual(SpecialWeaponPhase.OnGround, WeaponEvents.Special.Phase, "Öncül: pencere boyunca yerde kalmadı.");

            // ---- aktif: timer metni + bar
            _s.PickupaGit(_s.CanliPickup());
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
            Time.captureDeltaTime = SessizAdim;
            long aktifAcik = 0, aktifKapali = 0, aktifFark = 0;
            yield return HudFarkiOlc(hud, CiftKaresi, CiftSayisi, (acik, kapali, fark) => { aktifAcik = acik; aktifKapali = kapali; aktifFark = fark; });
            Time.captureDeltaTime = 0f;
            Debug.Log("[DD-TEST] wp007.perf activeHudOn=" + aktifAcik + " activeHudOff=" + aktifKapali + " delta=" + aktifFark +
                      " (median pair, B/frame≈" + (aktifFark / (float)OlcumKaresi).ToString("0.0") + ")");
            Assert.IsTrue(_s.Controller.SpecialWeaponActive, "Öncül: pencere boyunca aktif kalmadı.");

            // PB-2'nin hedefi (aktif timer metni) aktif pencerede ölçülür ve eşiklenir - iç içe pencerelerin medyanıyla (S-09 01.10).
            Assert.IsTrue(aktifAcik >= 0 && aktifKapali >= 0, "Aktif pencerede gen0 GC ölçümü bozdu — ölçüm yok.");
            Assert.LessOrEqual(aktifFark, EsikBayt, "Aktif penceresinde HUD yığını eşikten fazla büyüttü (timer metni kare başı geri geldi?).");

            // GÖZLEM (eşik yok): yerde penceresi, WP-005'ten gelen marker/pip animasyonunun canvas
            // yeniden kurulum maliyetini taşır; WP-007 öncesi (HEAD RunHudScreen) ile birebir aynı
            // ölçüldü (evidence/iter7b-headhud: 37 384 192 B / 600 kare her iki sürümde) — WP-007
            // yalnız kare başı mesafe metnini kaldırdı; canvas mimarisi S-09 alanı, M4 devri.
            Debug.Log("[DD-TEST] wp007.perf groundWindowObservation deltaB=" + yerdeFark + " (marker/pip canvas rebuild; WP-005 yüzeyi, HEAD ile aynı)");
        }

        /// <summary>
        /// HUD on vs off as interleaved windows - ABBA… over <paramref name="ciftler"/> pairs of <paramref name="kare"/> frames.
        /// The frame recorder is process-wide, and a ~200 KB allocation burst from outside the HUD lands in whichever window
        /// it meets: back-to-back 600-frame windows on the same code read +197 / −1 / +194 / −204 KB (S-09 evidence b12,
        /// 25.09; FINAL A1 +220 KB, 01.10). The median of the pair deltas, scaled to ciftler × kare frames (the old
        /// 600-frame window), keeps one burst from deciding the verdict; the threshold is unchanged (H5: the method, not the
        /// expectation). <paramref name="sonuc"/> gets (on total, off total, median delta × pairs), or -1s when a window had no
        /// measurement.
        /// </summary>
        private static IEnumerator HudFarkiOlc(RunHudScreen hud, int kare, int ciftler, Action<long, long, long> sonuc)
        {
            var farklar = new List<long>(ciftler);
            long acikToplam = 0, kapaliToplam = 0;
            for (int c = 0; c < ciftler; c++)
            {
                bool acikOnce = c % 2 == 0;   // ABBA: an allocation trend over the pairs cancels instead of favouring one side
                long acik = -1, kapali = -1;
                for (int k = 0; k < 2; k++)
                {
                    bool acikPencere = (k == 0) == acikOnce;
                    hud.enabled = acikPencere;   // OnEnable / OnDisable cost falls in AyirmaOlc's uncounted warm-up
                    long v = -1;
                    yield return AyirmaOlc(kare, x => v = x);
                    if (acikPencere) acik = v; else kapali = v;
                }

                hud.enabled = true;
                if (acik < 0 || kapali < 0)
                {
                    sonuc(-1, -1, -1);
                    yield break;
                }

                acikToplam += acik;
                kapaliToplam += kapali;
                farklar.Add(acik - kapali);
            }

            var sirali = new List<long>(farklar);
            sirali.Sort();
            int m = sirali.Count / 2;
            long medyan = sirali.Count % 2 == 1 ? sirali[m] : (sirali[m - 1] + sirali[m]) / 2;
            Debug.Log("[DD-TEST] wp007.perf activePairDeltas=" + string.Join(",", farklar) + " median=" + medyan + " frames/window=" + kare);
            sonuc(acikToplam, kapaliToplam, medyan * ciftler);
        }

        private static IEnumerator SayacPozitifKontrol()
        {
            long once = GC.GetTotalMemory(false);
            var yem = new object[4000];
            for (int i = 0; i < yem.Length; i++)
            {
                yem[i] = new byte[64];
            }

            long fark = GC.GetTotalMemory(false) - once;
            Debug.Log("[DD-TEST] wp007.perf positiveControl allocB=" + fark + " keep=" + yem.Length);
            Assert.Greater(fark, 0, "Yığın sayacı bilerek ayıran döngüde yükselmedi — ölçüm geçersiz.");
            yield return null;
        }


        /// <summary>
        /// WP-022C (C-00 / WP-037 bulgusu "yerde marker/pip ~206 ayırma/kare"): yer fazı
        /// kare başı ayırma SAYISI Profiler sayacıyla (ADR-037-1 yöntemi). Spawn'ın hemen
        /// ardındaki kareler (banner metni ilk kez üretilir, TMP/Canvas tek seferlik) gözlem
        /// olarak loglanır; sözleşme sabit kare içindir: HUD açık sabit kare − HUD kapalı
        /// sabit kare ≤ harness payı. Pozitif kontrol: bilerek ayıran kare sayacı yükseltir.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudSpecialYerFazi_SabitKare_SifirAyirma_Sayac()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");

            // Recorder order and control size mirror WP-037 (HotPathAllocationContractTests):
            // bytes first, then count; 300 x 80 B in one frame must read > 250.
            // WP-052 note: the Editor runner reports LastValue 0 while targetFrameRate is 60
            // (an earlier fixture leaves it there) - the batch-order "counter 0" of WP-022C/F.
            _frameRateBefore = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "Profiler kare sayaçları yok.");

                // Pozitif kontrol (bir kare gecikmeli okumayı da dener: sayaç önceki kareyi verir).
                long kontrol = 0;
                for (int deneme = 0; deneme < 3 && kontrol <= 250; deneme++)
                {
                    var yem = new object[300];
                    for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                    yield return null;
                    kontrol = count.LastValue;
                    long kontrolB = bytes.LastValue;
                    yield return null;
                    if (count.LastValue > kontrol) { kontrol = count.LastValue; kontrolB = bytes.LastValue; }
                    Debug.Log("[DD-TEST] wp022c.specialGround control attempt=" + deneme + " count=" + kontrol + " bytes=" + kontrolB + " keep=" + yem.Length);
                    GC.KeepAlive(yem);
                }

                Assert.Greater(kontrol, 250, "Sayaç bilerek ayıran kareyi görmedi — ölçüm geçersiz (" + kontrol + ").");

                yield return _s.SpawnEt(Pencere);
                yield return null;
                long spawn1 = count.LastValue, spawn1B = bytes.LastValue;
                yield return null;
                long spawn2 = count.LastValue, spawn2B = bytes.LastValue;

                // Sabit kare: banner ve ilk kurulum geçtikten sonra (30 kare), 20 karelik en yüksek değer.
                for (int i = 0; i < 30; i++) yield return null;
                long acikMax = 0, acikB = 0;
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    if (count.LastValue > acikMax) { acikMax = count.LastValue; acikB = bytes.LastValue; }
                }

                Assert.AreEqual(SpecialWeaponPhase.OnGround, WeaponEvents.Special.Phase, "Öncül: pencere boyunca yerde kalmadı.");

                hud.enabled = false;
                for (int i = 0; i < 5; i++) yield return null;
                long kapaliMax = 0;
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    if (count.LastValue > kapaliMax) kapaliMax = count.LastValue;
                }

                hud.enabled = true;
                yield return null;

                Debug.Log("[DD-TEST] wp022c.specialGround spawnFrame1=" + spawn1 + "/" + spawn1B + "B spawnFrame2=" + spawn2 + "/" + spawn2B +
                          "B steadyHudOnMax=" + acikMax + "/" + acikB + "B steadyHudOffMax=" + kapaliMax + " control=" + kontrol);
                Assert.LessOrEqual(acikMax, kapaliMax + HarnessAllocationSlack,
                    "Yer fazı sabit karesinde HUD ayırma yapıyor: " + acikMax + " (HUD kapalı " + kapaliMax + ").");
            }
        }

        /// <summary>
        /// WP-022I · enemy status markers (M4-R1 N19 v2): a shocked zombie gets a "SHOCK" stamp over its head, a
        /// knocked-down one "DOWN"; the marker layer adds no allocation to a steady frame
        /// (ProfilerRecorder count, HUD on vs off) and the stamps park when the state ends.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator DusmanDurumIsaretleri_ShockVeDown_GorunurVeSifirAyirma()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");
            Assert.IsNotNull(hud.EnemyStatus, "durum işareti katmanı");

            Vector3 merkez = _s.Zemin(0f, 6f);
            DD.Prototype.Enemies.ZombieEnemy a = _s.Dogur(merkez + Vector3.left);
            DD.Prototype.Enemies.ZombieEnemy b = _s.Dogur(merkez + Vector3.right);
            yield return null;
            yield return null;

            bool shocked = a.ApplyShock(8101, 3f);
            bool downed = b.ApplyKnockdown(8102, 2f);
            Assert.IsTrue(shocked && downed, "durumlar uygulanmadı (şok " + shocked + ", düşürme " + downed + ")");
            yield return null;
            yield return null;
            Assert.IsTrue(a.IsElectrocuted, "A şokta");
            Assert.GreaterOrEqual(hud.EnemyStatus.VisibleCount, 1, "en az bir işaret (kamera görüyorsa iki)");

            var labels = new System.Collections.Generic.List<string>();
            foreach (TMPro.TMP_Text t in hud.EnemyStatus.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (((RectTransform)t.transform.parent).anchoredPosition.x > -9000f) labels.Add(t.text);
            }

            Assert.IsTrue(labels.Contains("SHOCK") || labels.Contains("DOWN"), "görünür damga metni SHOCK/DOWN: " + string.Join(",", labels));
            // WP-068 HUD budget: an unbound chip parks at group alpha 0, so it is not drawn off-screen.
            foreach (CanvasGroup group in hud.EnemyStatus.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (((RectTransform)group.transform).anchoredPosition.x < -9000f)
                {
                    Assert.AreEqual(0f, group.alpha, 0.001f, "park edilmiş durum çipi çizilmemeli (grup α 0): " + group.name);
                }
            }

            // WP-068 8c · E19: the stamp carries the 24 px outer-only silhouette (G1 Status.*Outer), never colour alone.
            foreach (UnityEngine.UI.Image icon in hud.EnemyStatus.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (icon.name != "Icon" || ((RectTransform)icon.transform.parent).anchoredPosition.x < -9000f) continue;
                Assert.IsTrue(icon.sprite != null && icon.sprite.name.EndsWith("Outer"), "durum damgası dış hat glifi: " + (icon.sprite != null ? icon.sprite.name : "yok"));
                Assert.AreEqual(DD.Prototype.UI.UiEnemyStatusMarkers.GlyphSize, ((RectTransform)icon.transform).sizeDelta.x, 0.01f);
            }

            _frameRateBefore = Application.targetFrameRate;
            Application.targetFrameRate = -1; // WP-052 note
            using (var count = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                long onMax = 0;
                for (int i = 0; i < 10; i++) { yield return null; if (count.LastValue > onMax) onMax = count.LastValue; }
                hud.EnemyStatus.enabled = false;
                for (int i = 0; i < 3; i++) yield return null;
                long offMax = 0;
                for (int i = 0; i < 10; i++) { yield return null; if (count.LastValue > offMax) offMax = count.LastValue; }
                hud.EnemyStatus.enabled = true;
                Debug.Log("[DD-TEST] wp022i.statusMarkers visible=" + hud.EnemyStatus.VisibleCount + " onMax=" + onMax + " offMax=" + offMax);
                if (onMax > 0 || offMax > 0)
                {
                    Assert.LessOrEqual(onMax, offMax + HarnessAllocationSlack, "durum işaretleri kare başı ayırma yapıyor: " + onMax + " (kapalı " + offMax + ")");
                }
            }

            // Both end by themselves: the shock after its hold, the knockdown after the fall, the lie and the get-up.
            float t0 = Time.realtimeSinceStartup;
            while ((a.IsElectrocuted || b.Gel != DD.Prototype.Enemies.GelState.None) && Time.realtimeSinceStartup - t0 < 8f)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.4f);   // the stamp's exit fade
            Assert.IsFalse(a.IsElectrocuted);
            Assert.AreEqual(0, hud.EnemyStatus.VisibleCount, "durum bitince işaretler park eder");
        }

        /// <summary>Harness'ın (coroutine adımı, recorder) bir kareye ekleyebileceği ayırma sayısı (WP-037 ile aynı pay).</summary>
        private const int HarnessAllocationSlack = 8;

        /// <summary>
        /// WP-022K · arbiter method (WP-044 REPORT §ölçüm, WP-052): the window's cost is the
        /// ProfilerRecorder "GC Allocated In Frame" SUM when the recorder is live (a 64-object
        /// probe frame must read > 0), else the managed-heap delta of the old method. The heap
        /// delta grows in 64 KB steps and drifts with what earlier fixtures left on the heap
        /// (batch-order FAILs of WP-022C/F/J with the recorder reading a clean HUD), so it is
        /// the fallback, logged next to the recorder number when both exist.
        /// </summary>
        private static IEnumerator AyirmaOlc(int kare, Action<long> sonuc)
        {
            int frameRateBefore = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    for (int i = 0; i < 10; i++) yield return null; // ısınma

                    // Recorder probe: one frame with 64 small objects must register.
                    var yem = new object[64];
                    for (int i = 0; i < yem.Length; i++) yem[i] = new byte[32];
                    yield return null;
                    bool recorderLive = bytes.Valid && bytes.LastValue > 0;
                    GC.KeepAlive(yem);
                    yield return null;

                    for (int deneme = 0; deneme < 2; deneme++)
                    {
                        long c0 = GC.CollectionCount(0);
                        long once = GC.GetTotalMemory(false);
                        long recorded = 0;
                        for (int i = 0; i < kare; i++)
                        {
                            yield return null;
                            if (recorderLive) recorded += bytes.LastValue;
                        }

                        long fark = GC.GetTotalMemory(false) - once;
                        if (recorderLive)
                        {
                            Debug.Log("[DD-TEST] alloc window frames=" + kare + " recorderB=" + recorded + " heapDeltaB=" + fark + (GC.CollectionCount(0) != c0 ? " (gc ran)" : ""));
                            sonuc(recorded);
                            yield break;
                        }

                        if (GC.CollectionCount(0) == c0)
                        {
                            sonuc(fark);
                            yield break;
                        }

                        Debug.Log("[DD-TEST] perf window gc-interrupted, retrying");
                    }

                    sonuc(-1);
                }
            }
            finally
            {
                Application.targetFrameRate = frameRateBefore;
            }
        }
    }
}
