using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Characters;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-052 · Tier experience v1, the pure parts: (1) the shipped AP-009D v004 manifest carries every
    /// reaction clip the WP-032C contract names (directional × tier hits, armour break by region, the
    /// Runner stumble, the eight deaths) on the contract layers, (2) telegraph parity on the SHIPPED
    /// manifest: each tier's attack clip is present (the Ultra overhead is real, no placeholder), its rate
    /// lands hit_window_start on the profile telegraph, and the active / recovery deviation is within the
    /// S-06 note threshold (logged otherwise), (3) the beat payload carries the attack style.
    /// </summary>
    [Category("WP052")]
    public sealed class TierExperienceTests
    {
        private static string ManifestPath => Path.Combine(Application.dataPath, "_Art", "Characters", "Animations", "AP009-clips-manifest.json");

        private sealed class ManifestClip { public string clip; public string layer; public string tier; public int frames; public int fps = 30; public int start = -1, end = -1; }

        private static List<ManifestClip> ReadManifest()
        {
            var list = new List<ManifestClip>();
            if (!File.Exists(ManifestPath)) return list;
            string json = File.ReadAllText(ManifestPath);
            // one region per clip: from its "clip" key to the next clip's (the manifest lists clips in order; nested validation objects are harmless here)
            var heads = System.Text.RegularExpressions.Regex.Matches(json, "\"clip\"\\s*:\\s*\"([^\"]+)\"");
            for (int i = 0; i < heads.Count; i++)
            {
                int from = heads[i].Index, to = i + 1 < heads.Count ? heads[i + 1].Index : json.Length;
                string region = json.Substring(from, to - from);
                var c = new ManifestClip { clip = heads[i].Groups[1].Value };
                System.Text.RegularExpressions.Match m;
                m = System.Text.RegularExpressions.Regex.Match(region, "\"layer\"\\s*:\\s*\"([^\"]+)\""); c.layer = m.Success ? m.Groups[1].Value : "";
                m = System.Text.RegularExpressions.Regex.Match(region, "\"tier\"\\s*:\\s*\"([^\"]+)\""); c.tier = m.Success ? m.Groups[1].Value : null;
                m = System.Text.RegularExpressions.Regex.Match(region, "\"frames\"\\s*:\\s*(\\d+)"); c.frames = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                m = System.Text.RegularExpressions.Regex.Match(region, "\"fps\"\\s*:\\s*(\\d+)"); c.fps = m.Success ? int.Parse(m.Groups[1].Value) : 30;
                m = System.Text.RegularExpressions.Regex.Match(region, "\"hit_window_start\"\\s*:\\s*(\\d+)"); c.start = m.Success ? int.Parse(m.Groups[1].Value) : -1;
                m = System.Text.RegularExpressions.Regex.Match(region, "\"hit_window_end\"\\s*:\\s*(\\d+)"); c.end = m.Success ? int.Parse(m.Groups[1].Value) : -1;
                list.Add(c);
            }
            return list;
        }

        [Test]
        public void SevkManifesti_v004TepkiKlipleri_SozlesmeVeKatmanlar()
        {
            List<ManifestClip> clips = ReadManifest();
            Assert.Greater(clips.Count, 0, "sevk edilen AP-009 manifesti okunmadı: " + ManifestPath);
            var expected = new Dictionary<string, string>();
            for (int dir = 0; dir < 4; dir++) for (int tier = 0; tier < 3; tier++) expected[CharacterAnimatorContract.ZombieHitClip(dir, tier)] = "override_upper";
            for (int region = 0; region < 3; region++) expected[CharacterAnimatorContract.ZombieArmorBreakClip(region)] = "base";
            expected[CharacterAnimatorContract.ZombieHitSpecialStumble] = "base";
            foreach (string d in CharacterAnimatorContract.ZombieDeathClips) expected[d] = "base";
            Assert.AreEqual(8, CharacterAnimatorContract.ZombieDeathClips.Length, "WP-032C: 4 M1 + side_R + special_A/B + ultra");

            var missing = new List<string>();
            foreach (KeyValuePair<string, string> kv in expected)
            {
                ManifestClip c = clips.Find(x => x.clip == kv.Key);
                if (c == null) { missing.Add(kv.Key); continue; }
                Assert.AreEqual(kv.Value, c.layer, kv.Key + " katmanı sözleşmeyle uyumsuz (AP-009C/D manifesti)");
                Assert.Greater(c.frames, 0, kv.Key + " kare sayısı");
            }
            Debug.Log("[DD-OBS] wp052.clips contract=" + expected.Count + " missing=" + missing.Count + (missing.Count > 0 ? ": " + string.Join(", ", missing) : " (v004 tam)"));
            Assert.IsEmpty(missing, "WP-032C v004 seti bu klipleri taşımalı");
            // the tier-behaviour profile's own clip name resolves against the shipped set (WP-040 data switch-over is complete)
            TierBehaviourProfileSet profiles = TierBehaviourProfileSet.CreateDefault();
            try { Assert.AreEqual("AP009_zombie_attack_overhead", profiles.For(EnemyTier.Ultra).ResolvedAttackClip(n => clips.Exists(x => x.clip == n))); }
            finally { Object.DestroyImmediate(profiles); }
        }

        [Test]
        public void TelegraphParitesi_SevkManifesti_KlipOlayi_ProfilZamanlayicisi()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            TierBehaviourProfileSet profiles = TierBehaviourProfileSet.CreateDefault();
            try
            {
                List<ManifestClip> clips = ReadManifest().FindAll(x => x.start >= 0 && x.end >= 0);
                Assert.GreaterOrEqual(clips.Count, 4, "dört saldırı klibi (swipe / lunge / sweep / overhead)");
                var byTier = new Dictionary<EnemyTier, string>
                {
                    { EnemyTier.Standard, "AP009_zombie_attack_swipe" }, { EnemyTier.Special, "AP009_zombie_attack_lunge" },
                    { EnemyTier.Elite, "AP009_zombie_attack_sweep" }, { EnemyTier.Ultra, "AP009_zombie_attack_overhead" },
                };
                var table = new System.Text.StringBuilder("[DD-TEST] wp052.parity tier | clip | rate | telegraph(timer=clip) | active dev | recovery dev\n");
                foreach (KeyValuePair<EnemyTier, string> kv in byTier)
                {
                    ManifestClip c = clips.Find(x => x.clip == kv.Value);
                    Assert.IsNotNull(c, kv.Value + " sevk manifestinde yok (yer tutucu kalmamalı)");
                    EnemyTierSettings s = config.Enemies.Find(kv.Key);
                    TierBehaviourProfile p = profiles.For(kv.Key);
                    float timerT = s.TelegraphTime * p.TelegraphScale, timerA = s.AttackActiveTime, timerR = s.AttackRecoveryTime * p.RecoveryScale;
                    float fps = c.fps > 0 ? c.fps : 30f;
                    float nativeT = c.start / fps, nativeA = (c.end - c.start) / fps, nativeR = (c.frames - c.end) / fps;
                    float rate = Mathf.Clamp(nativeT / timerT, 0.25f, 4f);   // ZombieSkinnedVisual.PlayAttack: hit_window_start lands on the telegraph
                    float clipT = nativeT / rate, clipA = nativeA / rate, clipR = nativeR / rate;
                    float devA = Mathf.Abs(clipA - timerA) / Mathf.Max(0.01f, timerA), devR = Mathf.Abs(clipR - timerR) / Mathf.Max(0.01f, timerR);
                    table.Append(kv.Key).Append(" | ").Append(c.clip).Append(" | ").Append(rate.ToString("0.00")).Append(" | ").Append(timerT.ToString("0.000")).Append('=').Append(clipT.ToString("0.000"))
                         .Append(" | ").Append(devA.ToString("0.00")).Append(" | ").Append(devR.ToString("0.00")).Append('\n');
                    Assert.AreEqual(timerT, clipT, 1e-3f, kv.Key + " telegraph paritesi (klip hit_window_start = profil telegraph)");
                    Assert.Less(rate, 4f, kv.Key + " oran klamp tavanına dayanmamalı"); Assert.Greater(rate, 0.25f);
                    // the DEC-029 window is the clip's (hit_window_end closes it): a gross mismatch (> 50 %) would read as a wrong clip, not a tuning note
                    Assert.LessOrEqual(devA, 0.5f, kv.Key + " aktif pencere sapması brüt (" + devA.ToString("0.00") + ")");
                    if (devA > AttackWindowParityCheck.DeviationThreshold || devR > AttackWindowParityCheck.DeviationThreshold)
                        Debug.Log("[DD-OBS] wp052.parity S-06 notu: " + kv.Key + " " + c.clip + " aktif/toparlanma sapması " + devA.ToString("0.00") + "/" + devR.ToString("0.00") + " > " + AttackWindowParityCheck.DeviationThreshold);
                }
                Debug.Log(table.ToString());
                Assert.IsTrue(profiles.For(EnemyTier.Ultra).HasOwnAttackClip, "Ultra profil kendi klibini (overhead) adlandırır");
                Assert.AreEqual("AP009_zombie_attack_overhead", profiles.For(EnemyTier.Ultra).ResolvedAttackClip(n => clips.Exists(x => x.clip == n)), "v002 sette overhead çözülür, yer tutucu emekli");
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(profiles);
            }
        }

        [Test]
        public void VurusYuku_SaldiriStiliTasir()
        {
            var a = new EnemyCombatEventInfo(EnemyCombatEventKind.AttackCommit, null, EnemyTier.Ultra, 1, Vector3.zero, Vector3.forward, ArmorSlot.None, true, 0.25f, ZombieAttackStyle.Overhead);
            Assert.AreEqual(ZombieAttackStyle.Overhead, a.Style);
            var b = new EnemyCombatEventInfo(EnemyCombatEventKind.Death, null, EnemyTier.Standard, 1, Vector3.zero, Vector3.forward, ArmorSlot.None, false, 0f);
            Assert.AreEqual(ZombieAttackStyle.Swipe, b.Style, "varsayılan Swipe (WP-040 çağrıları bozulmaz)");
        }
    }
}
