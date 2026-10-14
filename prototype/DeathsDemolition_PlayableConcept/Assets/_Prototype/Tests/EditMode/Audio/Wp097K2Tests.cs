using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Audio;
using DD.Prototype.Audio.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = UnityEngine.TestTools.Constraints.Is;   // derives from NUnit's Is (Is.InRange resolves); no CS0104

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-097 K2 (S-08 07.10) · the AP-029 v001 integration: the imported manifest's quality (ceiling, level band per part, length,
    /// end silence, channels from the WAV header), the catalog priority AP-029 &gt; AP-027 &gt; AP-011 and the AP-029 rows' source,
    /// the new identity tables (Metal / Mat / Grass casings and links on the 700 / 800 arithmetic, MagDrop 970–984) and their
    /// fallbacks, and MagazineDropAudio on a fake play delegate (surface, fall time, level, pitch scatter, GC 0).
    /// </summary>
    [TestFixture]
    [Category("WP097")]
    public sealed class Wp097K2Tests
    {
        /// <summary>Far from anything the editor scene may hold (K1 uses 5000; this fixture 6000).</summary>
        private static readonly Vector3 O = new Vector3(6000f, 0f, 6000f);

        /// <summary>The fields ap029_import.py writes beyond the AP-011 schema (JsonUtility ignores the rest).</summary>
        [Serializable]
        public sealed class Ap029Entry
        {
            public string sound;
            public string part;
            public string[] files;
            public bool loop;
            public float lufs;
            public float stereoLufs;   // WP-113: the manifest's channel-sum stamp
            public float truePeakDb;
            public float targetLufs;
            public float ceilingDbtp;
            public float endSilenceDb;
            public int bitDepth;
            public int channels;
            public float[] durationsS;
        }

        [Serializable]
        public sealed class Ap029Manifest
        {
            public string version;
            public Ap029Entry[] entries = Array.Empty<Ap029Entry>();
        }

        private static Ap029Manifest LoadAp029()
        {
            if (!File.Exists(AudioCatalogBuilder.Ap029ManifestPath))
            {
                Assert.Inconclusive("AP-029 manifest not imported yet (tools/audio/ap029_import.py): " + AudioCatalogBuilder.Ap029ManifestPath);
            }

            return JsonUtility.FromJson<Ap029Manifest>(File.ReadAllText(AudioCatalogBuilder.Ap029ManifestPath));
        }

        // ------------------------------------------------------------------ AP-029 manifest quality

        /// <summary>
        /// The AP-027 gate pattern on AP-029 (per-part standard, 01-design/*-v5.md): ceiling ≤ −1 dBTP and the row's true peak under it
        /// (tails −3, foley −1 since S-08's re-level 07.10); level band — Fire at its AP-027 stamp ±1 LU, class tails −20 ±1 LUFS stereo
        /// sum, specials −26…−16, casings −24 / links −30 / magazines −22 (target +0.5, or up to 7 LU under it when the ceiling holds it
        /// down); length band per kind; end silence ≤ −70 dBFS (loops exempt); WAV header 24-bit 48 kHz, stereo exactly for the tails.
        /// </summary>
        [Test]
        public void AP029_Manifest_TavanHedefSureSessizlik_Kanal()
        {
            Ap029Manifest man = LoadAp029();
            var hatalar = new List<string>();
            int dosya = 0;
            // A special weapon's row replaces an AP-011 row at its level (AP-029 special-v5.md gate: ± 0.5 LU of the row it
            // supersedes): the loops sit well under −26 by design (SwGelFlightLoop −28, SwGelPuddleLoop −28.2 in AP-011).
            var ap011 = new Dictionary<string, float>();
            if (File.Exists(AudioCatalogBuilder.ManifestPath))
            {
                foreach (Ap029Entry o in JsonUtility.FromJson<Ap029Manifest>(File.ReadAllText(AudioCatalogBuilder.ManifestPath)).entries) ap011[o.sound] = o.lufs;
            }

            foreach (Ap029Entry e in man.entries)
            {
                string s = e.sound;
                if (e.ceilingDbtp > -1f + 0.001f) hatalar.Add($"{s}: tavan {e.ceilingDbtp} > −1 dBTP");
                if (e.truePeakDb > e.ceilingDbtp + 0.01f) hatalar.Add($"{s}: tepe {e.truePeakDb} > tavan {e.ceilingDbtp}");
                if (e.bitDepth != 24) hatalar.Add($"{s}: {e.bitDepth}-bit");
                if (!e.loop && e.endSilenceDb > -70f) hatalar.Add($"{s}: son {e.endSilenceDb} dBFS > −70");

                bool tail = s.StartsWith("WeaponTail", StringComparison.Ordinal);
                float minS, maxS;
                if (tail)
                {
                    if (Mathf.Abs(e.stereoLufs + 20f) > 1f) hatalar.Add($"{s}: stereoLufs {e.stereoLufs} ≠ −20 ± 1");
                    if (e.ceilingDbtp > -3f + 0.001f) hatalar.Add($"{s}: kuyruk tavanı {e.ceilingDbtp} > −3");
                    if (s.EndsWith("Room", StringComparison.Ordinal)) { minS = 0.5f; maxS = 1.5f; }
                    else { minS = 1.4f; maxS = 3.5f; }   // Echo 1.5–3 s / Release 2–3 s (+ silence pad)
                }
                else if (s.StartsWith("Weapon", StringComparison.Ordinal) && s.EndsWith("Fire", StringComparison.Ordinal))
                {
                    if (e.targetLufs < -20.5f || e.targetLufs > -17.5f) hatalar.Add($"{s}: hedef {e.targetLufs} ∉ [−20,5; −17,5]");
                    if (Mathf.Abs(e.lufs - e.targetLufs) > 1f) hatalar.Add($"{s}: {e.lufs} LUFS ≠ hedef {e.targetLufs} ± 1");
                    minS = 0.2f; maxS = 1.5f;
                }
                else if (s.StartsWith("Sw", StringComparison.Ordinal))
                {
                    if (ap011.TryGetValue(s, out float eski))
                    {
                        if (Mathf.Abs(e.lufs - eski) > 1f) hatalar.Add($"{s}: {e.lufs} LUFS ≠ AP-011 {eski} ± 1");
                    }
                    else if (e.lufs < -26f || e.lufs > -16f) hatalar.Add($"{s}: {e.lufs} LUFS ∉ [−26; −16]");
                    minS = 0.1f; maxS = 3f;
                }
                else
                {
                    float hedef = s.StartsWith("MagDrop", StringComparison.Ordinal) ? -22f : s.StartsWith("Link", StringComparison.Ordinal) ? -30f : -24f;
                    if (!s.StartsWith("Casing", StringComparison.Ordinal) && !s.StartsWith("Link", StringComparison.Ordinal) && !s.StartsWith("MagDrop", StringComparison.Ordinal))
                        hatalar.Add($"{s}: tanınmayan AP-029 satırı");
                    if (Mathf.Abs(e.targetLufs - hedef) > 0.01f) hatalar.Add($"{s}: hedef {e.targetLufs} ≠ {hedef}");
                    if (e.lufs > hedef + 0.5f || e.lufs < hedef - 7f) hatalar.Add($"{s}: {e.lufs} LUFS ∉ [{hedef - 7f}; {hedef + 0.5f}]");
                    bool mag = s.StartsWith("MagDrop", StringComparison.Ordinal);
                    minS = mag ? 0.3f : 0.2f; maxS = mag ? 0.8f : 0.6f;
                }

                if (e.files == null || e.files.Length == 0) { hatalar.Add($"{s}: dosya yok"); continue; }
                for (int f = 0; f < e.files.Length; f++)
                {
                    string path = Path.GetFullPath(AudioCatalogBuilder.Ap029AudioRoot + e.files[f]);
                    if (!File.Exists(path)) { hatalar.Add($"{e.files[f]}: yok"); continue; }
                    WavInfo(path, out int ch, out int rate, out int bits, out float sure);
                    dosya++;
                    if (bits != 24 || rate != 48000) hatalar.Add($"{e.files[f]}: {bits}-bit {rate} Hz");
                    if (ch != (tail ? 2 : 1)) hatalar.Add($"{e.files[f]}: {ch} kanal ({(tail ? "stereo kuyruk" : "mono")} beklenir)");
                    if (sure < minS - 0.005f || sure > maxS + 0.005f) hatalar.Add($"{e.files[f]}: {sure:0.000} s ∉ [{minS}; {maxS}]");
                }
            }

            Assert.Greater(man.entries.Length, 0, "AP-029 manifesti boş");
            Assert.IsEmpty(hatalar, "AP-029 kalite kapısı (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp097.k2.ap029 entries=" + man.entries.Length + " files=" + dosya + " version=" + man.version);
        }

        private static void WavInfo(string path, out int channels, out int rate, out int bits, out float seconds)
        {
            channels = rate = bits = 0;
            seconds = 0f;
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                fs.Seek(12, SeekOrigin.Begin);
                int blockAlign = 0;
                while (fs.Position + 8 <= fs.Length)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    long next = fs.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        br.ReadInt16();   // format
                        channels = br.ReadInt16();
                        rate = br.ReadInt32();
                        br.ReadInt32();   // byte rate
                        blockAlign = br.ReadInt16();
                        bits = br.ReadInt16();
                    }
                    else if (id == "data" && blockAlign > 0 && rate > 0)
                    {
                        seconds = size / (float)blockAlign / rate;
                    }

                    fs.Seek(next, SeekOrigin.Begin);
                }
            }
        }

        // ------------------------------------------------------------------ catalog priority

        [Test]
        public void KatalogDonusturucu_AP029_AP027_AP011_Onceligi_YinelenenHata()
        {
            AudioCatalogBuilder.ManifestEntry Satir(string ses) => new AudioCatalogBuilder.ManifestEntry { sound = ses, files = new[] { ses + "_01.wav" } };
            string ates = nameof(AudioSoundId.WeaponAr1Fire), mek = nameof(AudioSoundId.WeaponAr1Mechanical), ozel = nameof(AudioSoundId.SwArcFire);
            string ui = nameof(AudioSoundId.UiConfirm), metal = nameof(AudioSoundId.CasingAr1Metal);
            var ap011 = new AudioCatalogBuilder.Manifest { version = "t011", entries = new[] { Satir(ates), Satir(ozel), Satir(ui) } };
            var ap027 = new AudioCatalogBuilder.Manifest { version = "t027", entries = new[] { Satir(ates), Satir(mek) } };
            var ap029 = new AudioCatalogBuilder.Manifest { version = "t029", entries = new[] { Satir(ates), Satir(ozel), Satir(metal) } };

            var satirlar = AudioCatalogBuilder.MergeRows(ap011, ap027, ap029);
            var kok = new Dictionary<string, string>();
            foreach (var s in satirlar)
            {
                Assert.IsFalse(kok.ContainsKey(s.entry.sound), s.entry.sound + " iki kez");
                kok[s.entry.sound] = s.root;
            }

            Assert.AreEqual(5, satirlar.Count);
            Assert.AreEqual(AudioCatalogBuilder.Ap029AudioRoot, kok[ates], "Fire: AP-029 > AP-027 > AP-011");
            Assert.AreEqual(AudioCatalogBuilder.Ap029AudioRoot, kok[ozel], "özel silah: AP-029 > AP-011");
            Assert.AreEqual(AudioCatalogBuilder.Ap029AudioRoot, kok[metal]);
            Assert.AreEqual(AudioCatalogBuilder.Ap027AudioRoot, kok[mek], "AP-029'un vermediği AP-027 satırı kalır");
            Assert.AreEqual(AudioCatalogBuilder.AudioRoot, kok[ui], "geri kalan AP-011");

            Assert.AreEqual(4, AudioCatalogBuilder.MergeRows(ap011, null, ap029).Count, "AP-027 yokken");
            Assert.AreEqual(AudioCatalogBuilder.MergeRows(ap011, ap027).Count, AudioCatalogBuilder.MergeRows(ap011, ap027, null).Count, "iki argüman = AP-029 yok");

            var ap029Cift = new AudioCatalogBuilder.Manifest { version = "t029b", entries = new[] { Satir(metal), Satir(metal) } };
            var hata = Assert.Throws<InvalidOperationException>(() => AudioCatalogBuilder.MergeRows(ap011, ap027, ap029Cift));
            StringAssert.Contains("AP-029 " + metal, hata.Message);

            // Room / Echo / Release class tails: stereo q9; Echo / Release compressed in memory, Room decompressed (short).
            Assert.IsTrue(AudioCatalogBuilder.IsStereoTail(nameof(AudioSoundId.WeaponTailArRoom)) && AudioCatalogBuilder.IsStereoTail(nameof(AudioSoundId.WeaponTailSgEcho)));
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, AudioCatalogBuilder.WantedLoadType(nameof(AudioSoundId.WeaponTailArRoom), false));
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory, AudioCatalogBuilder.WantedLoadType(nameof(AudioSoundId.WeaponTailLmgRelease), false));
            Assert.IsFalse(AudioCatalogBuilder.IsStereoTail(metal) || AudioCatalogBuilder.IsStereoTail(nameof(AudioSoundId.MagDropRifleWood)), "foley mono");
        }

        /// <summary>The imported AP-029 rows are in the catalog from /AP029/ with every file (Fire v5 replacing AP-027's Fire under its name).</summary>
        [Test]
        public void Katalog_AP029Satirlari_AP029Kokunden_TumDosyalar()
        {
            Ap029Manifest man = LoadAp029();
            AudioCatalogBuilder.BuildCatalog();
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog);
            var kayit = new Dictionary<AudioSoundId, AudioCatalog.Entry>();
            foreach (AudioCatalog.Entry e in katalog.Entries) kayit[e.Sound] = e;
            var hatalar = new List<string>();
            foreach (Ap029Entry m in man.entries)
            {
                if (!Enum.TryParse(m.sound, false, out AudioSoundId id)) { hatalar.Add(m.sound + ": AudioSoundId değil"); continue; }
                if (!kayit.TryGetValue(id, out AudioCatalog.Entry e)) { hatalar.Add(m.sound + ": katalogda yok"); continue; }
                if (e.Clips.Length != m.files.Length) hatalar.Add($"{m.sound}: {e.Clips.Length} klip ≠ {m.files.Length}");
                for (int i = 0; i < e.Clips.Length; i++)
                {
                    string yol = AssetDatabase.GetAssetPath(e.Clips[i]);
                    if (yol.IndexOf("/AP029/", StringComparison.Ordinal) < 0) hatalar.Add($"{m.sound}: {yol} AP-029'dan değil");
                }

                if (m.sound.StartsWith("WeaponTail", StringComparison.Ordinal))
                {
                    var imp = AssetImporter.GetAtPath(AudioCatalogBuilder.Ap029AudioRoot + m.files[0]) as AudioImporter;
                    if (imp == null) { hatalar.Add(m.sound + ": importer yok"); continue; }
                    if (imp.forceToMono) hatalar.Add(m.sound + ": mono'ya zorlanmış (stereo kuyruk)");
                    AudioClipLoadType lt = imp.defaultSampleSettings.loadType;
                    if (lt != AudioCatalogBuilder.WantedLoadType(m.sound, false)) hatalar.Add($"{m.sound}: loadType {lt}");
                }
            }

            Assert.IsEmpty(hatalar, "AP-029 katalog (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        // ------------------------------------------------------------------ identities

        [Test]
        public void Kimlik_KovanMetalMatGrass_SeritHalkasi_MagDrop_Tablolari()
        {
            // 700 / 800 + weapon·10 + surface: Metal 5, Mat 6, Grass 7 (V4Surfaces order), the name rule Casing<W><Surface>.
            CollectionAssert.AreEqual(new[] { "StoneSlate", "WetStone", "Wood", "Dirt", "Puddle", "Metal", "Mat", "Grass" }, WeaponFoleyV3.V4Surfaces);
            for (int w = 0; w < WeaponAudioIdentity.All.Length; w++)
            {
                string a = WeaponAudioIdentity.All[w].AudioId;
                string pascal = a[0] + a.Substring(1).ToLowerInvariant();
                for (int s = WeaponFoleyV3.FirstV5Surface; s < WeaponFoleyV3.V4Surfaces.Length; s++)
                {
                    bool kovan = Enum.TryParse("Casing" + pascal + WeaponFoleyV3.V4Surfaces[s], false, out AudioSoundId k);
                    Assert.AreEqual(a != "RL1", kovan, a + " " + WeaponFoleyV3.V4Surfaces[s]);
                    if (kovan) Assert.AreEqual(700 + w * 10 + s, (int)k, k.ToString());
                    bool halka = Enum.TryParse("Link" + pascal + WeaponFoleyV3.V4Surfaces[s], false, out AudioSoundId l);
                    Assert.AreEqual(a.StartsWith("LMG", StringComparison.Ordinal), halka, a + " link");
                    if (halka) Assert.AreEqual(800 + w * 10 + s, (int)l, l.ToString());
                }
            }

            // AP-029 surface map; the AP-027 one (CasingV4) is unchanged — it is the fallback when no AP-029 file is produced.
            Assert.AreEqual(AudioSoundId.CasingAr1Metal, WeaponFoleyV3.CasingV5("AR1", SurfaceKind.Metal));
            Assert.AreEqual(AudioSoundId.CasingSg2Mat, WeaponFoleyV3.CasingV5("SG2", SurfaceKind.Paper));
            Assert.AreEqual(AudioSoundId.CasingSmg1Grass, WeaponFoleyV3.CasingV5("SMG1", SurfaceKind.Foliage));
            Assert.AreEqual(AudioSoundId.LinkLmg1Grass, WeaponFoleyV3.CasingV5("LMG1", SurfaceKind.Foliage, true));
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV5("AR1", SurfaceKind.Metal, true), "tüfeğin şerit halkası yok");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV5("RL1", SurfaceKind.Metal));
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV5("AR1", SurfaceKind.Stone), "taş AP-027'de kalır");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV5("AR1", SurfaceKind.Water));
            Assert.AreEqual(AudioSoundId.CasingAr1StoneSlate, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Metal, false), "yedek: metal → StoneSlate");
            Assert.AreEqual(AudioSoundId.CasingAr1WetStone, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Metal, true));
            Assert.AreEqual(AudioSoundId.CasingSg1Wood, WeaponFoleyV3.CasingV4("SG1", SurfaceKind.Paper, false), "yedek: tatami → Wood");
            Assert.AreEqual(AudioSoundId.None, WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Foliage, false), "yedek: çim sessiz");

            // S-08 07.10: the AP-029 files carry their own skips — no game bounce on them; the AP-027 / v017 rule unchanged otherwise.
            Assert.IsFalse(WeaponFoleyV3.CasingBounces(SurfaceKind.Metal, true), "Metal dosyası 1–2 sekme taşır: üçüncü darbe yok");
            Assert.IsFalse(WeaponFoleyV3.CasingBounces(SurfaceKind.Paper, true) || WeaponFoleyV3.CasingBounces(SurfaceKind.Foliage, true));
            Assert.IsTrue(WeaponFoleyV3.CasingBounces(SurfaceKind.Metal, false), "AP-029 dosyası yokken metal eskisi gibi sekiyor");
            Assert.AreEqual(WeaponFoleyV3.CasingBounces(SurfaceKind.Stone), WeaponFoleyV3.CasingBounces(SurfaceKind.Stone, false));
            Assert.AreEqual(0.4f, WeaponFoleyV3.V5JitterSemitones, 1e-6f);

            // MagDrop 970 + surface (Rifle) / 980 + surface (Pistol), surfaces StoneSlate, Wood, Dirt, Metal, WetStone.
            CollectionAssert.AreEqual(new[] { "StoneSlate", "Wood", "Dirt", "Metal", "WetStone" }, MagazineDropAudio.Surfaces);
            for (int s = 0; s < MagazineDropAudio.Surfaces.Length; s++)
            {
                Assert.AreEqual("MagDropRifle" + MagazineDropAudio.Surfaces[s], MagazineDropAudio.Id(MagazineDropFamily.Rifle, s).ToString());
                Assert.AreEqual(970 + s, (int)MagazineDropAudio.Id(MagazineDropFamily.Rifle, s));
                Assert.AreEqual("MagDropPistol" + MagazineDropAudio.Surfaces[s], MagazineDropAudio.Id(MagazineDropFamily.Pistol, s).ToString());
                Assert.AreEqual(980 + s, (int)MagazineDropAudio.Id(MagazineDropFamily.Pistol, s));
            }

            Assert.AreEqual(AudioSoundId.None, MagazineDropAudio.Id(MagazineDropFamily.None, 0));
            Assert.AreEqual(AudioSoundId.None, MagazineDropAudio.Id(MagazineDropFamily.Rifle, 5));

            // Which weapon drops which magazine: the magazine-timeline weapons (rl_1 excluded by WeaponController; shotguns raise none).
            var aile = new Dictionary<ClassicWeaponId, MagazineDropFamily>
            {
                { ClassicWeaponId.AssaultRifle, MagazineDropFamily.Rifle }, { ClassicWeaponId.Ar2, MagazineDropFamily.Rifle },
                { ClassicWeaponId.Lmg1, MagazineDropFamily.Rifle }, { ClassicWeaponId.Lmg2, MagazineDropFamily.Rifle }, { ClassicWeaponId.Sr1, MagazineDropFamily.Rifle },
                { ClassicWeaponId.AutoSmg, MagazineDropFamily.Pistol }, { ClassicWeaponId.Smg2, MagazineDropFamily.Pistol },
                { ClassicWeaponId.CombatShotgun, MagazineDropFamily.None }, { ClassicWeaponId.Sg2, MagazineDropFamily.None }, { ClassicWeaponId.Rl1, MagazineDropFamily.None },
            };
            Assert.AreEqual(Enum.GetValues(typeof(ClassicWeaponId)).Length, aile.Count, "her silah eşlemede");
            foreach (var kv in aile) Assert.AreEqual(kv.Value, MagazineDropAudio.Family(kv.Key), kv.Key.ToString());

            Assert.AreEqual(MagazineDropAudio.SurfaceStoneSlate, MagazineDropAudio.Surface(SurfaceKind.Stone, false));
            Assert.AreEqual(MagazineDropAudio.SurfaceWetStone, MagazineDropAudio.Surface(SurfaceKind.Default, true), "ıslak görünüm → WetStone");
            Assert.AreEqual(MagazineDropAudio.SurfaceWetStone, MagazineDropAudio.Surface(SurfaceKind.Water, false));
            Assert.AreEqual(MagazineDropAudio.SurfaceWood, MagazineDropAudio.Surface(SurfaceKind.Paper, true));
            Assert.AreEqual(MagazineDropAudio.SurfaceDirt, MagazineDropAudio.Surface(SurfaceKind.Foliage, true));
            Assert.AreEqual(MagazineDropAudio.SurfaceMetal, MagazineDropAudio.Surface(SurfaceKind.Metal, true), "metal ıslakta da metal");

            // Fall √(2h/g) in 0.35–0.5 s; level +4 dB (−22 vs −18) + the row's deficit under −22, ≤ 6 dB; no stamp → +4.
            Assert.AreEqual(Mathf.Sqrt(2f / 9.81f), MagazineDropAudio.FallSeconds(1f), 1e-4f);
            Assert.AreEqual(MagazineDropAudio.MinFall, MagazineDropAudio.FallSeconds(0.1f), 1e-6f);
            Assert.AreEqual(MagazineDropAudio.MaxFall, MagazineDropAudio.FallSeconds(3f), 1e-6f);
            Assert.AreEqual(4f, MagazineDropAudio.FileCompensationDb(float.NaN), 1e-6f);
            Assert.AreEqual(4f, MagazineDropAudio.FileCompensationDb(0f), 1e-6f, "damga 0 = yok");
            Assert.AreEqual(4f, MagazineDropAudio.FileCompensationDb(-22f), 1e-6f);
            Assert.AreEqual(4f, MagazineDropAudio.FileCompensationDb(-20f), 1e-6f, "hedefin üstü kısılmaz");
            Assert.AreEqual(7.5f, MagazineDropAudio.FileCompensationDb(-25.5f), 1e-5f);
            Assert.AreEqual(10f, MagazineDropAudio.FileCompensationDb(-40f), 1e-6f, "en çok +6");
        }

        [Test]
        public void MagDrop_Yedek_AyniYuzeyDigerAile_SonraStoneSlate()
        {
            var lib = new AudioClipLibrary(null);
            AudioClip k = AudioClip.Create("wp097k2_mag", 480, 1, 48000, false);
            try
            {
                Assert.AreEqual(AudioSoundId.None, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Rifle, MagazineDropAudio.SurfaceDirt), "hiçbiri yokken sessiz (eski davranış)");
                lib.OverrideForKit(AudioSoundId.MagDropPistolDirt, new[] { k });
                lib.OverrideForKit(AudioSoundId.MagDropRifleStoneSlate, new[] { k });
                lib.OverrideForKit(AudioSoundId.MagDropRifleMetal, new[] { k });
                Assert.AreEqual(AudioSoundId.MagDropPistolDirt, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Rifle, MagazineDropAudio.SurfaceDirt), "Rifle_Dirt yok → Pistol_Dirt");
                Assert.AreEqual(AudioSoundId.MagDropRifleMetal, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Pistol, MagazineDropAudio.SurfaceMetal), "Pistol_Metal yok → Rifle_Metal");
                Assert.AreEqual(AudioSoundId.MagDropRifleStoneSlate, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Rifle, MagazineDropAudio.SurfaceWetStone), "WetStone yok → StoneSlate");
                Assert.AreEqual(AudioSoundId.MagDropRifleStoneSlate, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Pistol, MagazineDropAudio.SurfaceWood), "Wood yok → diğer ailenin StoneSlate'i");
                Assert.AreEqual(AudioSoundId.MagDropRifleStoneSlate, MagazineDropAudio.Resolve(lib, MagazineDropFamily.Rifle, MagazineDropAudio.SurfaceStoneSlate));
                Assert.AreEqual(AudioSoundId.None, MagazineDropAudio.Resolve(lib, MagazineDropFamily.None, MagazineDropAudio.SurfaceStoneSlate));
            }
            finally
            {
                lib.ClearKitOverrides();
                UnityEngine.Object.DestroyImmediate(k);
            }
        }

        // ------------------------------------------------------------------ MagazineDropAudio on a fake play delegate

        [Test]
        public void MagazineDropAudio_SahteOynatma_YuzeyDusmeSeviyePerde_SifirAyirma()
        {
            CameraMode oncekiMod = CameraModeService.Current;
            if (oncekiMod != CameraMode.TopDown) CameraModeService.Request(CameraMode.TopDown, true);
            bool oncekiIslak = AmbienceDirector.WetLook;
            AmbienceDirector.WetLook = false;
            GameObject zemin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zemin.name = "wp097k2_floor";
            zemin.layer = 0;   // Default: in CasingEffect.FloorMask
            zemin.transform.position = O + new Vector3(0f, -0.5f, 0f);   // top face at O.y
            zemin.transform.localScale = new Vector3(20f, 1f, 20f);
            zemin.AddComponent<SurfaceTag>().Surface = SurfaceKind.Metal;
            AudioClip k = AudioClip.Create("wp097k2_drop", 480, 1, 48000, false);
            var lib = new AudioClipLibrary(null);
            try
            {
                Physics.SyncTransforms();
                lib.OverrideForKit(AudioSoundId.MagDropRifleMetal, new[] { k });
                int calan = 0;
                AudioSoundId sonSes = AudioSoundId.None;
                float sonKazanc = 0f, sonPerde = 0f;
                WeaponFireAudio.PlayFunc play = (in AudioPlacement p, Vector3 pos, float g, int pr) =>
                {
                    calan++;
                    sonSes = p.Sound;
                    sonKazanc = g;
                    sonPerde = p.Pitch;
                    return true;
                };
                var m = new MagazineDropAudio(lib, play);
                Vector3 el = O + Vector3.up * 1f;

                // sg_1 / sg_2 / rl_1: no magazine — nothing scheduled.
                Assert.IsFalse(m.Drop(ClassicWeaponId.CombatShotgun, el, 0f));
                Assert.IsFalse(m.Drop(ClassicWeaponId.Sg2, el, 0f));
                Assert.IsFalse(m.Drop(ClassicWeaponId.Rl1, el, 0f));
                Assert.AreEqual(0, m.InAir);

                // AR1 on metal: Rifle_Metal, after √(2·1/g) ≈ 0.45 s, at the casing's top-down level + 4 dB (no stamp: kit override).
                Assert.IsTrue(m.Drop(ClassicWeaponId.AssaultRifle, el, 10f));
                Assert.AreEqual(SurfaceKind.Metal, m.LastSurface);
                m.Tick(10.2f);
                Assert.AreEqual(0, calan, "düşüş süresi dolmadan çalmaz");
                m.Tick(10.46f);
                Assert.AreEqual(1, calan);
                Assert.AreEqual(AudioSoundId.MagDropRifleMetal, sonSes);
                Assert.AreEqual(Mathf.Sqrt(2f / 9.81f), m.LastFallSeconds, 0.01f);
                float beklenenDb = CasingAudio.PerspectiveDb(CameraMode.TopDown) + MagazineDropAudio.FileCompDb;
                Assert.AreEqual(beklenenDb, m.LastGainDb, 1e-4f);
                Assert.AreEqual(Mathf.Pow(10f, beklenenDb / 20f) * (0.85f / 0.5f), sonKazanc, 1e-4f);
                Assert.That(sonPerde, Is.InRange(WeaponFoleyV3.Pitch(-0.4f) - 1e-4f, WeaponFoleyV3.Pitch(0.4f) + 1e-4f), "perde ±0,4 yarım ton");

                // SMG1 on metal: no Pistol_Metal → the Rifle_Metal fallback.
                Assert.IsTrue(m.Drop(ClassicWeaponId.AutoSmg, el, 20f));
                m.Tick(21f);
                Assert.AreEqual(AudioSoundId.MagDropRifleMetal, sonSes);

                // No floor under it: not heard.
                Assert.IsFalse(m.Drop(ClassicWeaponId.AssaultRifle, O + new Vector3(500f, 1f, 0f), 30f));
                Assert.AreEqual(1, m.NoFloor);

                // GC 0 after warm-up: 200 drops and their landings (raycast, surface cache, resolve, play).
                float t = 100f;
                float minPerde = float.MaxValue, maxPerde = 0f;
                Assert.That(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        m.Drop((i & 1) == 0 ? ClassicWeaponId.AssaultRifle : ClassicWeaponId.Smg2, el, t);
                        t += 1f;
                        m.Tick(t);
                        if (sonPerde < minPerde) minPerde = sonPerde;
                        if (sonPerde > maxPerde) maxPerde = sonPerde;
                    }
                }, Is.Not.AllocatingGCMemory(), "MagazineDropAudio Drop + Tick ayırdı (DEC-088 GC 0)");
                Assert.AreEqual(202, calan, "200 düşüşün hepsi duyuldu");
                Assert.Greater(maxPerde - minPerde, 0.01f, "perde serpintisi var");
                Debug.Log("[DD-TEST] wp097.k2.magdrop played=" + calan + " fall=" + m.LastFallSeconds.ToString("0.000") + " gainDb=" + m.LastGainDb.ToString("0.0")
                          + " pitch=" + minPerde.ToString("0.000") + "…" + maxPerde.ToString("0.000") + " threadAlloc=none");
            }
            finally
            {
                lib.ClearKitOverrides();
                UnityEngine.Object.DestroyImmediate(k);
                UnityEngine.Object.DestroyImmediate(zemin);
                AmbienceDirector.WetLook = oncekiIslak;
                if (CameraModeService.Current != oncekiMod) CameraModeService.Request(oncekiMod, true);
            }
        }
    }
}
