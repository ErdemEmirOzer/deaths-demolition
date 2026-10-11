using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DD.Prototype.Audio;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-104 (R4) · ağaç sesleri haritada ve karışım planında. Sınananlar:
    /// - B0'ın her TreeProcKind üyesi bir "TreeEvents.Proc · …" satırında adlanır (sözleşme yalnız eklenir: yeni tür satırsız kalamaz);
    ///   satırın sesi TreeAudio.SoundFor ile, bus / konumluluğu yerleşimle aynı.
    /// - Kimlikler IDS.md değerlerinde.
    /// - Bus grupları S-08 kararında: ekran = UI, oyuncu merkezli = SFX-Player (Weapon), öbürü = SFX-World (Impact / Target).
    /// - LOD: sık seslerde sert tavan; Mini Nuke ≤ 1 / 3 s; Doomsday 1 canlı; MaxInstances = canlı tavan.
    /// - Duck: yalnız Mini Nuke ve Doomsday, kısa.
    /// - R4 döngüleri IsLoopBed'de.
    /// Genel kapsam (tekillik, rezerve olmayanın sesi, recipe / katalog) AudioSliceEditModeTests'te otomatik koşar.
    /// </summary>
    [TestFixture]
    public sealed class Wp104TreeAudioMapTests
    {
        private const string ProcPrefix = "TreeEvents.Proc · ";

        [Category("WP021")]
        [Test]
        public void Agac_HerTreeProcKindSatirda_SesBusKonumYerlesimleAyni()
        {
            var eksik = new List<string>();
            foreach (TreeProcKind kind in Enum.GetValues(typeof(TreeProcKind)))
            {
                string ad = kind.ToString();
                bool bulundu = false;
                foreach (AudioEventBinding b in AudioEventMap.Entries)
                {
                    if (b.Source != AudioEventSource.Combat || !b.Event.StartsWith(ProcPrefix, StringComparison.Ordinal)) continue;
                    if (!Regex.IsMatch(b.Event.Substring(ProcPrefix.Length), @"(^|[ /])" + Regex.Escape(ad) + @"($|[ /(])")) continue;
                    bulundu = true;
                    AudioSoundId ses = TreeAudio.SoundFor(kind);
                    if (b.Reserved)
                    {
                        Assert.AreEqual(AudioSoundId.None, ses, ad + ": rezerve satır ama TreeAudio çalıyor → çift ses");
                        Assert.IsFalse(string.IsNullOrEmpty(b.Note), ad + " rezerve satırın gerekçesi yok");
                        continue;
                    }

                    Assert.AreEqual(b.Sound, ses, ad + ": harita sesi TreeAudio.SoundFor ile aynı olmalı");
                    AudioPlacement p = AudioMixPlan.Tree(ses);
                    Assert.AreEqual(ses, p.Sound, ad);
                    Assert.AreEqual(b.Bus, p.Bus, ad + ": harita ile karışım planı aynı bus'ta");
                    Assert.AreEqual(b.Spatial, p.Spatial > 0f, ad + ": konumluluk haritayla aynı");
                }

                if (!bulundu) eksik.Add(ad);
            }

            Assert.IsEmpty(eksik, "Haritada satırı olmayan TreeProcKind: " + string.Join(", ", eksik));

            foreach (string olay in new[] { "TreeEvents.NodePurchased", "TreeEvents.SynergyUnlocked", "TreeEvents.AuraChanged (level up)" })
            {
                Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Run, olay, out AudioEventBinding b), "Matris satırı yok: " + olay);
                Assert.IsFalse(b.Reserved, olay);
                Assert.AreEqual(b.Bus, AudioMixPlan.Tree(b.Sound).Bus, olay);
            }
        }

        [Category("WP021")]
        [Test]
        public void Agac_KimliklerIdsDegerinde_BusGruplariS08Karari()
        {
            var ids = new (AudioSoundId ses, int deger)[]
            {
                (AudioSoundId.TreeOpen, 1020), (AudioSoundId.TreeStamp, 1021), (AudioSoundId.TreeSynergy, 1022), (AudioSoundId.TreeReroll, 1023),
                (AudioSoundId.TreeFireIgnite, 1030), (AudioSoundId.TreeFireLoop, 1031), (AudioSoundId.TreeIceFreeze, 1032),
                (AudioSoundId.TreeIceShatter, 1033), (AudioSoundId.TreePoisonLoop, 1034), (AudioSoundId.TreeThunder, 1035),
                (AudioSoundId.TreeElementNova, 1036), (AudioSoundId.TreeCrit, 1040), (AudioSoundId.TreeMiniNuke, 1041),
                (AudioSoundId.TreeBlackHoleLoop, 1042), (AudioSoundId.TreeBlackHoleCollapse, 1043), (AudioSoundId.TreeDoomsdayBell, 1044),
                (AudioSoundId.TreeShieldUp, 1050), (AudioSoundId.TreeShieldHit, 1051), (AudioSoundId.TreeShieldBreak, 1052),
                (AudioSoundId.TreeLifesteal, 1053), (AudioSoundId.TreeDodge, 1054), (AudioSoundId.TreeUndying, 1055),
                (AudioSoundId.TreeRicochet, 1060), (AudioSoundId.TreeEndlessOn, 1061), (AudioSoundId.TreeMiniRocket, 1062),
                (AudioSoundId.TreeAuraUp, 1063)
            };
            var ekran = new HashSet<AudioSoundId> { AudioSoundId.TreeOpen, AudioSoundId.TreeStamp, AudioSoundId.TreeSynergy, AudioSoundId.TreeReroll };
            var oyuncu = new HashSet<AudioSoundId>
            {
                AudioSoundId.TreeShieldUp, AudioSoundId.TreeShieldHit, AudioSoundId.TreeShieldBreak, AudioSoundId.TreeLifesteal,
                AudioSoundId.TreeDodge, AudioSoundId.TreeUndying, AudioSoundId.TreeAuraUp, AudioSoundId.TreeEndlessOn
            };

            foreach ((AudioSoundId ses, int deger) in ids)
            {
                Assert.AreEqual(deger, (int)ses, ses + ": IDS.md");
                AudioPlacement p = AudioMixPlan.Tree(ses);
                AudioVoiceLod lod = AudioMixPlan.TreeLod(ses);
                if (ekran.Contains(ses))
                {
                    Assert.AreEqual(AudioBus.Interface, p.Bus, ses + ": ağaç ekranı UI");
                    Assert.AreEqual(0f, p.Spatial, ses + ": 2D");
                }
                else if (oyuncu.Contains(ses))
                {
                    Assert.AreEqual(AudioBus.Weapon, p.Bus, ses + ": oyuncu merkezli = SFX-Player (Weapon)");
                }
                else
                {
                    Assert.IsTrue(p.Bus == AudioBus.Impact || p.Bus == AudioBus.Target, ses + ": SFX-World (Impact / Target)");
                    Assert.GreaterOrEqual(p.Spatial, 0.5f, ses + ": dünya sesi konumlu");
                }

                Assert.AreEqual(Math.Max(1, lod.MaxLive), p.MaxInstances, ses + ": havuz kopya sayısı LOD canlı tavanıyla aynı");
                bool duck = ses == AudioSoundId.TreeMiniNuke || ses == AudioSoundId.TreeDoomsdayBell;
                if (duck)
                {
                    Assert.Greater(p.DuckSeconds, 0f, ses + ": kısa duck");
                    Assert.LessOrEqual(p.DuckSeconds, 0.75f, ses + ": duck kısa olmalı");
                    Assert.GreaterOrEqual(p.DuckCombat, 0.6f, ses + ": savaş en çok −4,4 dB kısılır");
                }
                else
                {
                    Assert.AreEqual(0f, p.DuckSeconds, ses + ": yalnız Mini Nuke ve Doomsday duck yapar");
                }
            }

            Assert.AreEqual(TreeAudio.IdCount, 1063 - 1020 + 1, "TreeAudio kapı bloğu 1020–1063");
        }

        [Category("WP021")]
        [Test]
        public void Agac_SesLod_SikSeslerTavanli_NukeUcSaniye_DoomsdayTek_DongulerLoopBed()
        {
            foreach (AudioSoundId sik in new[] { AudioSoundId.TreeCrit, AudioSoundId.TreeLifesteal, AudioSoundId.TreeDodge, AudioSoundId.TreeShieldHit, AudioSoundId.TreeRicochet })
            {
                AudioVoiceLod lod = AudioMixPlan.TreeLod(sik);
                Assert.LessOrEqual(lod.MaxLive, 2, sik + ": eşzamanlı ≤ 2");
                Assert.Greater(lod.PerSecond, 0, sik + ": saniye tavanı var");
                Assert.LessOrEqual(lod.PerSecond, 6, sik + ": saniyede ≤ 6");
                Assert.Greater(lod.MinInterval, 0f, sik + ": yeniden tetikleme kapısı var");
                Assert.AreEqual(0f, lod.HoldSeconds, sik + ": sık ses bekletilmez, düşer");
            }

            AudioVoiceLod nuke = AudioMixPlan.TreeLod(AudioSoundId.TreeMiniNuke);
            Assert.GreaterOrEqual(nuke.MinInterval, TreeGuardrails.NukeCooldownSeconds, "Mini Nuke ≤ 1 / 3 s");
            Assert.LessOrEqual(nuke.MaxLive, 2, "~6 s Nuke: 3 s aralıkla en çok 2 canlı (canlı tavanı 3 s'yi uzatmamalı)");
            Assert.Greater(nuke.MaxLive, 1, "1 canlı tavanı ~6 s'lik klipte aralığı 6 s'ye uzatırdı");
            Assert.Greater(nuke.HoldSeconds, 0f, "erken gelen Nuke kaybolmaz, kapıya kadar bekler");
            Assert.LessOrEqual(nuke.HoldSeconds, 1f);
            Assert.AreEqual(1, AudioMixPlan.TreeLod(AudioSoundId.TreeDoomsdayBell).MaxLive, "Doomsday ≤ 1");
            Assert.LessOrEqual(AudioMixPlan.TreeFieldVoices, 3, "sesli döngü ≤ 3");
            Assert.LessOrEqual(TreeGuardrails.AreaZones, 16, "B0 alan zonları ≤ 16");

            foreach (AudioSoundId loop in new[] { AudioSoundId.TreeFireLoop, AudioSoundId.TreePoisonLoop, AudioSoundId.TreeBlackHoleLoop })
            {
                Assert.IsTrue(AudioRecipes.IsLoopBed(loop), loop + ": döngü yatağı listesinde");
            }

            Assert.AreEqual(AudioSoundId.TreeCrit, TreeAudio.SoundFor(TreeProcKind.CritRing), "Crit ve CritRing aynı sesi paylaşır (aynı kare tek ses)");
            Assert.AreEqual(AudioSoundId.None, TreeAudio.SoundFor(TreeProcKind.ExplosiveRound), "patlama kendi Explosion cue'sunu çalar");
        }
    }
}
