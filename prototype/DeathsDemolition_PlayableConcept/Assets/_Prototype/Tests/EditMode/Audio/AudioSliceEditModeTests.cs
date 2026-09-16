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
            Assert.AreEqual(4, AudioEventMap.For(AudioEventSource.Ui).Count, "UI (navigate/confirm/back/denied)");
            Assert.AreEqual(6, AudioEventMap.For(AudioEventSource.Ambience).Count, "5 bölge + ortak katman (DEC-066)");
            Assert.AreEqual(4, AudioEventMap.For(AudioEventSource.Music).Count, "4 müzik katmanı (DEC-065)");
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
                if (ad.StartsWith("Amb")) amb++;
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
            foreach (AudioCatalog.Entry e in katalog.Entries)
            {
                float hedef = Hedef(e.Sound, manifest.standard);
                // Standart: hedef ± tolerans; ya da tepe sınırında (-1 dBTP) ve hedefin ALTINDA.
                // Kısa transientler (darbe, tık, damla) -1 dBTP tavanında -18 LUFS'a
                // ulaşamaz - tepe kısıtı seviyeyi belirler, bu doğru davranıştır.
                bool tepeSinirinda = e.TruePeakDb >= manifest.standard.truePeakMax - 0.15f && e.Lufs <= hedef + 0.1f;
                if (Mathf.Abs(e.Lufs - hedef) > tol && !tepeSinirinda)
                {
                    hatalar.Add($"{e.Sound}: manifest {e.Lufs:0.0} LUFS, hedef {hedef:0.0} ± {tol} (tepe {e.TruePeakDb:0.0})");
                }

                if (e.TruePeakDb > manifest.standard.truePeakMax + 0.1f)
                {
                    hatalar.Add($"{e.Sound}: true peak {e.TruePeakDb:0.0} dBTP > {manifest.standard.truePeakMax}");
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
    }
}
