using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Difficulty;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R4 FINAL tek ses kapısının dinleme malzemesi (C-00 12.10; ayrı dinleme koşusu yok): gerçek director, oyunun karışımı (müzik
    /// sakin), dinleyicinin çıkışı AudioOutputCapture ile segment başına bir WAV. Her olayın kayıttaki zamanı R4-DINLEME.txt'de.
    /// Segmentler: 1 wall buy (istem, satın alma, roketatar, yenileme, red) · 2 mod seçimi (Hard, Demolition, Normal'e dönüş) ·
    /// 3 ağaç ekranı (damga, sinerji, aura) · 4 ağaç savaş anları (sesli her TreeProcKind) · 5 bölgeler ve kara delik ·
    /// 6 isabet v025 (yüzey başına üç kalibre, setin destesi bir tur) · 7 keskin nişancı katmanı (LMG2 / sr_1 sırayla, aynı Heavy).
    /// Çıktı: temporaryCachePath/wp097-r4-listening. Explicit: kanıt üretir, CI'da koşmaz.
    /// </summary>
    [TestFixture]
    public sealed class R4DinlemePaketiTests
    {
        private SpecialWeaponSahne _s;
        private PrototypeAudioDirector _d;
        private WallBuyDirector _wallBuy;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _d = PrototypeAudioDirector.EnsureExists();
            yield return SpecialWeaponSahne.Bekle(() => _d.Library.PrewarmComplete, 30f, "Ses kütüphanesi prewarm bitmedi.");
            yield return null;
            _wallBuy = WallBuyDirector.Instance;
            if (_wallBuy != null) _wallBuy.enabled = false;   // the events come from the test, never from a station at spawn
            WallBuyEvents.ClearState();
            Time.captureDeltaTime = 0f;
            _d.Tree.ResetRun();
            _d.Tree.ResetGatesForTests();
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            WallBuyEvents.ClearState();
            TreeRuntime.ClearTestStats();
            if (_d != null)
            {
                if (!_d.enabled) _d.enabled = true;
                _d.Tree.StopAll();
            }

            if (_wallBuy != null) _wallBuy.enabled = true;
            yield return _s.Bitir();
        }

        private Vector3 Konum(float dx, float dz) => _d.Listener.Transform.position + new Vector3(dx, 0f, dz);

        private static TreeProcInfo Proc(TreeProcKind kind, Vector3 at, float seconds = 0f)
        {
            return new TreeProcInfo { Kind = kind, Point = at, Radius = 2f, Tier = 3, Seconds = seconds };
        }

        [UnityTest]
        [Explicit("R4 FINAL tek ses kapısı malzemesi — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator DinlemePaketi_R4_Uret()
        {
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp097-r4-listening");
            System.IO.Directory.CreateDirectory(dir);
            var isaret = new System.Text.StringBuilder("# file\tseconds\tevent (R4 FINAL single sound gate, S-08 12.10)\n");
            MusicDirector m = _d.Music;
            int? oncekiTehdit = m.ThreatLayerOverride;
            float? oncekiYogunluk = m.IntensityOverride;
            DifficultyMode oncekiMod = DifficultyService.Selected;
            _s.Rig.Health.DiagnosisInvulnerable = true;
            AudioOutputCapture cap = null;
            string dosya = null;
            int rate = AudioSettings.outputSampleRate;
            int yazilan = 0;

            void Isaret(string olay)
            {
                isaret.Append(dosya).Append('\t').Append((cap != null ? cap.FramesCaptured / (double)rate : 0).ToString("0.000"))
                    .Append('\t').Append(olay).Append('\n');
            }

            // One segment: arm, run the script (yields), let the last sound ring out, disarm.
            IEnumerator Kaydet(string ad, IEnumerator senaryo, float kuyruk)
            {
                dosya = ad + ".wav";
                cap = AudioOutputCapture.Arm(_d.gameObject, System.IO.Path.Combine(dir, dosya));
                yield return new WaitForSecondsRealtime(0.5f);
                yield return senaryo;
                yield return new WaitForSecondsRealtime(kuyruk);
                long frames = cap.FramesWritten;
                Object.DestroyImmediate(cap);
                foreach (var tap in Object.FindObjectsOfType<AudioOutputTap>(true)) Object.DestroyImmediate(tap);
                cap = null;
                if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                yazilan++;
            }

            IEnumerator Bekle(float s) { yield return new WaitForSecondsRealtime(s); }

            IEnumerator WallBuy()
            {
                Vector3 at = Konum(1.4f, 1.1f);
                var istem = new WallBuyPrompt("r4_listen", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 900, WallBuyBlock.None), at);
                Isaret("PromptChanged on (WallBuyPrompt)"); WallBuyEvents.PublishPrompt(istem);
                yield return Bekle(1.2f);
                Isaret("Resolved Bought smg (WallBuyCoins + WallBuyRack)");
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Bought, WallBuyBlock.None, "r4_listen", ClassicWeaponId.AutoSmg, 900, at));
                WallBuyEvents.PublishPrompt(default);
                yield return Bekle(1.8f);
                Isaret("Resolved Bought rl_1 (WallBuyCoins + WallBuyHeavy)");
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Bought, WallBuyBlock.None, "r4_listen_rl", ClassicWeaponId.Rl1, 2500, at));
                yield return Bekle(1.8f);
                Isaret("Resolved Refilled (WallBuyCoins)");
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Refilled, WallBuyBlock.None, "r4_listen", ClassicWeaponId.AutoSmg, 450, at));
                yield return Bekle(1.4f);
                Isaret("Resolved Denied NotEnoughGold (WallBuyDenied + UiDenied)");
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Denied, WallBuyBlock.NotEnoughGold, "r4_listen", ClassicWeaponId.AutoSmg, 900, at));
            }

            IEnumerator ModSecimi()
            {
                DifficultyService.Selected = DifficultyMode.Normal;
                yield return Bekle(0.3f);
                Isaret("SelectedChanged Hard (ModeSelectHard)"); DifficultyService.Selected = DifficultyMode.Hard;
                yield return Bekle(2.6f);
                Isaret("SelectedChanged Demolition (ModeSelectDemolition)"); DifficultyService.Selected = DifficultyMode.Demolition;
                yield return Bekle(5.2f);
                Isaret("SelectedChanged Hard, then Normal 0.8 s later: the ringing stinger fades in 150 ms"); DifficultyService.Selected = DifficultyMode.Hard;
                yield return Bekle(0.8f);
                DifficultyService.Selected = DifficultyMode.Normal;
            }

            IEnumerator AgacEkrani()
            {
                for (int i = 0; i < 3; i++)
                {
                    Isaret("NodePurchased (TreeStamp)"); TreeEvents.RaiseNodePurchased("sh_double", TreeBranch.Shot, 1 + i);
                    yield return Bekle(0.7f);
                }

                Isaret("SynergyUnlocked (TreeSynergy)"); TreeEvents.RaiseSynergyUnlocked(TreeSynergy.PinballStorm);
                yield return Bekle(1.6f);
                Isaret("AuraChanged 1 (TreeAuraUp)"); TreeEvents.RaiseAuraChanged(1);
                yield return Bekle(1.5f);
                Isaret("AuraChanged 2 (TreeAuraUp)"); TreeEvents.RaiseAuraChanged(2);
            }

            IEnumerator AgacSavas()
            {
                var anlar = new (TreeProcKind kind, float dx, float dz, float sonra)[]
                {
                    (TreeProcKind.Crit, 3f, 2f, 0.5f), (TreeProcKind.CritRing, -3f, 2f, 0.6f), (TreeProcKind.Leech, 0f, 1f, 0.6f),
                    (TreeProcKind.Dodge, 0f, 0f, 0.7f), (TreeProcKind.Ignite, 4f, 1f, 0.7f), (TreeProcKind.Freeze, -4f, 1f, 0.8f),
                    (TreeProcKind.Shatter, -4f, 1f, 0.8f), (TreeProcKind.ShockChain, 5f, 4f, 1.0f), (TreeProcKind.Ricochet, 3f, 5f, 0.5f),
                    (TreeProcKind.Ricochet, 6f, 3f, 0.7f), (TreeProcKind.BarrageRocket, 1f, 1f, 0.2f), (TreeProcKind.BarrageRocket, 1f, 1f, 0.2f),
                    (TreeProcKind.BarrageRocket, 1f, 1f, 0.8f), (TreeProcKind.ShieldUp, 0f, 0f, 1.0f), (TreeProcKind.ShieldHit, 0f, 0f, 0.5f),
                    (TreeProcKind.ShieldHit, 0f, 0f, 0.8f), (TreeProcKind.ShieldBreak, 0f, 0f, 1.2f), (TreeProcKind.UndyingOath, 0f, 0f, 2.0f),
                    (TreeProcKind.EndlessWindowOpen, 0f, 0f, 1.6f), (TreeProcKind.ElementNova, 6f, 6f, 2.5f), (TreeProcKind.MiniNuke, 8f, 6f, 3.0f),
                    (TreeProcKind.DoomsdayBell, 10f, 8f, 0f),
                };
                foreach (var (kind, dx, dz, sonra) in anlar)
                {
                    Isaret("Proc " + kind + " (" + TreeAudio.SoundFor(kind) + ")");
                    TreeEvents.RaiseProc(Proc(kind, Konum(dx, dz)));
                    yield return Bekle(sonra);
                }
            }

            IEnumerator Alanlar()
            {
                Isaret("Proc FireGround 4 s (TreeFireLoop)"); TreeEvents.RaiseProc(Proc(TreeProcKind.FireGround, Konum(4f, 0f), 4f));
                yield return Bekle(1.0f);
                Isaret("Proc VenomCloud 4 s (TreePoisonLoop)"); TreeEvents.RaiseProc(Proc(TreeProcKind.VenomCloud, Konum(-4f, 3f), 4f));
                yield return Bekle(4.5f);
                Isaret("Proc BlackHoleOpen 2.5 s (TreeBlackHoleLoop)"); TreeEvents.RaiseProc(Proc(TreeProcKind.BlackHoleOpen, Konum(5f, 5f), 2.5f));
                yield return Bekle(2.0f);
                Isaret("Proc BlackHoleCollapse (TreeBlackHoleCollapse)"); TreeEvents.RaiseProc(Proc(TreeProcKind.BlackHoleCollapse, Konum(5f, 5f)));
            }

            int islem = 900000;
            AudioCatalog katalog = Resources.Load<AudioCatalog>(AudioCatalog.ResourcePath);
            Assert.IsNotNull(katalog, "katalog yok");
            void Isabet(SurfaceKind yuzey, WeaponCaliber kalibre, ClassicWeaponId silah, float dx)
            {
                Vector3 p = Konum(dx, 4f);
                ProjectileEvents.RaiseImpact(new ProjectileImpactInfo(islem, 0, silah, ProjectileKind.Bullet, kalibre, p, Vector3.up, Vector3.down,
                    yuzey, 300f, false, false, false, true, islem));
                islem++;
            }

            IEnumerator IsabetYuzey(SurfaceKind yuzey)
            {
                foreach (WeaponCaliber kalibre in new[] { WeaponCaliber.Light, WeaponCaliber.Medium, WeaponCaliber.Heavy })
                {
                    AudioSoundId set = ImpactAudio.For(yuzey, kalibre);
                    int deste = katalog.TryGet(set, out AudioCatalog.Entry e) ? e.Clips.Length : 3;
                    for (int i = 0; i < deste; i++)
                    {
                        Isaret(set + " hit " + (i + 1) + "/" + deste);
                        Isabet(yuzey, kalibre, ClassicWeaponId.AssaultRifle, -2f + i);
                        yield return Bekle(0.45f);
                    }

                    yield return Bekle(0.6f);
                }
            }

            IEnumerator KeskinNisanci()
            {
                foreach (SurfaceKind yuzey in new[] { SurfaceKind.Stone, SurfaceKind.Dirt, SurfaceKind.Metal })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Isaret(yuzey + " Heavy lmg_2 (no layer)"); Isabet(yuzey, WeaponCaliber.Heavy, ClassicWeaponId.Lmg2, -1f);
                        yield return Bekle(0.7f);
                        Isaret(yuzey + " Heavy sr_1 (+ ImpactSniperCrack)"); Isabet(yuzey, WeaponCaliber.Heavy, ClassicWeaponId.Sr1, 1f);
                        yield return Bekle(0.9f);
                    }
                }
            }

            try
            {
                m.ThreatLayerOverride = 0;
                m.IntensityOverride = 0.05f;
                yield return new WaitForSecondsRealtime(2f);
                yield return Kaydet("1_wallbuy", WallBuy(), 1.5f);
                yield return Kaydet("2_mod_secimi", ModSecimi(), 1.0f);
                yield return Kaydet("3_agac_ekrani", AgacEkrani(), 3.0f);
                yield return Kaydet("4_agac_savas_anlari", AgacSavas(), 8.5f);   // the bell rings 8 s
                yield return Kaydet("5_agac_alanlar_kara_delik", Alanlar(), 1.5f);
                foreach (SurfaceKind yuzey in new[] { SurfaceKind.Stone, SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Water, SurfaceKind.Wood, SurfaceKind.Default })
                {
                    yield return Kaydet("6_isabet_" + yuzey.ToString().ToLowerInvariant(), IsabetYuzey(yuzey), 0.8f);
                }

                yield return Kaydet("7_keskin_nisanci_katmani", KeskinNisanci(), 1.0f);
            }
            finally
            {
                if (cap != null) Object.DestroyImmediate(cap);
                m.ThreatLayerOverride = oncekiTehdit;
                m.IntensityOverride = oncekiYogunluk;
                DifficultyService.Selected = oncekiMod;
                _s.Rig.Health.DiagnosisInvulnerable = false;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "R4-DINLEME.txt"), isaret.ToString());
            Debug.Log("[DD-TEST] r4.listening files=" + yazilan + " dir=" + dir + " · tree " + _d.Tree.Describe() + " · impacts played=" + _d.Impacts.Played
                + " sniper=" + _d.Impacts.SniperLayers);
            Assert.AreEqual(12, yazilan, "12 segment: 5 + 6 isabet yüzeyi + keskin nişancı");
            Assert.Greater(_d.Impacts.SniperLayers, 0, "keskin nişancı katmanı duyulmadı");
        }
    }
}
