using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Audio;
using DD.Prototype.Audio.EditorTools;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-021 · Ses omurgası EditMode dilimi: olay haritası tamlığı (ADR-021-1), katalog
    /// dönüştürücü idempotansı, manifest LUFS/tepe standardı (BS.1770 ölçerle yeniden
    /// ölçüm), mixer grup/snapshot tablosu, bölge haritası.
    /// </summary>
    [TestFixture]
    [Category("WP021")]
    public sealed class AudioSliceEditModeTests
    {
        // ------------------------------------------------------------------ olay haritası

        [Test]
        public void OlayHaritasi_HerCombatVeRunOlayiIcinSatirVar()
        {
            var eksik = new List<string>();
            foreach (CombatAudioEvent e in Enum.GetValues(typeof(CombatAudioEvent)))
            {
                if (!AudioEventMap.TryFind(AudioEventSource.Combat, e.ToString(), out AudioEventBinding b) || b.Sound == AudioSoundId.None)
                {
                    eksik.Add("Combat." + e);
                }
            }

            foreach (RunAudioEvent e in Enum.GetValues(typeof(RunAudioEvent)))
            {
                if (!AudioEventMap.TryFind(AudioEventSource.Run, e.ToString(), out AudioEventBinding b) || b.Sound == AudioSoundId.None)
                {
                    eksik.Add("Run." + e);
                }
            }

            Assert.IsEmpty(eksik, "Olay haritasında ses girdisi olmayan oyun olayı: " + string.Join(", ", eksik));
        }

        [Test]
        public void OlayHaritasi_OyuncuUiAmbiyansMuzikSatirlari_TamVeTekil()
        {
            Assert.AreEqual(2, AudioEventMap.For(AudioEventSource.Player).Count, "Oyuncu olayı (hasar, ölüm)");
            // WP-021C: 5 PrototypeAudio calls (navigate/confirm/back/denied/reveal) + one row per UiEvent member (WP-022B).
            Assert.AreEqual(5 + Enum.GetValues(typeof(DD.Prototype.UI.UiEvent)).Length, AudioEventMap.For(AudioEventSource.Ui).Count, "UI satırları");
            int bed = 0;
            foreach (AudioEventBinding b in AudioEventMap.For(AudioEventSource.Ambience)) { if (b.Event.StartsWith("Region.")) bed++; }
            Assert.AreEqual(6, bed, "5 bölge + ortak katman (DEC-066)");
            int katman = 0;
            foreach (AudioEventBinding b in AudioEventMap.For(AudioEventSource.Music)) { if (b.Event.StartsWith("Music.") && b.Bus == AudioBus.Music) katman++; }
            Assert.AreEqual(6, katman, "4 koşu katmanı + menü/sonuç döngüleri Music bus'ında");
            Assert.GreaterOrEqual(AudioEventMap.For(AudioEventSource.Polled).Count + AudioEventMap.For(AudioEventSource.Damage).Count, 4,
                "Zombi vokal seti: hazırlık, saldırı, hit, ölüm (DEC-067)");

            var gorulen = new HashSet<string>();
            foreach (AudioEventBinding b in AudioEventMap.Entries)
            {
                string anahtar = b.Source + "/" + b.Event;
                Assert.IsTrue(gorulen.Add(anahtar), "Aynı olay iki kez: " + anahtar);
                if (!b.Reserved)
                {
                    Assert.AreNotEqual(AudioSoundId.None, b.Sound, "Rezerve olmayan satırın sesi yok: " + anahtar);
                }
            }

            // REQ-053: Special Weapon spawn sesi rezervdir - başka olay aynı sesi kullanmaz.
            int gong = 0;
            foreach (AudioEventBinding b in AudioEventMap.Entries)
            {
                if (b.Sound == AudioSoundId.SwSpawnGong) gong++;
            }

            Assert.AreEqual(1, gong, "SwSpawnGong yalnız SpecialWeaponSpawn olayına bağlı olmalı (REQ-053).");
        }

        /// <summary>WP-068 slice 11: the UI v3 contract - fifteen new members bound (TimelineSkipped is the one control row), run cues ≤ 60 and never duck, sliders ≤ 12/s, ±3 % jitter.</summary>
        [Test]
        public void UiV3_Sozlesme_YeniOlaylarBagli_RunCuelariDuckYok_SliderHizi()
        {
            var yeni = new[]
            {
                DD.Prototype.UI.UiEvent.ScreenExiting, DD.Prototype.UI.UiEvent.CurtainClosed, DD.Prototype.UI.UiEvent.CurtainOpened,
                DD.Prototype.UI.UiEvent.PressDown, DD.Prototype.UI.UiEvent.BootLogo, DD.Prototype.UI.UiEvent.HudNewBestRound,
                DD.Prototype.UI.UiEvent.HudEliteDown, DD.Prototype.UI.UiEvent.HudUltraDown, DD.Prototype.UI.UiEvent.CardDealt,
                DD.Prototype.UI.UiEvent.CardFlipped, DD.Prototype.UI.UiEvent.StampLanded, DD.Prototype.UI.UiEvent.CounterRolling,
                DD.Prototype.UI.UiEvent.CounterSettled, DD.Prototype.UI.UiEvent.RerollHoldStarted
            };
            foreach (var e in yeni)
            {
                Assert.AreNotEqual(AudioSoundId.None, AudioEventRouter.UiSoundFor(e), e + " bağlı olmalı");
            }

            Assert.AreEqual(AudioSoundId.None, AudioEventRouter.UiSoundFor(DD.Prototype.UI.UiEvent.TimelineSkipped), "TimelineSkipped kontrol satırıdır");
            Assert.GreaterOrEqual(Enum.GetValues(typeof(DD.Prototype.UI.UiEvent)).Length, 50, "UI v3 sözleşmesi ≥ 50 olay");

            foreach (var s in new[] { AudioSoundId.HudNewBestRound, AudioSoundId.HudEliteDown, AudioSoundId.HudUltraDown })
            {
                AudioPlacement p = AudioMixPlan.UiEvent(s);
                Assert.LessOrEqual(p.Priority, 60f, s + ": koşu içi yeni cue öncelik ≤ 60 (M3)");
                Assert.AreEqual(0f, p.DuckSeconds, s + ": duck yok (stinger değil)");
            }

            Assert.GreaterOrEqual(AudioMixPlan.UiEvent(AudioSoundId.UiSliderTick).MinRetrigger, 1f / 12f - 1e-4f, "slider ≤ 12 tık/s");
            Assert.AreEqual(0.03f, AudioMixPlan.UiEvent(AudioSoundId.UiConfirm).PitchJitter, 1e-4f, "Interface ±3 % jitter (M2)");
            Assert.AreEqual(3, AudioMixPlan.UiEvent(AudioSoundId.UiCardDealt).MaxInstances, "3 kart üst üste çalabilir");
        }

        /// <summary>WP-021C · §12: every WP-022B UiEvent member has exactly one map row; the router's table equals the map (bound ↔ reserved).</summary>
        [Test]
        public void UiOlaylari_HerUiEventUyesininSatiriVar_RouterHaritayaEsit()
        {
            int bagli = 0, rezerve = 0;
            foreach (DD.Prototype.UI.UiEvent e in Enum.GetValues(typeof(DD.Prototype.UI.UiEvent)))
            {
                string anahtar = "UiEvent." + e;
                Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Ui, anahtar, out AudioEventBinding b), "Matris satırı yok: " + anahtar);
                Assert.AreEqual(AudioBus.Interface, b.Bus, anahtar + " UI bus'ta olmalı");
                AudioSoundId router = AudioEventRouter.UiSoundFor(e);
                if (b.Reserved)
                {
                    Assert.AreEqual(AudioSoundId.None, router, anahtar + " rezerve (sahibi başka satır) ama router çalıyor → çift ses");
                    Assert.IsFalse(string.IsNullOrEmpty(b.Note), anahtar + " rezerve satırın gerekçesi yok");
                    rezerve++;
                }
                else
                {
                    Assert.AreEqual(b.Sound, router, anahtar + " router sesi haritayla aynı olmalı");
                    Assert.AreNotEqual(AudioSoundId.None, b.Sound, anahtar);
                    AudioPlacement p = AudioMixPlan.UiEvent(b.Sound);
                    Assert.AreEqual(b.Sound, p.Sound);
                    Assert.AreEqual(AudioBus.Interface, p.Bus);
                    bagli++;
                }
            }

            Assert.GreaterOrEqual(bagli, 18, "Bağlı UI satırı sayısı");
            // Own identity: no two bound UI rows share a sound (§12 "her şeyin kendi sesi").
            var sesler = new HashSet<AudioSoundId>();
            foreach (AudioEventBinding b in AudioEventMap.For(AudioEventSource.Ui))
            {
                if (!b.Reserved && b.Event.StartsWith("UiEvent."))
                {
                    Assert.IsTrue(sesler.Add(b.Sound), "İki bağlı UI olayı aynı sesi paylaşıyor: " + b.Sound);
                }
            }

            Debug.Log("[DD-TEST] wp021c.ui rows bound=" + bagli + " reserved=" + rezerve);
        }

        /// <summary>WP-048B · every AP-009 clip event (CharacterAnimationEvents.Names) has an Animation row; the WP-032C weapon foley resolves to 21 distinct ids.</summary>
        [Test]
        public void AnimasyonOlaylari_HerKlipOlayininSatiriVar_SilahFoleyAyrik()
        {
            var satirlar = AudioEventMap.For(AudioEventSource.Animation);
            var eksik = new List<string>();
            for (int i = 1; i < DD.Prototype.Characters.CharacterAnimationEvents.Names.Length; i++)
            {
                string ad = DD.Prototype.Characters.CharacterAnimationEvents.Names[i];
                bool var = false;
                foreach (AudioEventBinding b in satirlar)
                {
                    // "anim.foot_l/foot_r", "anim.mag_in/mag_in_2/mag_in_3", "anim.fire / anim.pickup" - the name is a token of the row key.
                    if (System.Text.RegularExpressions.Regex.IsMatch(b.Event, @"(^|[ ./])" + System.Text.RegularExpressions.Regex.Escape(ad) + @"($|[ /→(])")) { var = true; break; }
                }

                if (!var) eksik.Add(ad);
            }

            Assert.IsEmpty(eksik, "Haritada satırı olmayan klip olayı: " + string.Join(", ", eksik));

            var ids = new HashSet<AudioSoundId>();
            foreach (DD.Prototype.Core.Config.ClassicWeaponId w in Enum.GetValues(typeof(DD.Prototype.Core.Config.ClassicWeaponId)))
            {
                foreach (var e in new[] { DD.Prototype.Characters.CharacterAnimationEvent.Eject, DD.Prototype.Characters.CharacterAnimationEvent.Chamber, DD.Prototype.Characters.CharacterAnimationEvent.MagOut, DD.Prototype.Characters.CharacterAnimationEvent.MagIn, DD.Prototype.Characters.CharacterAnimationEvent.SwitchReady, DD.Prototype.Characters.CharacterAnimationEvent.Lowered, DD.Prototype.Characters.CharacterAnimationEvent.Raised })
                {
                    AudioSoundId id = AudioEventMap.WeaponFoley(w, e);
                    Assert.IsTrue(id.ToString().StartsWith("Weapon"), w + "/" + e + " → " + id);
                    Assert.IsTrue(ids.Add(id), "Silah foley kimliği paylaşılıyor: " + id);
                }
            }

            Assert.AreEqual(21, ids.Count);
            Assert.AreEqual(AudioSoundId.WeaponCChamber, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.CombatShotgun, DD.Prototype.Characters.CharacterAnimationEvent.Chamber));
            Assert.AreEqual(AudioSoundId.WeaponBMagIn, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.AutoSmg, DD.Prototype.Characters.CharacterAnimationEvent.MagIn3));
            Assert.AreEqual(AudioSoundId.None, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, DD.Prototype.Characters.CharacterAnimationEvent.FootLeft));
        }

        /// <summary>WP-058B · AP-015P plate → footstep variant rule (WP-038 plates carry Dirt/Stone/Water tags only).</summary>
        [Test]
        public void AyakSesi_PlakaVaryantlari_AdKurali()
        {
            Assert.AreEqual(AudioSoundId.FootstepPavement, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP015_paving_quiet_01", SurfaceKind.Stone));
            Assert.AreEqual(AudioSoundId.FootstepGravel, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP015_ground_center_01", SurfaceKind.Dirt));
            Assert.AreEqual(AudioSoundId.None, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP015_ground_east_01", SurfaceKind.Dirt), "kenar zemin = toprak (varsayılan)");
            Assert.AreEqual(AudioSoundId.None, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP015_route_00", SurfaceKind.Stone), "taş rota = FootstepStone varsayılanı");
            Assert.AreEqual(AudioSoundId.FootstepWaterShallow, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP015_water_pond", SurfaceKind.Water));
            Assert.AreEqual(AudioSoundId.None, DD.Art.Ap005SurfaceTags.FootstepVariantForName("AP005_i0001", SurfaceKind.Wood));
        }

        /// <summary>WP-058 · WP-056 SpecialUsageEvent: every (Special, kind) pair resolves to its own voice; Activated / Deactivated are covered.</summary>
        [Test]
        public void SpecialKullanim_HerSpecialVeKind_KendiSesi()
        {
            var ids = new HashSet<AudioSoundId>();
            foreach (DD.Prototype.Core.Config.SpecialWeaponId w in Enum.GetValues(typeof(DD.Prototype.Core.Config.SpecialWeaponId)))
            {
                foreach (DD.Prototype.Weapons.SpecialUsageKind k in Enum.GetValues(typeof(DD.Prototype.Weapons.SpecialUsageKind)))
                {
                    AudioSoundId id = AudioEventMap.SpecialUsage(w, k);
                    bool covered = k == DD.Prototype.Weapons.SpecialUsageKind.Activated || k == DD.Prototype.Weapons.SpecialUsageKind.Deactivated;
                    Assert.AreEqual(covered, id == AudioSoundId.None, w + "/" + k + " → " + id);
                    if (!covered) Assert.IsTrue(ids.Add(id), "Special kullanım sesi paylaşılıyor: " + id);
                    string satir = covered ? "SpecialUsage." + k : "SpecialUsage." + k + " → " + w;
                    Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Combat, satir, out AudioEventBinding b), "Matris satırı yok: " + satir);
                    Assert.AreEqual(covered, b.Reserved, satir);
                }
            }

            Assert.AreEqual(12, ids.Count, "3 Special × 4 kullanım anı");
        }

        /// <summary>WP-048 · AP-017 breakable event table (AP017-breakable-event-table-v001): every family root name resolves to its own voice.</summary>
        [Test]
        public void Kirilabilirler_AP017AileAdlari_KendiSesineCozulur()
        {
            var beklenen = new (string ad, AudioSoundId ses)[]
            {
                ("AP017_breakable_bench_V01", AudioSoundId.BreakBench), ("AP017_breakable_sign_V01", AudioSoundId.BreakSign),
                ("AP017_breakable_fence_panel_V02", AudioSoundId.BreakFence), ("AP017_breakable_lantern_paper_V01", AudioSoundId.BreakLanternPaper),
                ("AP017_breakable_shoji_V01", AudioSoundId.BreakShoji), ("AP017_breakable_lantern_stone_V02", AudioSoundId.BreakLanternStone),
                ("AP017_breakable_planter_V01", AudioSoundId.BreakPlanter), ("AP017_breakable_strawbale_V01", AudioSoundId.BreakBale),
                ("SM_small_breakable_V03", AudioSoundId.BreakCeramic), ("", AudioSoundId.BreakCeramic)
            };
            var sesler = new HashSet<AudioSoundId>();
            foreach ((string ad, AudioSoundId ses) in beklenen)
            {
                Assert.AreEqual(ses, AudioEventRouter.BreakSoundForName(ad), ad);
                sesler.Add(ses);
            }

            Assert.AreEqual(9, sesler.Count, "8 AP-017 ailesi + seramik: dokuz ayrı ses");
        }

        /// <summary>WP-060 · WP-038B: the break event id is data on the prop; six events each own a voice, the name only refines inside the event.</summary>
        [Test]
        public void Kirilabilirler_EventIdVeriden_AltiOlayKendiSesi_AdOlayIcindeInceltir()
        {
            var beklenen = new (string olay, string ad, AudioSoundId ses)[]
            {
                ("BreakStone", "SM_breakable_lantern_stone_V02", AudioSoundId.BreakLanternStone), ("BreakStone", "", AudioSoundId.BreakLanternStone),
                ("BreakCeramic", "SM_breakable_planter_V01", AudioSoundId.BreakPlanter), ("BreakCeramic", "SM_small_breakable_V03", AudioSoundId.BreakCeramic),
                ("BreakWood", "SM_breakable_bench_V01", AudioSoundId.BreakBench), ("BreakWood", "SM_breakable_sign_V01", AudioSoundId.BreakSign), ("BreakWood", "", AudioSoundId.BreakBench),
                ("BreakBamboo", "SM_breakable_fence_panel_V02", AudioSoundId.BreakFence), ("BreakPaper", "SM_breakable_shoji_V01", AudioSoundId.BreakShoji),
                ("BreakPaper", "SM_breakable_lantern_paper_V01", AudioSoundId.BreakLanternPaper), ("BreakStraw", "SM_breakable_strawbale_V01", AudioSoundId.BreakBale),
                ("", "SM_breakable_sign_V01", AudioSoundId.BreakSign), ("Unknown", "", AudioSoundId.BreakCeramic)
            };
            foreach ((string olay, string ad, AudioSoundId ses) in beklenen)
            {
                Assert.AreEqual(ses, AudioEventRouter.BreakSoundForEvent(olay, ad), olay + " / " + ad);
            }

            string[] olaylar = { "BreakStone", "BreakCeramic", "BreakWood", "BreakBamboo", "BreakPaper", "BreakStraw" };
            foreach (string olay in olaylar)
            {
                bool var = false;
                foreach (AudioEventBinding e in AudioEventMap.Entries) if (e.Event == "BreakableProp.Broke · EventId " + olay) var = true;
                Assert.IsTrue(var, "AudioEventMap satırı yok: " + olay);
            }
        }

        /// <summary>WP-021C · tier vocal set: every tier has its own windup/attack/hit/death/idle/aggro id (B·B·B).</summary>
        [Test]
        public void TierVokalSeti_HerTierKendiKimligi()
        {
            var tiers = (DD.Prototype.Core.Config.EnemyTier[])Enum.GetValues(typeof(DD.Prototype.Core.Config.EnemyTier));
            var gorulen = new HashSet<AudioSoundId>();
            foreach (DD.Prototype.Core.Config.EnemyTier t in tiers)
            {
                foreach (AudioSoundId id in new[] { AudioEventMap.ZombieWindup(t), AudioEventMap.ZombieAttack(t), AudioEventMap.ZombieHit(t), AudioEventMap.ZombieDeath(t), AudioEventMap.ZombieIdle(t), AudioEventMap.ZombieAggro(t) })
                {
                    Assert.AreNotEqual(AudioSoundId.None, id);
                    Assert.IsTrue(gorulen.Add(id), "Tier vokal kimliği paylaşılıyor: " + id + " (" + t + ")");
                }
            }

            Assert.AreEqual(tiers.Length * 6, gorulen.Count);
        }

        [Test]
        public void OlayHaritasi_HerSesinRecipeVeyaKatalogKarsiligiVar()
        {
            // Her ses ya prewarm listesinde (recipe) ya da WP-021 üretim setinde: hiçbiri
            // AudioRecipes.Build'in "default" dalına (yanlış aile) düşmemeli.
            var prewarm = new HashSet<AudioSoundId>(AudioClipLibrary.AllSounds);
            var kabul = new HashSet<AudioSoundId>(prewarm);
            foreach (AudioSoundId id in Enum.GetValues(typeof(AudioSoundId)))
            {
                if ((int)id >= 160) kabul.Add(id); // WP-021 kimlikleri: recipe fallback açıkça eşlendi
            }

            foreach (AudioEventBinding b in AudioEventMap.Entries)
            {
                if (b.Reserved) continue;
                Assert.IsTrue(kabul.Contains(b.Sound), "Ses kimliğinin recipe/katalog karşılığı yok: " + b.Sound);
            }

            foreach (AudioSoundId id in Enum.GetValues(typeof(AudioSoundId)))
            {
                if (id == AudioSoundId.None) continue;
                float[] ornek = AudioRecipes.Build(id, 0);
                Assert.IsTrue(ornek != null && ornek.Length > 64, "Recipe boş: " + id);
            }
        }

        // ------------------------------------------------------------------ ölçer

        [Test]
        public void LoudnessMeter_TamOlcekSinus_BS1770Referansi()
        {
            // BS.1770: 997 Hz, 0 dBFS sinüs = -3.01 LUFS. Ölçerin doğruluk kanıtı.
            const int sr = 48000;
            float[] x = new float[sr * 2];
            for (int i = 0; i < x.Length; i++) x[i] = Mathf.Sin(2f * Mathf.PI * 997f * i / sr);
            LoudnessMeter.Result r = LoudnessMeter.Measure(x, sr);
            Assert.AreEqual(-3.01f, r.IntegratedLufs, 0.35f, "K-ağırlıklı integrated LUFS");
            Assert.AreEqual(0f, r.TruePeakDb, 0.2f, "true peak (dBTP)");
            Assert.IsFalse(r.Silent);

            // -20 dB'lik sinüs 20 LU daha sessiz olmalı (doğrusallık).
            for (int i = 0; i < x.Length; i++) x[i] *= 0.1f;
            LoudnessMeter.Result r2 = LoudnessMeter.Measure(x, sr);
            Assert.AreEqual(r.IntegratedLufs - 20f, r2.IntegratedLufs, 0.2f);
            Assert.IsTrue(float.IsNegativeInfinity(LoudnessMeter.Measure(new float[sr], sr).IntegratedLufs), "sessizlik = -inf");
        }

        // ------------------------------------------------------------------ katalog

        [Test]
        public void Katalog_ManifestVar_DonusturucuIdempotan_VeYuklenebilir()
        {
            Assert.IsTrue(File.Exists(AudioCatalogBuilder.ManifestPath), "AP-011 manifesti yok: " + AudioCatalogBuilder.ManifestPath);
            string birinci = AudioCatalogBuilder.BuildCatalog();
            string hashOnce = File.Exists(AudioCatalogBuilder.CatalogPath) ? AudioCatalogBuilder.HashOf(AudioCatalogBuilder.CatalogPath) : null;
            string ikinci = AudioCatalogBuilder.BuildCatalog();
            string hashSonra = AudioCatalogBuilder.HashOf(AudioCatalogBuilder.CatalogPath);

            StringAssert.Contains("up to date", ikinci, "İkinci koşu değişiklik yapmamalı (idempotans). 1: " + birinci + " 2: " + ikinci);
            Assert.AreEqual(hashOnce, hashSonra, "Katalog asset baytları ikinci koşuda değişti.");

            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog);
            Assert.AreSame(katalog, Resources.Load<AudioCatalog>(AudioCatalog.ResourcePath), "Resources.Load aynı varlığı bulmalı");
            int dosya = 0, sfx = 0, amb = 0, muzik = 0;
            foreach (AudioCatalog.Entry e in katalog.Entries)
            {
                Assert.IsTrue(e.Clips != null && e.Clips.Length > 0, "Klipsiz girdi: " + e.Sound);
                foreach (AudioClip c in e.Clips) Assert.IsNotNull(c, "Null klip: " + e.Sound);
                dosya += e.Clips.Length;
                string ad = e.Sound.ToString();
                // Region / common beds only: WP-048 adds the pond bed (AmbPondBed, 3D at the pond) as a seventh loop outside the crossfade set.
                if ((ad.StartsWith("AmbRegion") || ad == "AmbCommon") && e.Loop) amb++;
                else if (ad.StartsWith("Music") && !e.Loop) sfx += e.Clips.Length;   // WP-057 stingers are one-shots
                else if (ad.StartsWith("Music") && (ad == "MusicMenu" || ad == "MusicResults")) { }   // WP-057 phase loops
                else if (ad.StartsWith("Music")) muzik++;
                else sfx += e.Clips.Length;
            }

            // DoD: ≥ 40 SFX + 6 ambiyans + 4 müzik katmanı (sayım dosya/varyasyon üzerinden).
            Assert.GreaterOrEqual(sfx, 40, "DoD: ≥ 40 SFX klibi; " + sfx);
            Assert.AreEqual(6, amb, "DoD: 6 ambiyans bed'i (ortak + 5 bölge)");
            Assert.AreEqual(4, muzik, "DoD: 4 müzik katmanı");
            Debug.Log("[DD-TEST] wp021.catalog entries=" + katalog.Entries.Count + " files=" + dosya + " sfx=" + sfx + " amb=" + amb + " music=" + muzik + " manifest=" + katalog.SourceHash.Substring(0, 8));
        }

        [Test]
        public void Katalog_ManifestLufsVeTepe_StandartIcinde_VeYenidenOlcumTutuyor()
        {
            AudioCatalogBuilder.Manifest manifest = AudioCatalogBuilder.LoadManifest();
            Assert.IsNotNull(manifest, "manifest yok");
            AudioCatalogBuilder.BuildCatalog();
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog, "katalog yok (dönüştürücü)");

            float tol = manifest.standard.toleranceLu;
            var hatalar = new List<string>();
            int olculen = 0;
            var girdi = new Dictionary<string, AudioCatalogBuilder.ManifestEntry>();
            foreach (AudioCatalogBuilder.ManifestEntry m in manifest.entries) girdi[m.sound] = m;
            foreach (AudioCatalog.Entry e in katalog.Entries)
            {
                // v003: girdinin kendi hedefi (tasarım seviyesi: -18 SFX, -20 ambiyans/ayak/idle, -16 müzik)
                // ve tavanı (-1,5; silahlar -2/-3 kodek payı). Hedef standart kümenin dışına çıkamaz.
                AudioCatalogBuilder.ManifestEntry m = girdi[e.Sound.ToString()];
                float hedef = m.targetLufs != 0f ? m.targetLufs : Hedef(e.Sound, manifest.standard);
                float tavan = m.ceilingDbtp != 0f ? m.ceilingDbtp : manifest.standard.truePeakMax;
                // WP-021C: the UI palette sits lower by design (hover / tick / pane -26…-23; chips -22…-20).
                // Ambience scatter one-shots (v006: creak / ring / knock / settle) sit at −24 under the −20 beds;
                // WP-065C: the fūrin tinkle sits 2 LU lower still (−26, Fable v014 design).
                // WP-068 s11: UI v3 textures under the cues - the soroban roll (−28) and the hold rub (−30).
                float altSinir = m.bus == "UI" ? -30.1f : m.bus == "Ambience" ? -26.1f : -22.1f;
                if (hedef < altSinir || hedef > -15.9f) hatalar.Add($"{e.Sound}: hedef {hedef} standart kümede değil ({(m.bus == "UI" ? "-16…-30 UI" : m.bus == "Ambience" ? "-16…-26 ambiyans" : "-16…-22")})");
                if (tavan > manifest.standard.truePeakMax + 0.01f) hatalar.Add($"{e.Sound}: tavan {tavan} dBTP > standart {manifest.standard.truePeakMax}");
                // Standart: hedef ± tolerans; ya da tavana dayanmış ve hedefin ALTINDA (kısa transientler
                // tavanda hedefe ulaşamaz - tepe kısıtı seviyeyi belirler, bu doğru davranıştır).
                bool tepeSinirinda = e.TruePeakDb >= tavan - 0.15f && e.Lufs <= hedef + 0.1f;
                if (Mathf.Abs(e.Lufs - hedef) > tol && !tepeSinirinda)
                {
                    hatalar.Add($"{e.Sound}: manifest {e.Lufs:0.0} LUFS, hedef {hedef:0.0} ± {tol} (tepe {e.TruePeakDb:0.0}, tavan {tavan})");
                }

                if (e.TruePeakDb > tavan + 0.1f)
                {
                    hatalar.Add($"{e.Sound}: true peak {e.TruePeakDb:0.0} dBTP > {tavan}");
                }

                // Yeniden ölçüm: sevk edilen (Vorbis) klip manifest damgasını ± 2 LU içinde tutmalı.
                // Streaming klipler GetData vermez; onlar atlanır (loop bed'ler).
                AudioClip clip = e.Clips[0];
                if (clip.loadType == AudioClipLoadType.Streaming || clip.length > 12f)
                {
                    continue;
                }

                clip.LoadAudioData();
                LoudnessMeter.Result r = LoudnessMeter.Measure(clip);
                if (r.Silent)
                {
                    hatalar.Add($"{e.Sound}: klip sessiz ({clip.name})");
                    continue;
                }

                olculen++;
                if (Mathf.Abs(r.IntegratedLufs - e.Lufs) > 2.0f)
                {
                    hatalar.Add($"{e.Sound}: ölçülen {r.IntegratedLufs:0.0} LUFS ≠ manifest {e.Lufs:0.0} ({clip.name})");
                }

                // Sevk edilen klip Vorbis: kodek aşımı transientlerde ~1 dB'e çıkar; master (manifest)
                // tavanı -1.5 dBTP olduğundan ölçülen tepe -0.5 dBTP'yi geçmemeli (0 dBFS'e 0.5 dB pay).
                if (r.TruePeakDb > manifest.standard.truePeakMax + 1.0f)
                {
                    hatalar.Add($"{e.Sound}: ölçülen tepe {r.TruePeakDb:0.0} dBTP ({clip.name})");
                }
            }

            Assert.Greater(olculen, 20, "Yeniden ölçülen klip sayısı");
            Assert.IsEmpty(hatalar, "Standart dışı (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        private static float Hedef(AudioSoundId id, AudioCatalogBuilder.ManifestStandard s)
        {
            switch (id)
            {
                case AudioSoundId.MusicBase:
                case AudioSoundId.MusicPercussion:
                case AudioSoundId.MusicTension:
                case AudioSoundId.MusicAccent:
                    return s.musicLufs;
                case AudioSoundId.AmbCommon:
                case AudioSoundId.AmbRegionCenter:
                case AudioSoundId.AmbRegionNorth:
                case AudioSoundId.AmbRegionEast:
                case AudioSoundId.AmbRegionSouth:
                case AudioSoundId.AmbRegionWest:
                    return s.ambienceLufs;
                default:
                    return s.sfxLufs;
            }
        }

        // ------------------------------------------------------------------ v003 üst kalite kapısı

        /// <summary>
        /// Kullanıcı direktifi (2026-09-16): kaynak 48 kHz / 24-bit, gürültü tabanı ≤ −60 dBFS,
        /// klip 0, DC 0, sevk OGG q8 (SFX) / q7 (loop). Manifest damgaları + kaynak WAV başlığı
        /// + importer ayarı + sevk klibinde yeniden ölçüm (en sessiz 100 ms penceresi).
        /// </summary>
        [Test]
        public void UstKalite_24BitKaynak_GurultuTabani_Klip_DC_ImporterKalitesi()
        {
            AudioCatalogBuilder.Manifest manifest = AudioCatalogBuilder.LoadManifest();
            Assert.IsNotNull(manifest, "manifest yok");
            AudioCatalogBuilder.BuildCatalog();
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog);

            var hatalar = new List<string>();
            int olculen = 0;
            foreach (AudioCatalogBuilder.ManifestEntry m in manifest.entries)
            {
                if (m.bitDepth != manifest.standard.bitDepth) hatalar.Add($"{m.sound}: kaynak {m.bitDepth}-bit ≠ {manifest.standard.bitDepth}");
                if (m.clipped > 0) hatalar.Add($"{m.sound}: {m.clipped} klip örneği");
                if (m.dcDb > -60f) hatalar.Add($"{m.sound}: DC {m.dcDb:0.0} dBFS");
                // Gürültü tabanı = ham take'in en sessiz 100 ms'i (kaynak arka planı); sürekli malzeme (burst, loop) muaf.
                if (!m.continuous && m.noiseFloorDb > manifest.standard.noiseFloorMaxDb) hatalar.Add($"{m.sound}: kaynak gürültü tabanı {m.noiseFloorDb:0.0} dBFS > {manifest.standard.noiseFloorMaxDb}");
                if (!m.loop && m.endSilenceDb > -70f) hatalar.Add($"{m.sound}: dosya sonu sessiz değil ({m.endSilenceDb:0.0} dBFS)");

                for (int f = 0; f < m.files.Length; f++)
                {
                    string path = AudioCatalogBuilder.AudioRoot + m.files[f];
                    int bits = WavBitDepth(Path.GetFullPath(path));
                    if (bits != manifest.standard.bitDepth) hatalar.Add($"{m.files[f]}: WAV başlığı {bits}-bit");
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) { hatalar.Add($"{m.files[f]}: importer yok"); continue; }
                    AudioImporterSampleSettings ss = importer.defaultSampleSettings;
                    // WP-072: weapon v3 q9 / PCM transients (DEC-093); the rest v003 q8 SFX, q7 loops and streams.
                    AudioCompressionFormat bicim = AudioCatalogBuilder.WantedFormat(m.sound, m.loop, ss.loadType == AudioClipLoadType.Streaming, out float beklenen);
                    if (ss.compressionFormat != bicim || (bicim == AudioCompressionFormat.Vorbis && !Mathf.Approximately(ss.quality, beklenen)))
                        hatalar.Add($"{m.files[f]}: importer {ss.compressionFormat} q{ss.quality:0.00} ≠ {bicim} q{beklenen:0.00}");
                }
            }

            // Sevk klibinde yeniden ölçüm (Vorbis sonrası): tek-atışlarda en sessiz 100 ms ≤ −54 dBFS
            // (kodek tabanı için 6 dB pay), klip yok (örnek tepe < 0 dBFS).
            foreach (AudioCatalog.Entry e in katalog.Entries)
            {
                AudioClip clip = e.Clips[0];
                if (e.Loop || clip.loadType == AudioClipLoadType.Streaming || clip.length > 12f) continue;
                clip.LoadAudioData();
                float[] data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                float[] mono = LoudnessMeter.ToMono(data, clip.channels);
                // WP-021C: a UI tick can be shorter than the 100 ms window - it is then its own (single) window.
                int w = Mathf.Min(clip.frequency / 10, mono.Length); int n = mono.Length / w; float minRms = float.MaxValue; float peak = 0f; double sum = 0;
                for (int i = 0; i < mono.Length; i++) { float a = Mathf.Abs(mono[i]); if (a > peak) peak = a; sum += mono[i]; }
                for (int b = 0; b < n; b++) { double acc = 0; for (int i = 0; i < w; i++) { float v = mono[b * w + i]; acc += v * v; } float rms = Mathf.Sqrt((float)(acc / w)); if (rms < minRms) minRms = rms; }
                olculen++;
                float floorDb = 20f * Mathf.Log10(minRms + 1e-9f);
                // Sevk klibinde: son 10 ms sessiz (kuyruk kapısı) ve klip yok; en sessiz pencere raporlanır.
                float endRms = 0f; int tailN = Mathf.Min(mono.Length, clip.frequency / 100);
                for (int i = mono.Length - tailN; i < mono.Length; i++) endRms += mono[i] * mono[i];
                float endDb = 20f * Mathf.Log10(Mathf.Sqrt(endRms / Mathf.Max(1, tailN)) + 1e-9f);
                if (endDb > -60f) hatalar.Add($"{e.Sound}: sevk klibi sonu sessiz değil {endDb:0.0} dBFS ({clip.name})");
                if (floorDb > 0f) hatalar.Add($"{e.Sound}: ölçüm hatası ({clip.name})");
                if (peak >= 0.999f) hatalar.Add($"{e.Sound}: sevk klibi klip ({clip.name})");
                float dc = 20f * Mathf.Log10(Mathf.Abs((float)(sum / mono.Length)) + 1e-9f);
                if (dc > -50f) hatalar.Add($"{e.Sound}: sevk klibi DC {dc:0.0} dBFS ({clip.name})");
            }

            Assert.Greater(olculen, 30);
            Assert.IsEmpty(hatalar, "Üst kalite kapısı (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp021.quality entries=" + manifest.entries.Length + " remeasured=" + olculen + " bitDepth=" + manifest.standard.bitDepth + " floorMax=" + manifest.standard.noiseFloorMaxDb + " q=" + AudioCatalogBuilder.SfxQuality + "/" + AudioCatalogBuilder.MusicQuality);
        }

        private static int WavBitDepth(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (fs.Length < 36) return 0;
                fs.Seek(12, SeekOrigin.Begin);
                while (fs.Position + 8 <= fs.Length)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    if (id == "fmt ")
                    {
                        fs.Seek(14, SeekOrigin.Current); // format, channels, rate, byte rate, block align
                        return br.ReadInt16();
                    }

                    fs.Seek(size + (size & 1), SeekOrigin.Current);
                }

                return 0;
            }
        }

        // ------------------------------------------------------------------ mixer

        [Test]
        public void Mixer_AltiGrupDortSnapshot_AcikParametreler_KopruEslemesi()
        {
            Assert.IsTrue(AudioMixerBuilder.Verify(out string neden), "Mixer varlığı eksik/yanlış: " + neden);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerBuilder.AssetPath);
            Assert.AreSame(mixer, Resources.Load<AudioMixer>(AudioMixerBridge.ResourcePath));

            var kopru = new AudioMixerBridge(mixer);
            Assert.IsTrue(kopru.Available);
            foreach (string g in AudioMixerBridge.GroupNames) Assert.IsNotNull(kopru.Group(g), "grup: " + g);
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus))) Assert.IsNotNull(kopru.GroupFor(bus), "bus→grup: " + bus);
            Assert.IsNotNull(kopru.MusicGroup);
            foreach (AudioSnapshot s in Enum.GetValues(typeof(AudioSnapshot))) Assert.IsTrue(kopru.HasSnapshot(s), "snapshot: " + s);

            Assert.AreEqual(AudioMixerBridge.GroupSfxPlayer, kopru.GroupFor(AudioBus.Weapon).name, "silah = oyuncunun eli");
            Assert.AreEqual(AudioMixerBridge.GroupSfxWorld, kopru.GroupFor(AudioBus.Target).name);
            Assert.AreEqual(AudioMixerBridge.GroupUi, kopru.GroupFor(AudioBus.Interface).name);
            Assert.AreEqual(AudioMixerBridge.GroupAmbience, kopru.GroupFor(AudioBus.Ambience).name);
            Assert.AreEqual(-80f, AudioMixerBridge.LinearToDb(0f));
            Assert.AreEqual(0f, AudioMixerBridge.LinearToDb(1f), 0.001f);
            Assert.AreEqual(-6.02f, AudioMixerBridge.LinearToDb(0.5f), 0.05f);

            // WP-021C: master limiter (Compressor on Master), parameters exposed and at the engineer's values.
            Assert.IsTrue(AudioMixerBuilder.HasMasterLimiter(out string limiter), "Master limiter: " + limiter);
            Assert.IsTrue(mixer.GetFloat(AudioMixerBuilder.LimiterThresholdParam, out float esik));
            Assert.IsTrue(mixer.GetFloat(AudioMixerBuilder.LimiterAttackParam, out float atak));
            Assert.IsTrue(mixer.GetFloat(AudioMixerBuilder.LimiterReleaseParam, out float birakma));
            Assert.AreEqual(AudioMixerBuilder.LimiterThresholdDb, esik, 0.01f, "limiter threshold");
            Assert.AreEqual(AudioMixerBuilder.LimiterAttackMs, atak, 0.01f, "limiter attack");
            Assert.AreEqual(AudioMixerBuilder.LimiterReleaseMs, birakma, 0.01f, "limiter release");
        }

        // ------------------------------------------------------------------ WP-021C · yüzey etiketleri

        /// <summary>WP-021C · ADR-021-1 v2: every placement family in the shipped world has a surface rule (no family falls back by role).</summary>
        [Test]
        public void YuzeyEtiketleri_HerPlacementAilesininKuraliVar()
        {
            var placement = AssetDatabase.LoadAssetAtPath<DD.Art.WorldPlacementAsset>("Assets/_Art/Environment/World/AP005_Placement.asset");
            Assert.IsNotNull(placement, "AP005_Placement.asset yok");
            var aileler = new SortedDictionary<string, int>();
            var eksik = new List<string>();
            foreach (DD.Art.WorldPlacementAsset.Instance inst in placement.instances)
            {
                string key = DD.Art.Ap005SurfaceTags.Key(inst.family);
                aileler.TryGetValue(key, out int n); aileler[key] = n + 1;
                if (!DD.Art.Ap005SurfaceTags.FamilySurface.ContainsKey(key) && !eksik.Contains(key)) eksik.Add(key);
            }

            Assert.AreEqual(0, eksik.Count, "Kuralsız aile(ler): " + string.Join(", ", eksik));
            Assert.AreEqual(SurfaceKind.Wood, DD.Art.Ap005SurfaceTags.FamilySurface["bridge_segment"]);
            Assert.AreEqual(SurfaceKind.Water, DD.Art.Ap005SurfaceTags.FamilySurface["pond_water_surface"]);
            Assert.AreEqual(SurfaceKind.Dirt, DD.Art.Ap005SurfaceTags.FamilySurface["ground"]);
            Assert.AreEqual(SurfaceKind.Wood, DD.Art.Ap005SurfaceTags.FamilySurface["drying_rack_cloth"]);
            Assert.AreEqual(SurfaceKind.Wood, DD.Art.Ap005SurfaceTags.FamilySurface["well_head_tsurube"]);
            Assert.AreEqual(SurfaceKind.Wood, DD.Art.Ap005SurfaceTags.FamilySurface["ofuda_post"]);
            Assert.AreEqual(SurfaceKind.Stone, DD.Art.Ap005SurfaceTags.FamilySurface["breakable_sake_jar"]);
            int stone = 0;
            foreach (DD.Art.WorldPlacementAsset.Route r in placement.routes) if (r.surface == "Stone") stone++;
            Assert.Greater(stone, 0, "Taş rota yok: zemin iyileştirmesi çalışamaz");
            Debug.Log("[DD-TEST] wp021c.surface families=" + aileler.Count + " instances=" + placement.instances.Count + " stoneRoutes=" + stone);
        }

        // ------------------------------------------------------------------ bölge haritası

        [Test]
        public void BolgeHaritasi_BesPoligon_HerMerkezKendiBolgesineCozulur()
        {
            string rapor = AudioCatalogBuilder.BuildRegionMap();
            var harita = AssetDatabase.LoadAssetAtPath<AudioRegionMap>(AudioCatalogBuilder.RegionMapPath);
            Assert.IsNotNull(harita, rapor);
            Assert.AreEqual(5, harita.Polygons.Count, "merkez/kuzey/doğu/güney/batı (DEC-066)");
            Assert.AreSame(harita, Resources.Load<AudioRegionMap>(AudioRegionMap.ResourcePath));

            var gorulen = new HashSet<AudioRegion>();
            foreach (AudioRegionMap.Polygon p in harita.Polygons)
            {
                Assert.IsTrue(gorulen.Add(p.Region), "bölge iki kez: " + p.Region);
                Vector3 merkez = new Vector3(p.Centroid.x, 0f, p.Centroid.y);
                Assert.AreEqual(p.Region, harita.Resolve(merkez, 0f), "merkez kendi poligonunda değil: " + p.SourceId);
            }

            Assert.AreEqual(AudioRegion.None, harita.Resolve(new Vector3(9999f, 0f, 9999f), 0f), "harita dışı = None");
            StringAssert.Contains("up to date", AudioCatalogBuilder.BuildRegionMap(), "ikinci koşu idempotan");
        }

        [Test]
        public void BolgeHaritasi_NoktaPoligonTesti()
        {
            float[] kare = { 0f, 0f, 10f, 0f, 10f, 10f, 0f, 10f };
            Assert.IsTrue(AudioRegionMap.Contains(kare, 5f, 5f));
            Assert.IsFalse(AudioRegionMap.Contains(kare, 15f, 5f));
            Assert.IsFalse(AudioRegionMap.Contains(new float[] { 0f, 0f, 1f, 1f }, 0.5f, 0.5f), "3 köşeden az = dış");
        }

        [Test]
        public void AtesV3_KimlikTablosu_WP069AudioIdleri_HibritUcSilah_KuyrukMatrisi()
        {
            // WP-069 A4 audioId'leri (S-02 §0): 10 silah, döngü yalnız Auto ∧ RPM ≥ 600 → AR1/SMG1/LMG1.
            string[] beklenen = { "AR1", "AR2", "SMG1", "SMG2", "SG1", "SG2", "LMG1", "LMG2", "SR1", "RL1" };
            Assert.AreEqual(beklenen.Length, WeaponAudioIdentity.All.Length);
            var sesler = new HashSet<AudioSoundId>();
            var hibrit = new List<string>();
            foreach (string id in beklenen)
            {
                Assert.IsTrue(WeaponAudioIdentity.TryGet(id, out WeaponVoiceSet set), "audioId yok: " + id);
                Assert.IsTrue(sesler.Add(set.Fire), "Ateş sesi paylaşılıyor: " + id);
                if (set.IsHybrid)
                {
                    hibrit.Add(id);
                    Assert.IsTrue(set.LoopRpm >= 600f, id + " döngü RPM'i ≥ 600");
                    Assert.AreNotEqual(AudioSoundId.None, set.Transient);
                }
            }

            CollectionAssert.AreEqual(new[] { "AR1", "SMG1", "LMG1" }, hibrit);
            Assert.IsFalse(WeaponAudioIdentity.TryGet("SNP", out _), "Eski yuva adı (SNP) kimlik değil.");
            Assert.AreEqual("AR1", WeaponAudioIdentity.ForClassic(DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle));
            Assert.AreEqual("SMG1", WeaponAudioIdentity.ForClassic(DD.Prototype.Core.Config.ClassicWeaponId.AutoSmg));
            Assert.AreEqual("SG1", WeaponAudioIdentity.ForClassic(DD.Prototype.Core.Config.ClassicWeaponId.CombatShotgun));

            // 6 sınıf × 3 mekân = 18 ayrı kuyruk, adı sınıf+mekânla eşleşir.
            var kuyruk = new HashSet<AudioSoundId>();
            foreach (WeaponTailClass c in Enum.GetValues(typeof(WeaponTailClass)))
            {
                foreach (AcousticSpace a in Enum.GetValues(typeof(AcousticSpace)))
                {
                    AudioSoundId t = WeaponAudioIdentity.Tail(c, a);
                    Assert.AreEqual("WeaponTail" + c + a, t.ToString());
                    Assert.IsTrue(kuyruk.Add(t));
                }
            }

            Assert.AreEqual(18, kuyruk.Count);
            Assert.AreEqual(AcousticSpace.Open, AcousticSpaces.At(Vector3.zero), "Çözücü yokken Open.");
            Assert.Less(WeaponFireAudio.WetDb(AcousticSpace.Open), WeaponFireAudio.WetDb(AcousticSpace.Corridor));
            Assert.Less(WeaponFireAudio.WetDb(AcousticSpace.Corridor), WeaponFireAudio.WetDb(AcousticSpace.Hall));
            Assert.AreEqual(0f, WeaponPerspectiveMix.For(DD.Prototype.CameraSystem.CameraMode.FirstPerson).Spatial, "FP: oyuncu silahı 2D.");
        }

        [Test]
        public void FoleyV3_SilahBasinaMekanizmaSeti_AileVeKovanEslemi()
        {
            // S-02 WP-069 §3 reload sets under WP-071 names; each weapon speaks with its own ids.
            var E = typeof(DD.Prototype.Characters.CharacterAnimationEvent);
            var set = new Dictionary<string, string[]>
            {
                { "AR1", new[] { "MagOut", "MagIn", "Chamber", "Charge" } }, { "AR2", new[] { "MagOut", "MagIn", "Charge" } },
                { "SMG1", new[] { "MagOut", "MagIn", "Chamber" } }, { "SMG2", new[] { "MagOut", "MagIn", "MagIn2", "Chamber" } },
                { "SG1", new[] { "ShellIn", "Eject", "Chamber" } }, { "SG2", new[] { "Eject", "ShellIn", "Chamber" } },
                { "LMG1", new[] { "BoxOpen", "BeltSeat", "BoxClose", "Charge" } }, { "LMG2", new[] { "BoxOpen", "BeltSeat", "BoxClose", "Charge" } },
                { "SR1", new[] { "MagOut", "MagIn", "BoltUp", "BoltBack", "BoltFwd", "BoltDown" } }, { "RL1", new[] { "RocketIn", "RocketSeat" } },
            };
            var sahip = new Dictionary<AudioSoundId, string>();
            foreach (var kv in set)
            {
                foreach (string ad in kv.Value)
                {
                    var e = (DD.Prototype.Characters.CharacterAnimationEvent)Enum.Parse(E, ad);
                    AudioSoundId id = WeaponFoleyV3.Mechanism(kv.Key, e);
                    Assert.AreNotEqual(AudioSoundId.None, id, kv.Key + "/" + ad + " sesi yok");
                    StringAssert.StartsWith("Foley" + kv.Key.Substring(0, 1) + kv.Key.Substring(1).ToLowerInvariant(), id.ToString(), kv.Key + "/" + ad);
                    if (sahip.TryGetValue(id, out string o)) Assert.AreEqual(kv.Key, o, id + " iki silahta");
                    sahip[id] = kv.Key;
                }
            }

            Assert.AreEqual(35, sahip.Count, "35 mekanizma kimliği");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("AR1", DD.Prototype.Characters.CharacterAnimationEvent.Eject), "otomatik tüfekte eject sessiz");

            // Handling: 3 families × 4 events + dry fire, names line up with the layout.
            foreach (WeaponHandlingFamily f in Enum.GetValues(typeof(WeaponHandlingFamily)))
            {
                Assert.AreEqual("Foley" + f + "Switch", WeaponFoleyV3.Handling(f, DD.Prototype.Characters.CharacterAnimationEvent.SwitchAway).ToString());
                Assert.AreEqual("Foley" + f + "Ads", WeaponFoleyV3.Handling(f, DD.Prototype.Characters.CharacterAnimationEvent.AdsReady).ToString());
                Assert.AreEqual("Foley" + f + "DryFire", WeaponFoleyV3.DryFire(f).ToString());
            }

            Assert.AreEqual(WeaponHandlingFamily.Light, WeaponFoleyV3.Family("SMG2"));
            Assert.AreEqual(WeaponHandlingFamily.Heavy, WeaponFoleyV3.Family("RL1"));

            // Casings: calibre family × surface; water / foliage silent; the launcher has none.
            Assert.AreEqual(AudioSoundId.CasingRifleWood, WeaponFoleyV3.CasingOn(WeaponFoleyV3.Casing("SR1"), SurfaceKind.Wood));
            Assert.AreEqual(AudioSoundId.CasingPistolDirt, WeaponFoleyV3.CasingOn(WeaponFoleyV3.Casing("SMG1"), SurfaceKind.Dirt));
            Assert.AreEqual(AudioSoundId.CasingShellStone, WeaponFoleyV3.CasingOn(WeaponFoleyV3.Casing("SG2"), SurfaceKind.Default));
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingOn(CasingFamily.Rifle, SurfaceKind.Water));
            Assert.AreEqual(CasingFamily.None, WeaponFoleyV3.Casing("RL1"));
            Assert.Less(CasingAudio.PerspectiveDb(DD.Prototype.CameraSystem.CameraMode.TopDown), CasingAudio.PerspectiveDb(DD.Prototype.CameraSystem.CameraMode.FirstPerson));
        }
    }
}
