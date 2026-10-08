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
using UnityEngine.TestTools.Constraints;   // WP-085 G2: Is.Not.AllocatingGCMemory() extension
using Is = UnityEngine.TestTools.Constraints.Is;   // derives from NUnit's Is: Is.InRange etc. still resolve; no CS0104 ambiguity

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-021 · Ses omurgası EditMode dilimi: olay haritası tamlığı (ADR-021-1), katalog
    /// dönüştürücü idempotansı, manifest LUFS/tepe standardı (BS.1770 ölçerle yeniden
    /// ölçüm), mixer grup/snapshot tablosu, bölge haritası.
    /// </summary>
    [TestFixture]
    public sealed class AudioSliceEditModeTests
    {
        // ------------------------------------------------------------------ olay haritası

        [Category("WP021")]
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

        [Category("WP021")]
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
        [Category("WP021")]
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
        [Category("WP021")]
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
        [Category("WP021")]
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

            // The WP-048B table owns A/B/C (unique rows); WP-069 B2's other weapons borrow their class sibling's row
            // (Arsenal.LegacyPoseIndex) as the fallback under WP-072 B2's per-weapon WeaponFoleyV3 set.
            var ids = new HashSet<AudioSoundId>();
            var olaylar = new[] { DD.Prototype.Characters.CharacterAnimationEvent.Eject, DD.Prototype.Characters.CharacterAnimationEvent.Chamber, DD.Prototype.Characters.CharacterAnimationEvent.MagOut, DD.Prototype.Characters.CharacterAnimationEvent.MagIn, DD.Prototype.Characters.CharacterAnimationEvent.SwitchReady, DD.Prototype.Characters.CharacterAnimationEvent.Lowered, DD.Prototype.Characters.CharacterAnimationEvent.Raised };
            foreach (DD.Prototype.Core.Config.ClassicWeaponId w in Enum.GetValues(typeof(DD.Prototype.Core.Config.ClassicWeaponId)))
            {
                foreach (var e in olaylar)
                {
                    AudioSoundId id = AudioEventMap.WeaponFoley(w, e);
                    Assert.IsTrue(id.ToString().StartsWith("Weapon"), w + "/" + e + " → " + id);
                    if ((int)w <= 2) Assert.IsTrue(ids.Add(id), "A/B/C silah foley kimliği paylaşılıyor: " + id);
                    else Assert.IsTrue(ids.Contains(id), w + " sınıf kardeşinin A/B/C satırını kullanmalı: " + id);
                }
            }

            Assert.AreEqual(21, ids.Count);
            Assert.AreEqual(AudioSoundId.WeaponCChamber, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.CombatShotgun, DD.Prototype.Characters.CharacterAnimationEvent.Chamber));
            Assert.AreEqual(AudioSoundId.WeaponBMagIn, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.AutoSmg, DD.Prototype.Characters.CharacterAnimationEvent.MagIn3));
            Assert.AreEqual(AudioSoundId.None, AudioEventMap.WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, DD.Prototype.Characters.CharacterAnimationEvent.FootLeft));
        }

        /// <summary>WP-058B · AP-015P plate → footstep variant rule (WP-038 plates carry Dirt/Stone/Water tags only).</summary>
        [Category("WP021")]
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

        /// <summary>WP-072 B4 · DEC-094: the Raiju / Namekuji usage rows replace the WP-058 Kagura / Ofuda / Tide set (ids 310–332 gone).</summary>
        [Category("WP021")]
        [Test]
        public void SpecialKullanim_RaijuNamekuji_Satirlari_EskiKimliklerYok()
        {
            string[] satirlar =
            {
                "SpecialUsage.Activated → Raiju / Namekuji", "SpecialUsage.ChargeStarted → Raiju", "SpecialUsage.Fired → Raiju / Namekuji",
                "SpecialUsage.Hit → Raiju (Count = hop)", "SpecialUsage.Hit → Namekuji", "SpecialUsage.FieldSpawned → Namekuji (Count = puddle id)",
                "SpecialUsage.FieldEnded → Raiju / Namekuji", "SpecialUsage.SelfShock → Raiju", "SpecialUsage.ReloadStarted / ReloadEnded → Raiju / Namekuji",
                "SpecialUsage.PlayerSlideStarted → Namekuji"
            };
            foreach (string satir in satirlar)
            {
                Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Combat, satir, out AudioEventBinding b), "Matris satırı yok: " + satir);
                Assert.IsFalse(b.Reserved, satir);
                StringAssert.StartsWith("Sw", b.Sound.ToString(), satir);
            }

            Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Damage, "EnemyCombatEvents.sw_shock", out _), "sw_shock satırı");
            Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Damage, "EnemyCombatEvents.sw_slide / sw_fall", out _), "sw_slide satırı");
            foreach (string eski in new[] { "SwFireKagura", "SwFieldTide", "SwFieldEndOfuda", "SwHitTide", "ZombieReactPush", "ZombieReactPull" })
            {
                Assert.IsFalse(Enum.IsDefined(typeof(AudioSoundId), eski), eski + " kaldırılmalıydı (DEC-094)");
            }
        }

        /// <summary>WP-048 · AP-017 breakable event table (AP017-breakable-event-table-v001): every family root name resolves to its own voice.</summary>
        [Category("WP021")]
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
        [Category("WP021")]
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
        [Category("WP021")]
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

        [Category("WP021")]
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

        [Category("WP021")]
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

        [Category("WP021")]
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

        /// <summary>
        /// WP-085 G2: the shipped catalog (as committed — not rebuilt here) has no null slot, every Sound once, ≥ 1 non-null
        /// clip per entry, and TryGet returns exactly that entry. A duplicate used to make TryGet rebuild its index on every call.
        /// </summary>
        [Category("WP021")]
        [Test]
        public void Katalog_SevkEdilen_NullGirdiYok_SoundTekil_HerGirdideKlipVar()
        {
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog, "sevk edilen katalog yüklenmedi: " + AudioCatalogBuilder.CatalogPath);
            Assert.AreSame(katalog, Resources.Load<AudioCatalog>(AudioCatalog.ResourcePath), "oyunun yüklediği (Resources) aynı varlık olmalı");
            Assert.Greater(katalog.Entries.Count, 0, "katalog boş — ölçülecek girdi yok");

            var gorulen = new HashSet<AudioSoundId>();
            var hatalar = new List<string>();
            int klip = 0;
            for (int i = 0; i < katalog.Entries.Count; i++)
            {
                AudioCatalog.Entry e = katalog.Entries[i];
                if (e == null)
                {
                    hatalar.Add("#" + i + ": null girdi");
                    continue;
                }

                if (e.Sound == AudioSoundId.None || !Enum.IsDefined(typeof(AudioSoundId), e.Sound)) hatalar.Add("#" + i + ": tanımsız ses " + (int)e.Sound);
                if (!gorulen.Add(e.Sound)) hatalar.Add("#" + i + ": " + e.Sound + " ikinci kez");
                if (e.Clips == null || e.Clips.Length == 0)
                {
                    hatalar.Add(e.Sound + ": klip yok");
                    continue;
                }

                for (int c = 0; c < e.Clips.Length; c++)
                {
                    if (e.Clips[c] == null) hatalar.Add(e.Sound + ": klip " + c + " null");
                    else klip++;
                }

                if (!katalog.TryGet(e.Sound, out AudioCatalog.Entry bulunan) || !ReferenceEquals(bulunan, e)) hatalar.Add(e.Sound + ": TryGet bu girdiyi döndürmüyor");
            }

            Assert.IsEmpty(hatalar, "Katalog tekillik ihlali (" + hatalar.Count + "): " + string.Join("; ", hatalar));
            Assert.AreEqual(katalog.Entries.Count, gorulen.Count, "her girdi ayrı bir ses olmalı");
            Debug.Log("[DD-TEST] wp085.g2.catalog entries=" + katalog.Entries.Count + " unique=" + gorulen.Count + " clips=" + klip + " version=" + katalog.ManifestVersion);
        }

        /// <summary>
        /// WP-085 G2 regression: a catalog with the same Sound twice (3 entries, 2 keys). The old check
        /// (_index.Count != _entries.Length → 2 != 3, never equal) rebuilt the dictionary on EVERY TryGet; the
        /// second and later calls must allocate nothing. Statement lambdas on purpose: an expression lambda that
        /// returns bool binds to ActualValueDelegate and is evaluated outside the GC recorder.
        /// </summary>
        [Category("WP021")]
        [Test]
        public void Katalog_AyniSoundIkiKez_IkinciTryGetAyirmaz()
        {
            AudioClip klip = AudioClip.Create("wp085_g2_dup", 480, 1, 48000, false);
            var k = ScriptableObject.CreateInstance<AudioCatalog>();
            try
            {
                k.Set("wp085-g2-test", "g2-dup", new List<AudioCatalog.Entry>
                {
                    new AudioCatalog.Entry { Sound = AudioSoundId.RifleFire, Clips = new[] { klip } },
                    new AudioCatalog.Entry { Sound = AudioSoundId.RifleFire, Clips = new[] { klip } },   // aynı ses ikinci kez
                    new AudioCatalog.Entry { Sound = AudioSoundId.SmgFire, Clips = new[] { klip } }
                });
                Assert.AreEqual(3, k.Entries.Count, "yineleme kurulumda durmalı (indeks 2 anahtar, girdi 3)");

                Assert.IsTrue(k.TryGet(AudioSoundId.RifleFire, out AudioCatalog.Entry ilk), "ilk çağrı indeksi kurar ve bulur");
                Assert.IsTrue(k.TryGet(AudioSoundId.SmgFire, out _));
                Assert.IsFalse(k.TryGet(AudioSoundId.ShotgunFire, out _), "katalogda olmayan ses");

                object[] yem = null;
                Assert.That(() => { yem = new object[256]; for (int i = 0; i < yem.Length; i++) yem[i] = new byte[64]; },
                    Is.AllocatingGCMemory(), "sayaç geçersiz (pozitif kontrol: kayıtçı ayırmayı görmeli)");
                GC.KeepAlive(yem);

                bool bulundu = false;
                AudioCatalog.Entry sonra = null;
                Assert.That(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        bulundu = k.TryGet(AudioSoundId.RifleFire, out sonra);
                        k.TryGet(AudioSoundId.ShotgunFire, out _);
                    }
                }, Is.Not.AllocatingGCMemory(), "yineleme varken TryGet indeksi yeniden kurdu (her çağrıda sözlük ayırma)");
                Assert.IsTrue(bulundu, "ikinci ve sonraki çağrılar da bulmalı");
                Assert.AreSame(ilk, sonra, "aynı indeks, aynı girdi");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(k);
                UnityEngine.Object.DestroyImmediate(klip);
            }
        }

        /// <summary>
        /// WP-085 G2: with the count check gone, Set() and OnAfterDeserialize (load / in-place reimport / undo) are the only
        /// invalidations — both must lead to a rebuild, and only one. Same entry count throughout, which the old check could not see.
        /// </summary>
        [Category("WP021")]
        [Test]
        public void Katalog_SetVeOnAfterDeserialize_IndeksiYenidenKurar()
        {
            AudioClip klip = AudioClip.Create("wp085_g2_inval", 480, 1, 48000, false);
            var k = ScriptableObject.CreateInstance<AudioCatalog>();
            try
            {
                k.Set("a", "a", new List<AudioCatalog.Entry> { new AudioCatalog.Entry { Sound = AudioSoundId.RifleFire, Clips = new[] { klip } } });
                Assert.IsTrue(k.TryGet(AudioSoundId.RifleFire, out _));
                Assert.IsFalse(k.TryGet(AudioSoundId.SmgFire, out _));

                k.Set("b", "b", new List<AudioCatalog.Entry> { new AudioCatalog.Entry { Sound = AudioSoundId.SmgFire, Clips = new[] { klip } } });
                Assert.IsTrue(k.TryGet(AudioSoundId.SmgFire, out _), "Set sonrası yeni ses bulunmalı");
                Assert.IsFalse(k.TryGet(AudioSoundId.RifleFire, out _), "Set sonrası eski ses kalmamalı");

                // Serialized data rewritten under the live instance (what an in-place reimport does), then Unity's callback.
                k.Entries[0].Sound = AudioSoundId.ShotgunFire;
                ((ISerializationCallbackReceiver)k).OnAfterDeserialize();
                bool bulundu = false;
                Assert.That(() => { bulundu = k.TryGet(AudioSoundId.ShotgunFire, out _); }, Is.AllocatingGCMemory(),
                    "OnAfterDeserialize sonrası ilk TryGet indeksi yeniden kurmalı");
                Assert.IsTrue(bulundu, "yeni veri indekste");
                Assert.IsFalse(k.TryGet(AudioSoundId.SmgFire, out _), "eski anahtar indeksten düşmeli");
                Assert.That(() => { k.TryGet(AudioSoundId.ShotgunFire, out _); }, Is.Not.AllocatingGCMemory(), "yeniden kurulumdan sonra ayırma yok");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(k);
                UnityEngine.Object.DestroyImmediate(klip);
            }
        }

        /// <summary>WP-085 G2: the builder's overlay step — AP-027 wins per sound, AP-011 keeps the rest; a sound twice in one manifest throws.</summary>
        [Category("WP021")]
        [Test]
        public void KatalogDonusturucu_AP027UsteBiner_ManifestteYinelenenSesHataVerir()
        {
            AudioCatalogBuilder.ManifestEntry Satir(string ses) => new AudioCatalogBuilder.ManifestEntry { sound = ses, files = new[] { ses + "_01.wav" } };
            string tufek = nameof(AudioSoundId.RifleFire), smg = nameof(AudioSoundId.SmgFire);
            var ap011 = new AudioCatalogBuilder.Manifest { version = "t011", entries = new[] { Satir(tufek), Satir(smg) } };
            var ap027 = new AudioCatalogBuilder.Manifest { version = "t027", entries = new[] { Satir(tufek) } };

            var satirlar = AudioCatalogBuilder.MergeRows(ap011, ap027);
            Assert.AreEqual(2, satirlar.Count, "RifleFire bir kez (AP-027) + SmgFire (AP-011)");
            foreach (var s in satirlar)
            {
                Assert.AreEqual(s.entry.sound == tufek ? AudioCatalogBuilder.Ap027AudioRoot : AudioCatalogBuilder.AudioRoot, s.root, s.entry.sound + " kökü");
            }

            Assert.AreEqual(2, AudioCatalogBuilder.MergeRows(ap011, null).Count, "AP-027 yokken AP-011 olduğu gibi");

            var ap027Cift = new AudioCatalogBuilder.Manifest { version = "t027b", entries = new[] { Satir(tufek), Satir(tufek) } };
            var hata = Assert.Throws<InvalidOperationException>(() => AudioCatalogBuilder.MergeRows(ap011, ap027Cift));
            StringAssert.Contains("AP-027 " + tufek, hata.Message);

            var ap011Cift = new AudioCatalogBuilder.Manifest { version = "t011b", entries = new[] { Satir(smg), Satir(smg) } };
            hata = Assert.Throws<InvalidOperationException>(() => AudioCatalogBuilder.MergeRows(ap011Cift, ap027));
            StringAssert.Contains("AP-011 " + smg, hata.Message);
        }

        [Category("WP021")]
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
            // WP-085 B5: AP-027 rows replace AP-011's; they follow Codex's per-layer standard (Fire −18, tails −20, casings −24,
            // links −30, Mechanical a stem) checked at delivery — here they keep the shipped-clip re-measurement and the peak gates.
            var v4 = new HashSet<string>();
            if (File.Exists(AudioCatalogBuilder.Ap027ManifestPath))
            {
                foreach (AudioCatalogBuilder.ManifestEntry m in JsonUtility.FromJson<AudioCatalogBuilder.Manifest>(File.ReadAllText(AudioCatalogBuilder.Ap027ManifestPath)).entries)
                {
                    girdi[m.sound] = m;
                    v4.Add(m.sound);
                }
            }
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
                if (!v4.Contains(m.sound) && (hedef < altSinir || hedef > -15.9f)) hatalar.Add($"{e.Sound}: hedef {hedef} standart kümede değil ({(m.bus == "UI" ? "-16…-30 UI" : m.bus == "Ambience" ? "-16…-26 ambiyans" : "-16…-22")})");
                // AP-027's own ceiling is −1 dBTP (Codex standard, checked per file at delivery); AP-011 rows keep the house −1.5.
                if (tavan > (v4.Contains(m.sound) ? -1f : manifest.standard.truePeakMax) + 0.01f) hatalar.Add($"{e.Sound}: tavan {tavan} dBTP > standart {manifest.standard.truePeakMax}");
                // Standart: hedef ± tolerans; ya da tavana dayanmış ve hedefin ALTINDA (kısa transientler
                // tavanda hedefe ulaşamaz - tepe kısıtı seviyeyi belirler, bu doğru davranıştır).
                bool tepeSinirinda = e.TruePeakDb >= tavan - 0.15f && e.Lufs <= hedef + 0.1f;
                if (!v4.Contains(m.sound) && Mathf.Abs(e.Lufs - hedef) > tol && !tepeSinirinda)
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
                    // AP-027 Mechanical stems sit near −50 LUFS by design (a layer under Fire); BS.1770's −70 gate can read them silent.
                    if (!(v4.Contains(m.sound) && m.sound.EndsWith("Mechanical", StringComparison.Ordinal))) hatalar.Add($"{e.Sound}: klip sessiz ({clip.name})");
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
        [Category("WP021")]
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
            var satirlar = new List<(AudioCatalogBuilder.ManifestEntry m, string root)>();
            foreach (AudioCatalogBuilder.ManifestEntry m in manifest.entries) satirlar.Add((m, AudioCatalogBuilder.AudioRoot));
            if (File.Exists(AudioCatalogBuilder.Ap027ManifestPath))
                foreach (AudioCatalogBuilder.ManifestEntry m in JsonUtility.FromJson<AudioCatalogBuilder.Manifest>(File.ReadAllText(AudioCatalogBuilder.Ap027ManifestPath)).entries)
                    satirlar.Add((m, AudioCatalogBuilder.Ap027AudioRoot));   // WP-085 B5: the same gate on the AP-027 import (its stamps from ap027_import.py)
            foreach (var (m, kok) in satirlar)
            {
                if (m.bitDepth != manifest.standard.bitDepth) hatalar.Add($"{m.sound}: kaynak {m.bitDepth}-bit ≠ {manifest.standard.bitDepth}");
                if (m.clipped > 0) hatalar.Add($"{m.sound}: {m.clipped} klip örneği");
                if (m.dcDb > -60f) hatalar.Add($"{m.sound}: DC {m.dcDb:0.0} dBFS");
                // Gürültü tabanı = ham take'in en sessiz 100 ms'i (kaynak arka planı); sürekli malzeme (burst, loop) muaf.
                if (!m.continuous && m.noiseFloorDb > manifest.standard.noiseFloorMaxDb)
                {
                    // WP-085 B5: a named, reasoned exception in the manifest (ap027_import.py NOISE_FLOOR_EXCEPTION) is logged, not failed.
                    if (string.IsNullOrEmpty(m.noiseFloorException)) hatalar.Add($"{m.sound}: kaynak gürültü tabanı {m.noiseFloorDb:0.0} dBFS > {manifest.standard.noiseFloorMaxDb}");
                    else Debug.Log($"[DD-TEST] noise-floor exception {m.sound} {m.noiseFloorDb:0.0} dBFS: {m.noiseFloorException}");
                }
                if (!m.loop && m.endSilenceDb > -70f) hatalar.Add($"{m.sound}: dosya sonu sessiz değil ({m.endSilenceDb:0.0} dBFS)");

                for (int f = 0; f < m.files.Length; f++)
                {
                    string path = kok + m.files[f];
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

        [Category("WP021")]
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
        [Category("WP021")]
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

        [Category("WP021")]
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

        [Category("WP021")]
        [Test]
        public void BolgeHaritasi_NoktaPoligonTesti()
        {
            float[] kare = { 0f, 0f, 10f, 0f, 10f, 10f, 0f, 10f };
            Assert.IsTrue(AudioRegionMap.Contains(kare, 5f, 5f));
            Assert.IsFalse(AudioRegionMap.Contains(kare, 15f, 5f));
            Assert.IsFalse(AudioRegionMap.Contains(new float[] { 0f, 0f, 1f, 1f }, 0.5f, 0.5f), "3 köşeden az = dış");
        }

        [Category("WP021")]
        [Test]
        public void AtesV4_KimlikTablosu_WP069AudioIdleri_SeriRpmWDIleAyni_KuyrukMatrisi()
        {
            // WP-069 A4 audioId'leri (S-02 §0): 10 silah. WP-085 B1: hibrit yok; seri ateşleyenler (Auto / Burst) kuyruğu
            // bırakışta çalar ve seri hızı silahın WD ateş bloğuyla aynıdır (Burst: seri içi hız = 60 / burstIntervalSeconds).
            string[] beklenen = { "AR1", "AR2", "SMG1", "SMG2", "SG1", "SG2", "LMG1", "LMG2", "SR1", "RL1" };
            Assert.AreEqual(beklenen.Length, WeaponAudioIdentity.All.Length);
            var sesler = new HashSet<AudioSoundId>();
            var seri = new List<string>();
            foreach (string id in beklenen)
            {
                Assert.IsTrue(WeaponAudioIdentity.TryGet(id, out WeaponVoiceSet set), "audioId yok: " + id);
                Assert.IsTrue(sesler.Add(set.Fire), "Ateş sesi paylaşılıyor: " + id);
                string weaponId = id.Substring(0, id.Length - 1).ToLowerInvariant() + "_" + id.Substring(id.Length - 1);
                var wd = Resources.Load<DD.Prototype.Core.Weapons.WeaponDefinition>("Arsenal/WD_" + weaponId);
                Assert.IsNotNull(wd, "Resources/Arsenal/WD_" + weaponId + " yok");
                float beklenenRpm;
                switch (wd.fire.fireMode)
                {
                    case DD.Prototype.Core.Weapons.WeaponFireMode.Auto: beklenenRpm = wd.fire.rpm; break;
                    case DD.Prototype.Core.Weapons.WeaponFireMode.Burst: beklenenRpm = 60f / wd.fire.burstIntervalSeconds; break;
                    default: beklenenRpm = 0f; break;
                }

                Assert.AreEqual(beklenenRpm, set.StringRpm, 0.5f, id + " seri hızı WD ile aynı olmalı");
                Assert.IsTrue(set.FireTrimDb <= 0f, id + " kırpma yalnız aşağı");
                if (set.FiresStrings) seri.Add(id);
            }

            CollectionAssert.AreEqual(new[] { "AR1", "AR2", "SMG1", "SMG2", "LMG1", "LMG2" }, seri);
            Assert.AreEqual(4, AudioMixPlan.PlayerWeapon(AudioSoundId.WeaponSmg1Fire, WeaponFireAudio.StringVoices).MaxInstances, "Otomatik: 4 canlı kopya.");
            Assert.AreEqual(3, AudioMixPlan.PlayerWeapon(AudioSoundId.WeaponSg1Fire).MaxInstances, "Tek atış: eski tavan 3.");
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
            // R3 (kullanıcı 06.10: testler yalnız top down): the FP weapon's 2D check is AtesV4_KimlikTablosu_FpSilah2D_TPFP.
        }

        /// <summary>R3 FPTP half of AtesV4_KimlikTablosu (C-00 07.10: mixed tests split): the FP player weapon is 2D.</summary>
        [Category("WP021")]
        [Test]
        [Category("FPTP")]
        public void AtesV4_KimlikTablosu_FpSilah2D_TPFP()
        {
            Assert.AreEqual(0f, WeaponPerspectiveMix.For(DD.Prototype.CameraSystem.CameraMode.FirstPerson).Spatial, "FP: oyuncu silahı 2D.");
        }

        [Category("WP021")]
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

            // WP-085 B2: metal rings like stone, paper / tatami lands soft; hard surfaces skip once; the queue holds 900 RPM × 0.6 s.
            Assert.AreEqual(AudioSoundId.CasingRifleStone, WeaponFoleyV3.CasingOn(CasingFamily.Rifle, SurfaceKind.Metal));
            Assert.AreEqual(AudioSoundId.CasingPistolDirt, WeaponFoleyV3.CasingOn(CasingFamily.Pistol, SurfaceKind.Paper));
            Assert.IsTrue(WeaponFoleyV3.CasingBounces(SurfaceKind.Stone) && WeaponFoleyV3.CasingBounces(SurfaceKind.Metal) && WeaponFoleyV3.CasingBounces(SurfaceKind.Wood));
            Assert.IsFalse(WeaponFoleyV3.CasingBounces(SurfaceKind.Dirt) || WeaponFoleyV3.CasingBounces(SurfaceKind.Water) || WeaponFoleyV3.CasingBounces(SurfaceKind.Foliage));
            Assert.GreaterOrEqual(CasingAudio.Capacity, Mathf.CeilToInt(900f / 60f * CasingAudio.MaxFall), "900 RPM'de havadaki kovanlar sığmalı");

            // WP-085 B5: AP-027 per-weapon casings — stone as StoneSlate (WetStone under the wet look), wood / tatami Wood, dirt, water a puddle.
            Assert.AreEqual(AudioSoundId.CasingAr1StoneSlate, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Stone, false));
            Assert.AreEqual(AudioSoundId.CasingAr1WetStone, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Default, true));
            Assert.AreEqual(AudioSoundId.CasingSg1Wood, WeaponFoleyV3.CasingV4("SG1", SurfaceKind.Paper, true));
            Assert.AreEqual(AudioSoundId.CasingSmg1Puddle, WeaponFoleyV3.CasingV4("SMG1", SurfaceKind.Water, true));
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Foliage, true), "çimde ses yok");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV4("RL1", SurfaceKind.Stone, false), "roketatarın kovanı yok");
            Assert.AreEqual(CasingAudio.V4FileCompDb - CasingAudio.V3FileCompDb, 2f, 1e-4f, "AP-027 kovan dosyası −24 LUFS: +6 telafi");
            Assert.IsTrue(CasingAudio.MinFall >= 0.25f && CasingAudio.MaxFall < CasingAudio.LatestLanding && CasingAudio.LatestLanding <= 0.7f, "R2 S5: 250–700 ms bandı (aynı kare kademesi dahil)");
            // R3 (kullanıcı 06.10: testler yalnız top down): the casing's top-down level itself; its order against FP is
            // FoleyV3_KovanPerspektifSirasi_TPFP.
            Assert.AreEqual(-18f, CasingAudio.PerspectiveDb(DD.Prototype.CameraSystem.CameraMode.TopDown), 1e-4f, "STANDARDS §4: tepeden kovan −18 dB (ateşe göre)");
        }

        /// <summary>R3 FPTP half of FoleyV3_SilahBasinaMekanizmaSeti (C-00 07.10): the top-down casing sits under the FP one.</summary>
        [Category("WP021")]
        [Test]
        [Category("FPTP")]
        public void FoleyV3_KovanPerspektifSirasi_TPFP()
        {
            Assert.Less(CasingAudio.PerspectiveDb(DD.Prototype.CameraSystem.CameraMode.TopDown), CasingAudio.PerspectiveDb(DD.Prototype.CameraSystem.CameraMode.FirstPerson));
        }

        [Category("WP021")]
        [Test]
        public void DarbeV3_YuzeyKalibreMatrisi_AdlarVeKazanc()
        {
            var ids = new HashSet<AudioSoundId>();
            for (int sf = 0; sf <= (int)SurfaceKind.Paper; sf++)
            {
                foreach (DD.Prototype.Core.Weapons.WeaponCaliber c in new[] { DD.Prototype.Core.Weapons.WeaponCaliber.Light, DD.Prototype.Core.Weapons.WeaponCaliber.Medium, DD.Prototype.Core.Weapons.WeaponCaliber.Heavy })
                {
                    AudioSoundId id = ImpactAudio.For((SurfaceKind)sf, c);
                    Assert.AreEqual("ImpactV3" + (SurfaceKind)sf + c, id.ToString());
                    Assert.IsTrue(ids.Add(id));
                }

                Assert.Less(ImpactAudio.GainDb((SurfaceKind)sf, DD.Prototype.Core.Weapons.WeaponCaliber.Light), ImpactAudio.GainDb((SurfaceKind)sf, DD.Prototype.Core.Weapons.WeaponCaliber.Heavy), "ağır kalibre daha yüksek");
            }

            Assert.AreEqual(24, ids.Count);
            Assert.AreEqual(AudioSoundId.None, ImpactAudio.For(SurfaceKind.Flesh, DD.Prototype.Core.Weapons.WeaponCaliber.Medium), "et hasar yolunda");
            Assert.AreEqual(AudioSoundId.None, ImpactAudio.For(SurfaceKind.Stone, DD.Prototype.Core.Weapons.WeaponCaliber.Explosive), "patlama kendi yolunda");
        }

        [Category("WP021")]
        [Test]
        public void Firlatici_UcusDongusuEslemi_PitchRampasi_PatlamaKatmani()
        {
            Assert.AreEqual(AudioSoundId.RocketFlightLoop, ProjectileFlightAudio.LoopFor(DD.Prototype.Core.Weapons.ProjectileKind.Rocket));
            Assert.AreEqual(AudioSoundId.SwArcSphereLoop, ProjectileFlightAudio.LoopFor(DD.Prototype.Core.Weapons.ProjectileKind.Orb));
            Assert.AreEqual(AudioSoundId.SwGelFlightLoop, ProjectileFlightAudio.LoopFor(DD.Prototype.Core.Weapons.ProjectileKind.Gel));
            Assert.AreEqual(AudioSoundId.None, ProjectileFlightAudio.LoopFor(DD.Prototype.Core.Weapons.ProjectileKind.Bullet), "mermide döngü yok");
            Assert.AreEqual(1f, ProjectileFlightAudio.PitchFor(40f, DD.Prototype.CameraSystem.CameraMode.TopDown), 1e-5f, "tepeden pitch rampası yok");
            Assert.AreEqual(1f, ProjectileFlightAudio.PitchFor(60f, DD.Prototype.CameraSystem.CameraMode.TopDown), 1e-5f, "tepeden 45 m+ da rampa yok (R3: FP rampası Firlatici_PitchRampasi_TPFP)");
            Assert.AreEqual(AudioSoundId.ExplosionNear, PrototypeAudioDirector.ExplosionLayer(12f));
            Assert.AreEqual(AudioSoundId.ExplosionMid, PrototypeAudioDirector.ExplosionLayer(12.1f));
            Assert.AreEqual(AudioSoundId.ExplosionFar, PrototypeAudioDirector.ExplosionLayer(35.1f));
        }

        /// <summary>R3 FPTP half of Firlatici_UcusDongusuEslemi (C-00 07.10): the FP flight loop drops 2 st past 45 m.</summary>
        [Category("WP021")]
        [Test]
        [Category("FPTP")]
        public void Firlatici_PitchRampasi_TPFP()
        {
            Assert.AreEqual(Mathf.Pow(2f, -2f / 12f), ProjectileFlightAudio.PitchFor(60f, DD.Prototype.CameraSystem.CameraMode.FirstPerson), 1e-4f, "45 m+ → −2 st");
        }

        [Category("WP021")]
        [Test]
        public void SilahFoleyV3_KlipSetineGore_MekanizmaEslemesi()
        {
            // WP-072 V6 mix audit: the legacy A/B/C clips vs the AP-022 v3 sets.
            const DD.Prototype.Characters.CharacterAnimationEvent MagOut = DD.Prototype.Characters.CharacterAnimationEvent.MagOut,
                MagIn = DD.Prototype.Characters.CharacterAnimationEvent.MagIn, MagIn3 = DD.Prototype.Characters.CharacterAnimationEvent.MagIn3,
                Eject = DD.Prototype.Characters.CharacterAnimationEvent.Eject, Chamber = DD.Prototype.Characters.CharacterAnimationEvent.Chamber,
                BoxOpen = DD.Prototype.Characters.CharacterAnimationEvent.BoxOpen, BoltBack = DD.Prototype.Characters.CharacterAnimationEvent.BoltBack;
            Assert.AreEqual(AudioSoundId.FoleyRl1RocketIn, WeaponFoleyV3.Mechanism("RL1", MagOut), "RL1 eski A klibi: mag_out = roket tüpe");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("SG2", Eject, false, false), "SG2 kırma: atış başına pompa yok");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("SG2", Chamber, false, false));
            Assert.AreEqual(AudioSoundId.FoleySg2Chamber, WeaponFoleyV3.Mechanism("SG2", Chamber, false, true), "SG2 reload sonunda kapanır");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("SG2", MagIn3, false, true), "SG2 iki fişek");
            Assert.AreEqual(AudioSoundId.FoleySg1Eject, WeaponFoleyV3.Mechanism("SG1", Eject, false, false), "SG1 pompa atış başına");
            Assert.AreEqual(AudioSoundId.FoleySr1BoltBack, WeaponFoleyV3.Mechanism("SR1", Eject, false), "SR1 eski: eject = sürgü geri");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("SR1", Eject, true), "SR1 v3: bolt_back taşır");
            Assert.AreEqual(AudioSoundId.FoleySr1BoltBack, WeaponFoleyV3.Mechanism("SR1", BoltBack, true));
            Assert.AreEqual(AudioSoundId.FoleyLmg1BoxOpen, WeaponFoleyV3.Mechanism("LMG1", MagOut, false), "LMG eski: mag_out = kutu açılır");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.Mechanism("LMG1", MagIn, true), "LMG v3: box_close taşır");
            Assert.AreEqual(AudioSoundId.FoleyLmg1BoxOpen, WeaponFoleyV3.Mechanism("LMG1", BoxOpen, true));
            Assert.AreEqual(AudioSoundId.FoleyAr1MagIn, WeaponFoleyV3.Mechanism("AR1", MagIn, true), "AR1 iki sette aynı");
        }

        [Category("WP021")]
        [Test]
        public void SilahAtesCuesu_TekrarBastirmasinaTakilmaz_YuksekAtesHizi()
        {
            // R1-B A2: the weapon raises one cue per shot; at the 2.2× rate tier two shots land within the 40 ms / 1.6 m window.
            int gelen = 0, bastirilan = 0;
            Action<CombatAudioCue> cue = c => { if (c.Event == CombatAudioEvent.ClassicWeaponFire) gelen++; };
            Action<CombatAudioCue> bas = c => { if (c.Event == CombatAudioEvent.ClassicWeaponFire) bastirilan++; };
            AudioEvents.Cue += cue;
            AudioEvents.CueSuppressed += bas;
            try
            {
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", Vector3.zero, 0.75f, 50);
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", Vector3.zero, 0.75f, 50);
            }
            finally
            {
                AudioEvents.Cue -= cue;
                AudioEvents.CueSuppressed -= bas;
            }

            Assert.AreEqual(2, gelen, "aynı anda / aynı yerde iki atış iki cue");
            Assert.AreEqual(0, bastirilan, "silah cue'su bastırılmaz");
        }

        [Category("WP021")]
        [Test]
        public void YagmurSonrasiKuslar_KazancFazdanBagimsiz_CatiAltindaKisik_DuckAltindaErtelenir()
        {
            // WP-072 B7 (C-00 30.09 panel): no phase factor (the old ×0.35 held the whole round), roof ×0.4, due-under-duck waits 4 s.
            Assert.AreEqual(0.6f * 0.35f, AfterRainAmbience.BirdGain(false, 0.35f), 1e-6f, "açık avlu, varsayılan 0,35");
            Assert.AreEqual(0.6f, AfterRainAmbience.BirdGain(false, 1f), 1e-6f, "açık avlu, tam seviye");
            Assert.AreEqual(0.6f * 0.4f, AfterRainAmbience.BirdGain(true, 1f), 1e-6f, "çatı altı −8 dB");
            Assert.AreEqual(0f, AfterRainAmbience.BirdGain(false, 0f), "0 = kuş yok");
            Assert.IsTrue(AfterRainAmbience.BirdDeferred(0.3f), "patlama/ateş duck'ı altında ertelenir");
            Assert.IsFalse(AfterRainAmbience.BirdDeferred(1f), "sakin avluda çalar");
            Assert.AreEqual(0.35f, AfterRainAmbience.BirdLevel, "varsayılan 0,35 kullanıcı kararı — değişmez");
            Assert.GreaterOrEqual(AfterRainAmbience.BirdRangeMin, 15f);
            Assert.LessOrEqual(AfterRainAmbience.BirdRangeMax, 25f);
        }

        [Category("WP021")]
        [Test]
        public void AtesV4_AP027Ithal_OnSilah_KatalogdaVeKatmanlarCozuluyor()
        {
            // WP-085 B5 (G4 = AP-027, C-00 05.10): every weapon's fire, mechanism, three space tails and two distance voices come
            // from the AP-027 import — in the catalog, resolved by the B3 layer path; casings per surface (none for RL1) and the
            // LMGs' belt links likewise. LMG2 Fire_01 is out of the pool (peak-limited, ~3.5 dB quiet): 5 variants.
            AudioCatalogBuilder.BuildCatalog();
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog);
            var kayit = new Dictionary<AudioSoundId, AudioCatalog.Entry>();
            foreach (AudioCatalog.Entry e in katalog.Entries) kayit[e.Sound] = e;
            foreach (string id in new[] { "AR1", "AR2", "SMG1", "SMG2", "SG1", "SG2", "LMG1", "LMG2", "SR1", "RL1" })
            {
                Assert.IsTrue(WeaponAudioIdentity.TryGet(id, out WeaponVoiceSet set));
                var sesler = new List<AudioSoundId> { set.Fire };
                foreach (WeaponLayer l in Enum.GetValues(typeof(WeaponLayer)))
                {
                    AudioSoundId k = WeaponAudioIdentity.Layer(id, l);
                    Assert.AreNotEqual(AudioSoundId.None, k, id + " " + l + " çözülmedi");
                    sesler.Add(k);
                }

                string pascal = id[0] + id.Substring(1).ToLowerInvariant();
                foreach (string yuzey in WeaponFoleyV3.V4Surfaces)
                {
                    bool kovanVar = Enum.TryParse("Casing" + pascal + yuzey, out AudioSoundId kovan);
                    Assert.AreEqual(id != "RL1", kovanVar, id + " " + yuzey + " kovanı");
                    if (kovanVar) sesler.Add(kovan);
                    if (id.StartsWith("LMG", StringComparison.Ordinal))
                    {
                        Assert.IsTrue(Enum.TryParse("Link" + pascal + yuzey, out AudioSoundId halka), id + " " + yuzey + " şerit halkası");
                        Assert.AreEqual(halka, WeaponFoleyV3.CasingV4(id, yuzey == "Puddle" ? SurfaceKind.Water : yuzey == "Wood" ? SurfaceKind.Wood : yuzey == "Dirt" ? SurfaceKind.Dirt : SurfaceKind.Stone, yuzey == "WetStone", true));
                        sesler.Add(halka);
                    }
                }

                foreach (AudioSoundId k in sesler)
                {
                    Assert.IsTrue(kayit.TryGetValue(k, out AudioCatalog.Entry e), k + " katalogda yok");
                    Assert.AreEqual(k == AudioSoundId.WeaponLmg2Fire ? 5 : 6, e.Clips.Length, k + ": varyant sayısı");
                    StringAssert.Contains("/AP027/", AssetDatabase.GetAssetPath(e.Clips[0]), k + " AP-027'den gelmeli");
                }
            }

            Assert.IsTrue(AudioCatalogBuilder.IsStereoTail("WeaponAr1TailHall") && !AudioCatalogBuilder.IsStereoTail("WeaponAr1Mechanical"));
        }

        [Category("WP021")]
        [Test]
        public void AtesV4_KatmanKazanci_LoudnessReferansli_UzakEsitEnerji_MekanikSpecOrani()
        {
            // WP-085 B5: distance voices meet the fire at equal loudness at the band edge (spec §3.2), the mechanism sits
            // −10 / −7 / −3 LU under it (spec §3.1) whatever level its stem was delivered at; ±12 dB cap; unknown → the fixed rule.
            Assert.AreEqual(4f, WeaponAudioIdentity.LoudnessCompDb(-18f, -22f, 0f, 0f), 1e-4f, "uzak ses: eş enerji");
            Assert.AreEqual(-10f + 0f, WeaponAudioIdentity.LoudnessCompDb(-18f, -18f, -10f, -10f), 1e-4f);
            Assert.AreEqual(WeaponAudioIdentity.LoudnessCompMaxDb, WeaponAudioIdentity.LoudnessCompDb(-18f, -55f, -10f, -10f), 1e-4f, "sessiz stem tavanla yükselir");
            Assert.AreEqual(-7f, WeaponAudioIdentity.LoudnessCompDb(float.NaN, -28f, -7f, -7f), 1e-4f, "LUFS yoksa sabit kural");

            AudioCatalogBuilder.BuildCatalog();
            var lib = new AudioClipLibrary();
            float fire = lib.LufsOf(AudioSoundId.WeaponSmg1Fire), mid = lib.LufsOf(AudioSoundId.WeaponSmg1DistanceMid), mech = lib.LufsOf(AudioSoundId.WeaponSmg1Mechanical);
            Assert.IsFalse(float.IsNaN(fire) || float.IsNaN(mid) || float.IsNaN(mech), "katalog LUFS");
            Assert.AreEqual(fire, mid + WeaponAudioIdentity.LoudnessCompDb(fire, mid, 0f, 0f), 0.01f, "SMG1 DistanceMid sınırda Fire ile aynı ses yüksekliği");
            Assert.AreEqual(fire - 10f, mech + WeaponAudioIdentity.LoudnessCompDb(fire, mech, -10f, -10f), 0.01f, "SMG1 mekanik üstten −10 LU");
            Debug.Log("[DD-TEST] wp085.layergain AR1 mech comp " + WeaponAudioIdentity.LoudnessCompDb(lib.LufsOf(AudioSoundId.WeaponAr1Fire), lib.LufsOf(AudioSoundId.WeaponAr1Mechanical), -10f, -10f)
                      + " dB · SMG1 mid comp " + WeaponAudioIdentity.LoudnessCompDb(fire, mid, 0f, 0f) + " dB");
            lib.Dispose();
        }

        [Category("WP021")]
        [Test]
        public void AtesV4_KatmanYolu_AdlarBantlarOfsetler_AP027GelmedenSessiz()
        {
            // WP-085 B3 (AP-027 spec §3–§5): layer ids resolve by name; none exists before AP-027 → the v016 set plays as before.
            Assert.AreEqual("WeaponAr1TailOpen", WeaponAudioIdentity.LayerName("AR1", WeaponLayer.TailOpen));
            Assert.AreEqual("WeaponSmg1Mechanical", WeaponAudioIdentity.LayerName("SMG1", WeaponLayer.Mechanical));
            Assert.AreEqual("WeaponLmg2DistanceFar", WeaponAudioIdentity.LayerName("LMG2", WeaponLayer.DistanceFar));
            foreach (WeaponVoiceSet set in WeaponAudioIdentity.All)
            {
                foreach (WeaponLayer l in Enum.GetValues(typeof(WeaponLayer)))
                {
                    bool adVar = Enum.TryParse(WeaponAudioIdentity.LayerName(set.AudioId, l), out AudioSoundId _);
                    Assert.AreEqual(adVar, WeaponAudioIdentity.Layer(set.AudioId, l) != AudioSoundId.None, set.AudioId + " " + l);
                }
            }

            Assert.AreEqual(AudioSoundId.None, WeaponAudioIdentity.Layer("YOK", WeaponLayer.Mechanical));
            Assert.AreEqual(WeaponDistanceBand.Near, WeaponAudioIdentity.Band(11.9f));
            Assert.AreEqual(WeaponDistanceBand.Mid, WeaponAudioIdentity.Band(12f));
            Assert.AreEqual(WeaponDistanceBand.Mid, WeaponAudioIdentity.Band(34.9f));
            Assert.AreEqual(WeaponDistanceBand.Far, WeaponAudioIdentity.Band(35f));
            Assert.That(WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Open), Is.InRange(0.025f, 0.035f), "açık avlu 25–35 ms");
            Assert.That(WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Corridor), Is.InRange(0.008f, 0.014f), "koridor 8–14 ms");
            Assert.That(WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Hall), Is.InRange(0.018f, 0.024f), "salon 18–24 ms");
            // R3 (kullanıcı 06.10: testler yalnız top down): the top-down row of the perspective ladder (the three-mode ladder is
            // AtesV4_KatmanYolu_PerspektifMerdiveni_TPFP). R2 S3 (C-00 06.10): top-down weapon +0.8 dB nearer, its tail lifted the same, so the space's wet/dry (S4,
            // WetDb) stays as tuned; Mechanical/Body −10 dB.
            WeaponPerspectiveMix td = WeaponPerspectiveMix.For(DD.Prototype.CameraSystem.CameraMode.TopDown);
            Assert.AreEqual(-10f, WeaponAudioIdentity.MechanicalRefDb + td.MechanicalDb, 1e-4f, "Mechanical/Body TopDown");
            Assert.AreEqual(0.8f, td.NearFieldDb, 1e-4f, "Silah yakın alan TopDown");
            Assert.AreEqual(td.NearFieldDb, td.TailDb, 1e-4f, "Kuyruk silahla aynı kalkar (ıslak/kuru korunur) TopDown");

            Assert.AreEqual(0f, AudioMixPlan.PlayerWeapon(AudioSoundId.WeaponAr1Fire).Delay, "kuru atış gecikmesiz");
        }

        /// <summary>R3 FPTP half of AtesV4_KatmanYolu (C-00 07.10): the Mechanical/Body and near-field ladder over all three modes.</summary>
        [Category("WP021")]
        [Test]
        [Category("FPTP")]
        public void AtesV4_KatmanYolu_PerspektifMerdiveni_TPFP()
        {
            float[] beklenen = { -10f, -7f, -3f };
            var modlar = new[] { DD.Prototype.CameraSystem.CameraMode.TopDown, DD.Prototype.CameraSystem.CameraMode.ThirdPerson, DD.Prototype.CameraSystem.CameraMode.FirstPerson };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(beklenen[i], WeaponAudioIdentity.MechanicalRefDb + WeaponPerspectiveMix.For(modlar[i]).MechanicalDb, 1e-4f, "Mechanical/Body " + modlar[i]);
            }

            // R2 S3 (C-00 06.10): the weapon's near-field ladder top-down +0.8 / third-person +1 / FP +2 dB, and every mode lifts its
            // tail by the same amount, so the space's wet/dry ratio (S4, WetDb) is the same in all three.
            float[] yakin = { 0.8f, 1f, 2f };
            for (int i = 0; i < 3; i++)
            {
                WeaponPerspectiveMix m = WeaponPerspectiveMix.For(modlar[i]);
                Assert.AreEqual(yakin[i], m.NearFieldDb, 1e-4f, "Silah yakın alan " + modlar[i]);
                Assert.AreEqual(m.NearFieldDb, m.TailDb, 1e-4f, "Kuyruk silahla aynı kalkar (ıslak/kuru korunur) " + modlar[i]);
            }
        }

        [Category("WP021")]
        [Test]
        public void SilahOnde_KullaniciB_FirlaticiAtesi4dBKisik_OtomatiklerRpmKirpmasi()
        {
            // WP-072 B7 (user 01.10, option B): SFX-Player +3 / SFX-World −1; the launcher's explosion keeps +2 dB over its shot.
            // WP-085 B1: every round the full shot — an automatic's held string at its own RPM comes back to the 8 shots/s kit level.
            var beklenen = new Dictionary<string, float> { { "RL1", -4f }, { "AR1", -0.9f }, { "SMG1", -2.6f }, { "SMG2", -0.4f }, { "LMG1", -1.6f } };
            foreach (WeaponVoiceSet set in WeaponAudioIdentity.All)
            {
                Assert.AreEqual(beklenen.TryGetValue(set.AudioId, out float db) ? db : 0f, set.FireTrimDb, 1e-4f, set.AudioId + " ateş kırpması");
            }
        }

        [Category("WP021")]
        [Test]
        public void AkustikAlanlar_V6Haritasi_SalonKoridor_Histerezis_SahneYoksaOpen()
        {
            Debug.Log("[DD-TEST] wp072b6 " + AudioCatalogBuilder.BuildAcousticSpaces());
            var map = AssetDatabase.LoadAssetAtPath<AcousticSpaceMap>(AudioCatalogBuilder.AcousticMapPath);
            Assert.IsNotNull(map, "DD_AcousticSpaces.asset pişirilmedi");
            Assert.GreaterOrEqual(map.Spaces.Count, 13, "S-05 v6: 6 oda + 6 koridor + R7");
            Assert.AreSame(map, AcousticSpaceMap.Load(), "Resources yükü");

            AcousticSpaceMap.Space hall = null, corridor = null;
            foreach (AcousticSpaceMap.Space sp in map.Spaces)
            {
                if (sp.id == "R1_MainHall") hall = sp;
                if (corridor == null && sp.enclosure == AcousticSpace.Corridor) corridor = sp;
            }

            Assert.IsNotNull(hall, "R1_MainHall yok");
            Assert.IsNotNull(corridor, "koridor yok");
            Vector3 Merkez(AcousticSpaceMap.Space sp)
            {
                float x = 0f, z = 0f; int n = sp.polygonXZ.Length / 2;
                for (int i = 0; i < n; i++) { x += sp.polygonXZ[i * 2]; z += sp.polygonXZ[i * 2 + 1]; }
                return new Vector3(x / n, sp.floor + 1.6f, z / n);
            }

            map.ResetHysteresis();
            Assert.AreEqual(AcousticSpace.Hall, map.ResolveInMap(Merkez(hall)), "salon içi Hall");
            // Güney kenarı poligondan (S-05 yerleşimi kayabilir: WP-074 B5b-1 z 38 → 39). 0,5 m dışarıda hâlâ Hall (1 m histerezis), 1,5 m'de Open.
            float guney = float.MaxValue;
            for (int i = 1; i < hall.polygonXZ.Length; i += 2) guney = Mathf.Min(guney, hall.polygonXZ[i]);
            float ortaX = Merkez(hall).x;
            Assert.AreEqual(AcousticSpace.Hall, map.ResolveInMap(new Vector3(ortaX, hall.floor + 1.6f, guney - 0.5f)), "eşikte 0,5 m dışarı: histerezis");
            Assert.AreEqual(AcousticSpace.Open, map.ResolveInMap(new Vector3(ortaX, hall.floor + 1.6f, guney - 1.5f)), "1,5 m dışarı: Open");
            map.ResetHysteresis();
            Assert.AreEqual(AcousticSpace.Corridor, map.ResolveInMap(Merkez(corridor)), corridor.id + " Corridor");
            Assert.AreEqual(AcousticSpace.Open, map.ResolveInMap(new Vector3(0f, 2f, -200f)), "harita dışı Open");
            // Resolve follows the world scene, whatever the suite order left open (CI c99971fb: a V6 EditMode test ran first):
            // without World_V6_Greybox everything is Open (AP005), with it the map answers.
            UnityEngine.SceneManagement.Scene v6 = UnityEngine.SceneManagement.SceneManager.GetSceneByName("World_V6_Greybox");
            bool opened = false;
            try
            {
                if (!v6.isLoaded)
                {
                    map.ResetHysteresis();
                    Assert.AreEqual(AcousticSpace.Open, map.Resolve(Merkez(hall)), "V6 sahnesi yüklü değilken (AP005) her yer Open");
                    string[] found = AssetDatabase.FindAssets("World_V6_Greybox t:Scene");
                    Assert.IsNotEmpty(found, "World_V6_Greybox sahnesi yok");
                    v6 = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(found[0]), UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    opened = true;
                }

                map.ResetHysteresis();
                Assert.AreEqual(AcousticSpace.Hall, map.Resolve(Merkez(hall)), "V6 sahnesi yüklüyken harita cevap verir");
            }
            finally
            {
                map.ResetHysteresis();
                if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(v6, true);
            }
        }
    }
}
