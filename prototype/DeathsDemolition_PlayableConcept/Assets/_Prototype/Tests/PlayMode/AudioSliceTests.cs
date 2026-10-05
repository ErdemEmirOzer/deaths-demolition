using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-021 · Ses omurgası PlayMode dilimi (Run sahnesi, gerçek director):
    /// olay → ses tetikleme (10 olay), bölge geçişi crossfade (≥ 2 s, DEC-066), müzik
    /// yoğunluk tepkisi + round arası geri çekilme (DEC-065), havuz tavanı, mixer
    /// snapshot'ları (pause dâhil), zombi hazırlık sesi (DEC-067) ve GC 0.
    /// Sahne kurulumu WP-007 kitini (SpecialWeaponSahne) yeniden kullanır.
    /// </summary>
    [TestFixture]
    [Category("WP021")]
    public sealed class AudioSliceTests
    {
        private SpecialWeaponSahne _s;
        private PrototypeAudioDirector _d;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _d = PrototypeAudioDirector.EnsureExists();
            yield return SpecialWeaponSahne.Bekle(() => _d.Library.PrewarmComplete, 30f, "Ses kütüphanesi prewarm bitmedi.");
            yield return SpecialWeaponSahne.Bekle(() => _d.Ambience.Started && _d.Music.Started, 10f, "Ambiyans/müzik başlamadı.");
            Time.captureDeltaTime = 0f;
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            if (_d != null)
            {
                _d.Ambience.RegionOverride = null;
                _d.Music.IntensityOverride = null;
                _d.Music.AccentOverride = null;
            }

            yield return _s.Bitir();
        }

        // ================================================================== 10 olay

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator OlayTetikleme_OnOlay_HerBiriSesUretir()
        {
            var olaylar = new (string ad, Action tetik, AudioSoundId beklenen)[]
            {
                ("ClassicWeaponFire", () => AudioEvents.Raise(CombatAudioEvent.ClassicWeaponFire, Vector3.zero, 1f, 50), AudioSoundId.RifleFire),
                ("HeavyWeaponFire", () => AudioEvents.Raise(CombatAudioEvent.HeavyWeaponFire, Konum(1f), 1f, 60), AudioSoundId.HeavyFire),
                ("SurfaceImpactStone", () => AudioEvents.Raise(CombatAudioEvent.SurfaceImpactStone, Konum(2f), 1f, 20), AudioSoundId.ImpactStone),
                ("FleshHit", () => AudioEvents.Raise(CombatAudioEvent.FleshHit, Konum(3f), 1f, 30), AudioSoundId.FleshHit),
                ("ArmorBreak", () => AudioEvents.Raise(CombatAudioEvent.ArmorBreak, Konum(4f), 1f, 80), AudioSoundId.ArmorBreak),
                ("EnemyDeath", () => AudioEvents.Raise(CombatAudioEvent.EnemyDeath, Konum(5f), 1f, 70), AudioSoundId.EnemyDeath),
                ("SpecialWeaponSpawn", () => AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponSpawn, Konum(6f), 1f, 100), AudioSoundId.SwSpawnGong),
                ("GoldCollected", () => RunAudioEvents.Raise(RunAudioEvent.GoldCollected, Konum(7f), 1f, 0), AudioSoundId.GoldPickup),
                ("CountdownTick", () => RunAudioEvents.Raise(RunAudioEvent.CountdownTick, default, 1f, 2), AudioSoundId.RoundCountdownTick),   // RoundStart has a 3 s retrigger window and the run just started one
                ("UI Navigate", PrototypeAudio.Navigate, AudioSoundId.UiNavigate)
            };

            int once = _d.PlayedCount;
            for (int i = 0; i < olaylar.Length; i++)
            {
                int oncesi = _d.PlayedCount;
                olaylar[i].tetik();
                Assert.AreEqual(oncesi + 1, _d.PlayedCount, "[" + olaylar[i].ad + "] ses üretmedi (dropped=" + _d.DroppedCues + ").");
                Assert.AreEqual(olaylar[i].beklenen, _d.LastPlayed, "[" + olaylar[i].ad + "] yanlış ses.");
                yield return null;
                yield return null;
            }

            Assert.AreEqual(once + olaylar.Length, _d.PlayedCount);
            Debug.Log("[DD-TEST] wp021.events played=" + (_d.PlayedCount - once) + " world=" + _d.WorldPool.Describe() + " signal=" + _d.SignalPool.Describe());
        }

        private Vector3 Konum(float dx)
        {
            return _s.Rig.transform.position + new Vector3(dx, 1f, 2f);
        }

        // ================================================================== bölge crossfade

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator BolgeGecisi_Crossfade_EnAzIkiSaniye_TekBolgeAktif()
        {
            AmbienceDirector amb = _d.Ambience;
            amb.RegionOverride = AudioRegion.Center;
            int merkez = AmbienceDirector.BedIndex(AudioRegion.Center);
            int kuzey = AmbienceDirector.BedIndex(AudioRegion.North);

            yield return SpecialWeaponSahne.Bekle(() => amb.CurrentRegion == AudioRegion.Center && amb.Level(merkez) > 0.3f, 15f, "Merkez bed'i yükselmedi.");

            float t0 = Time.realtimeSinceStartup;
            amb.RegionOverride = AudioRegion.North;
            yield return SpecialWeaponSahne.Bekle(() => amb.CurrentRegion == AudioRegion.North, 2f, "Bölge kuzeye çözülmedi.");

            float merkezBaslangic = amb.Level(merkez);
            Assert.Greater(merkezBaslangic, 0.3f, "Öncül: merkez bed'i aktif değildi.");

            yield return SpecialWeaponSahne.Bekle(() => amb.Level(merkez) <= 0.02f && amb.Level(kuzey) >= 0.3f, 15f, "Crossfade tamamlanmadı.");
            float sure = Time.realtimeSinceStartup - t0;

            Assert.GreaterOrEqual(sure, 2.0f, "DEC-066: bölge geçişi ≥ 2 s olmalı; ölçülen " + sure.ToString("0.00") + " s");
            Assert.LessOrEqual(sure, AmbienceDirector.CrossfadeSeconds + 1.5f, "Crossfade beklenenden çok uzun.");

            for (int i = 1; i < AmbienceDirector.BedCount; i++)
            {
                if (i != kuzey) Assert.LessOrEqual(amb.Level(i), 0.02f, "Aktif olmayan bölge bed'i çalıyor: " + i);
            }

            Assert.Greater(amb.Level(AmbienceDirector.Common), 0.2f, "Ortak katman her zaman çalar.");
            Debug.Log("[DD-TEST] wp021.region crossfade=" + sure.ToString("0.00") + "s changes=" + amb.RegionChanges + " " + amb.Describe());
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator BolgeHaritasi_OyuncuKonumundan_Cozulur()
        {
            AudioRegionMap map = AudioRegionMap.Load();
            if (map == null || map.Polygons.Count == 0)
            {
                Assert.Ignore("Bölge haritası yok (AP005 placement dışı dünya).");
            }

            // Haritanın kendisi: her merkez kendi bölgesine; oyuncunun gerçek konumu ya bir
            // bölgeye ya da None'a çözülür ve director aynı cevabı verir.
            foreach (AudioRegionMap.Polygon p in map.Polygons)
            {
                Assert.AreEqual(p.Region, map.Resolve(new Vector3(p.Centroid.x, 0f, p.Centroid.y), 0f));
            }

            _d.Ambience.RegionOverride = null;
            AudioRegion beklenen = map.Resolve(_s.Rig.transform.position);
            yield return SpecialWeaponSahne.Bekle(() => _d.Ambience.CurrentRegion == beklenen, 3f, "Director oyuncu bölgesini çözmedi: " + beklenen);
            Debug.Log("[DD-TEST] wp021.region player=" + beklenen + " at " + _s.Rig.transform.position);
        }

        // ================================================================== müzik

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator MuzikYogunluk_KatmanlarTepkiVerir_RoundArasiGeriCekilir()
        {
            MusicDirector m = _d.Music;
            m.SetPhase(RunPhase.Fighting);
            m.IntensityOverride = 0f;
            m.AccentOverride = false;
            yield return SpecialWeaponSahne.Bekle(() => m.Intensity <= 0.01f && m.Level(MusicDirector.Percussion) <= 0.02f && m.Level(MusicDirector.Tension) <= 0.02f, 12f, "Sıfır yoğunlukta katmanlar susmadı.");
            Assert.Greater(m.Level(MusicDirector.Base), 0.3f, "Temel doku her zaman çalar.");

            m.IntensityOverride = 0.95f;
            yield return SpecialWeaponSahne.Bekle(() => m.Level(MusicDirector.Percussion) > 0.5f && m.Level(MusicDirector.Tension) > 0.4f, 12f, "Yüksek yoğunlukta vurmalı/gerilim yükselmedi.");
            Assert.LessOrEqual(m.Level(MusicDirector.Accent), 0.02f, "Tier yokken vurgu katmanı çalmamalı.");

            m.AccentOverride = true;
            yield return SpecialWeaponSahne.Bekle(() => m.Level(MusicDirector.Accent) > 0.4f, 8f, "Elite/Ultra/Special vurgusu yükselmedi.");

            float perk = m.Level(MusicDirector.Percussion);
            m.SetPhase(RunPhase.Upgrading);
            float t0 = Time.realtimeSinceStartup;
            yield return SpecialWeaponSahne.Bekle(() => m.Level(MusicDirector.Percussion) <= 0.02f && m.Level(MusicDirector.Tension) <= 0.02f && m.Level(MusicDirector.Accent) <= 0.02f, 12f, "Round arası geri çekilme olmadı.");
            float sure = Time.realtimeSinceStartup - t0;
            Assert.GreaterOrEqual(sure, 1.5f, "Geri çekilme bir rampa olmalı, kesme değil (" + sure.ToString("0.00") + " s, başlangıç " + perk.ToString("0.00") + ").");
            Assert.Greater(m.Level(MusicDirector.Base), 0.1f, "Temel doku round arasında da çalar (geri çekilir, susmaz).");
            Assert.Less(m.Level(MusicDirector.Base), 0.5f, "Temel doku round arasında geri çekilmeli.");
            Debug.Log("[DD-TEST] wp021.music pullback=" + sure.ToString("0.00") + "s " + m.Describe());
        }

        // ================================================================== havuz tavanı

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HavuzTavani_AsimdaDusurur_AktifSesKapasiteyiAsmaz()
        {
            AudioVoicePool dunya = _d.WorldPool;
            dunya.ResetCounters();
            int kapasite = dunya.Capacity;
            Assert.LessOrEqual(kapasite, 32, "≤ 32 eşzamanlı 3D kaynak (WP-021 §2)");

            CombatAudioEvent[] tur = { CombatAudioEvent.FleshHit, CombatAudioEvent.SurfaceImpactStone, CombatAudioEvent.SurfaceImpactWood, CombatAudioEvent.ArmorHit, CombatAudioEvent.EnemyDeath, CombatAudioEvent.SurfaceImpactMetal, CombatAudioEvent.Ricochet, CombatAudioEvent.SurfaceImpactDirt };
            int istek = kapasite * 3;
            for (int i = 0; i < istek; i++)
            {
                Vector3 p = _s.Rig.transform.position + new Vector3((i % 9) * 3.5f - 14f, 1f, (i / 9) * 3.5f + 3f);
                AudioEvents.RaiseCritical(tur[i % tur.Length], p, 1f, 50);
            }

            Assert.LessOrEqual(dunya.ActiveCount, kapasite, "Aktif ses tavanı aşıldı.");
            Assert.Greater(dunya.DroppedCount + dunya.RetriggerBlockedCount + dunya.StolenCount, 0, "Aşımda hiçbir istek reddedilmedi/çalınmadı.");
            yield return null;
            Assert.LessOrEqual(dunya.ActiveCount, kapasite);
            Debug.Log("[DD-TEST] wp021.pool requested=" + istek + " capacity=" + kapasite + " " + dunya.Describe() + " dropped=" + dunya.DroppedCount + " blocked=" + dunya.RetriggerBlockedCount + " stolen=" + dunya.StolenCount);
        }

        [Test]
        public void HavuzKarari_Telemetri_SebepleriAyriKaydeder()
        {
            var root = new GameObject("wp065-voice-telemetry-test");
            AudioClip clip = AudioClip.Create("wp065-test", 4800, 1, 48000, false);
            try
            {
                AudioPlaybackTelemetry.Reset();
                var pool = new AudioVoicePool("World", root.transform, 1, new AudioBusMixer());
                var placement = new AudioPlacement
                {
                    Sound = AudioSoundId.FleshHit,
                    Bus = AudioBus.Target,
                    Gain = 1f,
                    Priority = 10f,
                    Spatial = 0f,
                    MaxInstances = 1,
                    MinRetrigger = 0f,
                    Pitch = 1f,
                };

                Assert.IsTrue(pool.Play(placement, clip, Vector3.zero, 1f, 10f));
                Assert.IsTrue(pool.Play(placement, clip, Vector3.zero, 1f, 10f));
                Assert.AreEqual(1, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.SameSoundRecycled));

                placement.Sound = AudioSoundId.CriticalHit;
                Assert.IsTrue(pool.Play(placement, clip, Vector3.zero, 1f, 20f));
                Assert.AreEqual(1, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.LowerPriorityStolen));

                placement.Sound = AudioSoundId.ImpactStone;
                Assert.IsFalse(pool.Play(placement, clip, Vector3.zero, 1f, 5f));
                Assert.AreEqual(1, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.BudgetDropped));

                placement.Sound = AudioSoundId.ArmorBreak;
                placement.MinRetrigger = 1f;
                Assert.IsTrue(pool.Play(placement, clip, Vector3.zero, 1f, 30f));
                Assert.IsFalse(pool.Play(placement, clip, Vector3.zero, 1f, 30f));
                Assert.AreEqual(1, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.RetriggerBlocked));
                Assert.AreEqual(4, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.Started));
                pool.StopAll();
                Assert.AreEqual(1, AudioPlaybackTelemetry.Total(AudioPlaybackOutcome.RequestedStop));
                Assert.AreEqual(0, AudioPlaybackTelemetry.OverflowCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ================================================================== mixer snapshot

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator MixerSnapshot_FightingRun_PauseHold_Pause_VeGeri()
        {
            AudioMixerBridge k = _d.Bridge;
            Assert.IsTrue(k.Available, "Mixer varlığı yüklenmedi (Resources/DD_AudioMixer).");
            Assert.AreEqual(RunPhase.Fighting, RunDirector.Instance.Phase, "Öncül: Fighting");
            Assert.AreEqual(AudioSnapshot.Run, k.Current, "Fighting → Run snapshot");

            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            yield return null;
            Assert.IsTrue(_d.IsPaused);
            Assert.AreEqual(AudioSnapshot.Pause, k.Current, "Pause hold → Pause snapshot");

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            yield return null;
            Assert.IsFalse(_d.IsPaused);
            Assert.AreEqual(AudioSnapshot.Run, k.Current, "Pause kalkınca önceki snapshot geri gelir");

            // ADR-021-2: Run'da tek etkin dinleyici var ve oyuncunun başında (kamerada değil).
            AudioListener[] tum = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
            int etkin = 0; AudioListener aktif = null;
            foreach (AudioListener l in tum) { if (l.enabled && l.gameObject.activeInHierarchy) { etkin++; aktif = l; } }
            Assert.AreEqual(1, etkin, "Tam bir etkin AudioListener olmalı (" + tum.Length + " toplam).");
            Assert.IsTrue(_d.Listener.Anchored, "Oyuncu varken dinleyici oyuncuya bağlı olmalı.");
            float uzaklik = Vector3.Distance(aktif.transform.position, _s.Rig.transform.position);
            Assert.Less(uzaklik, 2.5f, "Dinleyici oyuncunun başında değil: " + uzaklik.ToString("0.0") + " m");
            // v003 (ADR-021-2 mekânsal derinlik): bölge reverb zone'ları ve dünya seslerinde mesafe low-pass.
            if (AudioRegionMap.Load() != null)
            {
                Assert.GreaterOrEqual(_d.Ambience.ReverbZoneCount, 3, "Bölge reverb zone'ları kurulmadı.");
                Assert.IsNotNull(_d.Ambience.ReverbZone(AudioRegion.Center));
            }

            AudioEvents.RaiseCritical(CombatAudioEvent.SurfaceImpactStone, _s.Rig.transform.position + new Vector3(45f, 1f, 0f), 1f, 20);
            yield return null;
            AudioLowPassFilter[] filtreler = _d.WorldPool != null ? UnityEngine.Object.FindObjectsOfType<AudioLowPassFilter>() : null;
            int kapali = 0;
            foreach (AudioLowPassFilter f in filtreler) { if (f.enabled && f.cutoffFrequency < 15000f) kapali++; }
            Assert.GreaterOrEqual(kapali, 1, "45 m'deki dünya sesi için mesafe low-pass'ı kapanmadı (beklenen ≈ 12 kHz).");
            Assert.Less(AudioVoicePool.CutoffFor(30f), AudioVoicePool.CutoffFor(10f), "Uzak ses daha karanlık olmalı.");
            Assert.AreEqual(AudioVoicePool.LowPassOpen, AudioVoicePool.CutoffFor(4f), "Yakın ses tam bant.");

            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            yield return null;
            Assert.IsTrue(AudioListener.pause, "Öncül: pause listener'ı dondurmalı.");
            _d.enabled = false;
            Assert.IsFalse(AudioListener.pause, "Director devre dışı kalınca global listener pause serbest bırakılmalı.");
            Assert.IsFalse(_d.Mixer.Muted, "Director devre dışı kalınca software mixer mute serbest bırakılmalı.");
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            _d.enabled = true;
            yield return null;
            Assert.AreEqual(AudioSnapshot.Run, k.Current, "Director yeniden etkinleşince güncel Fighting snapshot'ını benimsemeli.");

            Debug.Log("[DD-TEST] wp021.mixer " + k.Describe() + " · " + _d.Listener.Describe() + " · listener-player " + uzaklik.ToString("0.00") + " m · reverb zones " + _d.Ambience.ReverbZoneCount + " · lpf closed " + kapali);
        }

        // ================================================================== zombi hazırlık sesi

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ZombiHazirlikSesi_SaldiridanOnce_Yonlu_VeVurusSesi()
        {
            AudioEventRouter r = _d.Router;
            int windupOnce = r.WindupCount, attackOnce = r.AttackCount;

            // Oyuncunun hemen önünde bir Standard: menzile girer, telegraph'a girer, vurur.
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 1.6f));
            _s.Rig.Health.DiagnosisInvulnerable = true;

            float windupAt = -1f;
            yield return SpecialWeaponSahne.Bekle(() =>
            {
                if (r.WindupCount > windupOnce && windupAt < 0f) windupAt = Time.realtimeSinceStartup;
                return r.WindupCount > windupOnce;
            }, 20f, "Hazırlık (windup) sesi hiç üretilmedi.");

            Assert.AreEqual(ZombieAttackPhase.Windup, z.AttackPhase, "Ses windup fazının kendisinde tetiklenmeli.");
            Assert.AreEqual(AudioSoundId.ZombieWindupStandard, _d.LastPlayed, "Standard tier hazırlık sesi.");

            yield return SpecialWeaponSahne.Bekle(() => r.AttackCount > attackOnce, 10f, "Saldırı (active) sesi üretilmedi.");
            float aralik = Time.realtimeSinceStartup - windupAt;
            Assert.GreaterOrEqual(aralik, 0.3f, "DEC-067: hazırlık sesi saldırıdan ≥ 0,3 s önce olmalı (" + aralik.ToString("0.00") + " s).");
            Assert.GreaterOrEqual(r.RiseCount + r.AggroCount, 1, "Spawn sesi üretilmedi (WP-048: spawn_rise toprak ya da spawn_land aggro).");
            Debug.Log("[DD-TEST] wp021.zombie windup→attack=" + aralik.ToString("0.00") + "s " + r.Describe());
        }

        // ================================================================== WP-021B · ADR-018-1 ayak sesi

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AyakSesi_AnimasyonOlayi_YuzeyeGoreKlip()
        {
            AudioEventRouter r = _d.Router;
            int once = r.FootstepCount;
            var events = _s.Rig.GetComponentInChildren<DD.Prototype.Characters.CharacterAnimationEvents>(true);
            if (events == null)
            {
                Assert.Ignore("Oyuncu rig'inde CharacterAnimationEvents yok (prosedürel karakter modu).");
            }

            // CI kökü (01e3ef5 / 0555653): önceki sınıfların SetUp'ı CharacterAnimationEvents.ResetStatics ile Any'i siliyordu;
            // yönlendirici sahne değişiminde yeniden bağlanır (AudioEventRouter.Rearm); tanı satırı rearm sayısını gösterir.
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.FootLeft);
            Assert.AreEqual(once + 1, r.FootstepCount, "foot_l ayak sesi üretmedi. " + AyakTani(events));
            // WP-021C: the surface under the rig is whatever the world's tags + routes say (Default still reads as stone).
            AudioSoundId beklenen = AyakBeklenen(_s.Rig.transform.position, out SurfaceKind altta);
            Assert.AreEqual(beklenen, r.LastFootstep, "Ayak sesi altındaki yüzeye/plakaya göre çalmalı (" + altta + ").");   // DEC-102: the wet layer plays after it
            yield return new WaitForSecondsRealtime(0.2f);   // PlayerFootstep MinRetrigger 0.08 s
            int ikinci = r.FootstepCount;
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.FootRight);
            Assert.AreEqual(ikinci + 1, r.FootstepCount, "foot_r ayak sesi üretmedi. " + AyakTani(events));
            Assert.AreEqual(AudioSoundId.FootstepWood, AudioEventRouter.FootstepFor(DD.Prototype.Feedback.SurfaceKind.Wood));
            Assert.AreEqual(AudioSoundId.FootstepWater, AudioEventRouter.FootstepFor(DD.Prototype.Feedback.SurfaceKind.Water));
            Debug.Log("[DD-TEST] wp021b.footstep " + r.Describe());
        }

        /// <summary>CI kanıtı: ayak sesi susarsa neden (oyuncu kaydı, kaynak, havuz).</summary>
        private string AyakTani(DD.Prototype.Characters.CharacterAnimationEvents events)
        {
            var sb = new System.Text.StringBuilder("[tanı] kayıt=");
            foreach (var rig in DD.Prototype.PlayerSystem.PlayerRegistry.All)
            {
                sb.Append(ReferenceEquals(rig, null) ? "ref-null" : rig == null ? "YOK-EDİLMİŞ" : rig.name + (rig == _s.Rig ? "(bu)" : "(başka)")).Append(',');
            }

            sb.Append(" primary=").Append(DD.Prototype.PlayerSystem.PlayerRegistry.Primary == null ? "null" : DD.Prototype.PlayerSystem.PlayerRegistry.Primary.name);
            sb.Append(" kaynakAltında=").Append(events.transform.IsChildOf(_s.Rig.transform));
            sb.Append(" havuz=").Append(_d.WorldPool.Describe()).Append(" düşen=").Append(_d.DroppedCues).Append(" rearm=").Append(_d.Router.RearmCount);
            return sb.ToString();
        }

        /// <summary>WP-058B: the expected footstep = plate variant (AudioSurfaceHints.FootstepVariant) or the kind's default.</summary>
        private static AudioSoundId AyakBeklenen(Vector3 root, out SurfaceKind kind)
        {
            int mask = (1 << DD.Prototype.Core.PrototypeLayers.Environment) | (1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder);
            if (Physics.Raycast(root + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore))
            {
                kind = AudioSurfaceHints.Resolve(hit.collider, hit.point);
                return AudioSurfaceHints.Footstep(hit.collider, hit.point, kind, AudioEventRouter.FootstepFor);
            }

            kind = SurfaceKind.Default;
            return AudioEventRouter.FootstepFor(kind);
        }

        private static SurfaceKind YuzeyAltinda(Vector3 root)
        {
            int mask = (1 << DD.Prototype.Core.PrototypeLayers.Environment) | (1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder);
            return Physics.Raycast(root + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore)
                ? AudioSurfaceHints.Resolve(hit.collider, hit.point)
                : SurfaceKind.Default;
        }

        // ================================================================== WP-021C · UI olayları (WP-022B)

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UiOlaylari_BagliSatirCalar_RezerveSatirCiftCalmaz()
        {
            AudioEventRouter r = _d.Router;
            int once = _d.PlayedCount, ui = r.UiCount;

            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.ButtonHover);
            Assert.AreEqual(once + 1, _d.PlayedCount, "ButtonHover ses üretmedi.");
            Assert.AreEqual(AudioSoundId.UiHover, _d.LastPlayed);
            Assert.AreEqual(ui + 1, r.UiCount);

            // Reserved row: the same frame's owner (UiSound.Move → PrototypeAudio.Navigate) plays; the UiEvent path stays silent.
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.FocusMoved);
            Assert.AreEqual(once + 1, _d.PlayedCount, "FocusMoved rezerve satır: UiEvent yolu çalmamalı (çift ses).");

            yield return null;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.HudThreatEncircled, "SURROUNDED");
            Assert.AreEqual(AudioSoundId.HudThreatEncircled, _d.LastPlayed, "SURROUNDED çipi kendi sesini çalmalı.");
            yield return null;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.NewBestRound);
            Assert.AreEqual(AudioSoundId.UiNewBest, _d.LastPlayed);

            // Reveal now reaches the library through UiSound (one voice, no local synth).
            yield return new WaitForSecondsRealtime(0.25f);   // UiSound retrigger window + UiReveal MinRetrigger
            int reveal = _d.PlayedCount, dropped = _d.DroppedCues;
            DD.Prototype.UI.UiSound.Play(DD.Prototype.UI.UiSoundKind.Reveal);
            Assert.AreEqual(reveal + 1, _d.PlayedCount, "UiSound.Reveal director'a ulaşmadı (dropped " + dropped + "→" + _d.DroppedCues + ", enabled " + DD.Prototype.UI.UiSound.Enabled + ").");
            Assert.AreEqual(AudioSoundId.UiReveal, _d.LastPlayed);
            Debug.Log("[DD-TEST] wp021c.ui " + r.Describe());
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnaSes_UiSettingsSozlesmesi_MixerParametresiniRampaIleTakipEder()
        {
            Assert.IsTrue(_d.MixerOwnsMaster, "Mixer varlığı yok: ana ses AudioListener'da kalır.");
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            try
            {
                DD.Prototype.UI.UiSettings.MasterVolume = 0.5f;   // S-09 contract: property → PrototypeAudio.MasterVolume + MasterVolumeChanged
                Assert.AreEqual(0.5f, PrototypeAudio.MasterVolume, 1e-4f);
                Assert.AreEqual(1f, AudioListener.volume, 1e-4f, "Mixer sahipken listener 1 kalmalı (çift zayıflatma yok).");
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.IsTrue(mixer.GetFloat(AudioMixerBridge.VolumeParams[0], out float db));
                Assert.AreEqual(-6.02f, db, 0.1f, "MasterVolume parametresi −6 dB olmalı.");

                DD.Prototype.UI.UiSettings.MasterVolume = 1f;
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.IsTrue(mixer.GetFloat(AudioMixerBridge.VolumeParams[0], out db));
                Assert.AreEqual(0f, db, 0.1f);
            }
            finally
            {
                DD.Prototype.UI.UiSettings.MasterVolume = 1f;
                PrototypeAudio.MasterVolume = 1f;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Aggro_SpawnLandBeat_TierVokali()
        {
            // WP-048: the aggro growl rides the WP-040 spawn_land beat (rise_end clip or an immediate
            // land on a spawn without a rise); the earth is the spawn_rise beat when the body rises.
            AudioEventRouter r = _d.Router;
            int once = r.AggroCount, landOnce = DD.Prototype.Enemies.EnemyCombatEvents.Count(DD.Prototype.Enemies.EnemyCombatEventKind.SpawnLand);
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 4f));
            _s.Rig.Health.DiagnosisInvulnerable = true;
            yield return SpecialWeaponSahne.Bekle(() => DD.Prototype.Enemies.EnemyCombatEvents.Count(DD.Prototype.Enemies.EnemyCombatEventKind.SpawnLand) > landOnce, 8f, "spawn_land beat gelmedi.");
            Assert.GreaterOrEqual(r.AggroCount, once + 1, "spawn_land aggro vokali üretmedi.");
            Assert.AreNotEqual(AudioEventMap.ZombieAggro(DD.Prototype.Core.Config.EnemyTier.Ultra), AudioEventMap.ZombieAggro(DD.Prototype.Core.Config.EnemyTier.Special));
            Assert.AreEqual(AudioSoundId.ArmorBreakHelmet, AudioEventMap.ArmorBreakRegion(DD.Prototype.Core.Config.ArmorSlot.Helmet));
            Assert.AreEqual(AudioSoundId.ArmorBreakLeg, AudioEventMap.ArmorBreakRegion(DD.Prototype.Core.Config.ArmorSlot.Leg));
            Debug.Log("[DD-TEST] wp048.aggro tier=" + z.Tier + " beats=" + DD.Prototype.Enemies.EnemyCombatEvents.Total + " " + r.Describe());
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator YuzeyEtiketleri_DunyaYuklenince_Uygulanir_ZeminCozulur()
        {
            // The test assembly does not reference DD.Art; the world root and the tags it adds are observable without it.
            GameObject world = GameObject.Find("[AP005 World]");
            if (world == null)
            {
                Assert.Ignore("Run sahnesi AP005 dünyasını yüklemedi (prosedürel dünya): etiketleme uygulanmaz.");
            }

            int tagged = world.GetComponentsInChildren<SurfaceTag>(true).Length;
            Assert.GreaterOrEqual(tagged, 2000, "Placement örnekleri etiketlenmedi (Ap005SurfaceTags).");
            SurfaceKind altta = YuzeyAltinda(_s.Rig.transform.position);
            Assert.AreNotEqual(SurfaceKind.Default, altta, "Oyuncu başlangıcındaki zemin etiketli olmalı.");
            Assert.IsNotNull(AudioSurfaceHints.Refine, "Rota iyileştirmesi kayıtlı değil.");
            Debug.Log("[DD-TEST] wp021c.surface tagged=" + tagged + " underPlayer=" + altta + " refined=" + AudioSurfaceHints.RefinedCount);
            yield return null;
        }

        // ================================================================== WP-057 · müzik v2

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator MuzikV2_TehditKatmaniTaban_Stinger_MenuSonucDongusu()
        {
            MusicDirector m = _d.Music;
            try
            {
                // Threat layer floor: layer 2 raises percussion + tension even at low intensity, and holds them.
                m.IntensityOverride = null;
                m.ThreatLayerOverride = 2;
                yield return new WaitForSecondsRealtime(2.0f);
                Assert.GreaterOrEqual(m.Level(MusicDirector.Percussion), 0.5f, "L2: vurmalı taban");
                Assert.GreaterOrEqual(m.Level(MusicDirector.Tension), 0.4f, "L2: gerilim taban");
                m.ThreatLayerOverride = 0;
                m.IntensityOverride = 0.05f;
                yield return new WaitForSecondsRealtime(3.0f);
                Assert.LessOrEqual(m.Level(MusicDirector.Tension), 0.05f, "L0 + düşük yoğunluk: gerilim gider");

                // Stinger: plays over the layers and ducks them.
                int once = m.StingerCount;
                Assert.IsTrue(m.PlayStinger(AudioSoundId.StingerRoundBreak), "StingerRoundBreak klibi yok");
                Assert.AreEqual(once + 1, m.StingerCount);
                Assert.AreEqual(AudioSoundId.StingerRoundBreak, m.LastStinger);
                yield return null;
                Assert.Less(m.StingerDuckLevel, 0.7f, "katmanlar stinger sırasında duck edilmeli");

                // Phase loops: menu when out of the run, results after a run end.
                m.SetPhase(RunPhase.None);
                yield return SpecialWeaponSahne.Bekle(() => m.PhaseLoop == AudioSoundId.MusicMenu && m.PhaseLoopLevel > 0.2f, 12f, "Menü döngüsü yükselmedi.");
                yield return SpecialWeaponSahne.Bekle(() => m.Level(MusicDirector.Base) <= 0.15f, 8f, "menüde koşu katmanları çekilmedi (PullbackSeconds 3 s).");
                m.OnRunEnded(false);
                Assert.AreEqual(AudioSoundId.StingerRunEnd, m.LastStinger);
                yield return SpecialWeaponSahne.Bekle(() => m.PhaseLoop == AudioSoundId.MusicResults && m.PhaseLoopLevel > 0.2f, 12f, "Sonuç döngüsü yükselmedi.");
                Debug.Log("[DD-TEST] wp057.music " + m.Describe());
            }
            finally
            {
                m.ThreatLayerOverride = null;
                m.IntensityOverride = null;
                m.OnRunStarted();
                m.SetPhase(RunPhase.Fighting);
            }
        }

        /// <summary>WP-065E: a score whose sources were stopped from outside (audio device / configuration reset) comes back by itself, in lock-step.</summary>
        [UnityTest]
        [Timeout(30000)]
        public IEnumerator Muzik_KaynaklarDisaridanDurunca_Kendiliginden_Doner()
        {
            MusicDirector m = _d.Music;
            yield return SpecialWeaponSahne.Bekle(() => m.Started && m.LayersPlaying, 10f, "Müzik başlamadı.");
            int once = m.LayerRestarts;
            m.StopSourcesForTest();
            Assert.IsFalse(m.LayersPlaying, "öncül: kaynaklar durdu");
            yield return SpecialWeaponSahne.Bekle(() => m.LayersPlaying, 2f, "Durmuş skor kendiliğinden dönmedi (sessiz kalırdı).");
            Assert.AreEqual(once + 1, m.LayerRestarts, "Tek bir kilitli yeniden başlatma bekleniyor.");
            Debug.Log("[DD-TEST] wp065e.heal " + m.DescribeSources());
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator MixGecisi_FazOlcumu_LowHealthMuzikDuck()
        {
            // WP-057 mix pass: loudness per phase (report) + the HudLowHealth ↔ taiko rule (Music bus ducks).
            // WP-065D: measured on the CONTINUOUS output (AudioOutputCapture, 3 s, gated BS.1770 LUFS-I) with the
            // score seeked to the same bars. The old 45 × GetOutputData(2048) read ONE 43 ms buffer 45 times in
            // batch (45 frames ≈ 0.05 s), so the phase read whichever instant of the score it landed on (−22…−39).
            float fightDb = 0f, breakDb = 0f, menuDb = 0f;
            const float ScorePosition = 20f;
            IEnumerator Measure(System.Action<float> sink)
            {
                _d.Music.SeekForTest(ScorePosition);
                string path = System.IO.Path.Combine(Application.temporaryCachePath, "wp057-phase.wav");
                AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, path);
                yield return new WaitForSecondsRealtime(3f);
                long frames = cap.FramesWritten;
                long dropped = cap.DroppedSamples;
                int taps = UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true).Length;
                UnityEngine.Object.DestroyImmediate(cap);
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                Debug.Log("[DD-TEST] wp065e.capture frames " + frames + " (3 s = " + (3 * AudioSettings.outputSampleRate) + ") dropped " + dropped + " taps " + taps + " · " + _d.Music.DescribeSources());
                if (frames == 0) { sink(float.NaN); yield break; }
                byte[] raw = System.IO.File.ReadAllBytes(path);
                int ch = raw[22] | (raw[23] << 8);
                int rate = raw[24] | (raw[25] << 8) | (raw[26] << 16) | (raw[27] << 24);
                int n = (raw.Length - 44) / (2 * ch);
                var mono = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < ch; c++) { int o = 44 + 2 * (i * ch + c); sum += (short)(raw[o] | (raw[o + 1] << 8)) / 32768f; }
                    mono[i] = sum / ch;
                }

                sink(LoudnessMeter.Measure(mono, rate).IntegratedLufs);
            }

            MusicDirector m = _d.Music;
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            try
            {
                // WP-065E: the fight measure used to INHERIT the phase / snapshot / mixer state from whatever ran
                // before it. In the full sequential PlayMode run a preceding suite leaves the score out of Fighting
                // (layers at 0 → the "fight" reads the beds alone, ≈ −38 LUFS). Log what we found, then set it.
                var entry = new System.Text.StringBuilder("phase ").Append(m.Phase).Append(" snap ").Append(_d.Bridge.Current)
                    .Append(" base ").Append(m.Level(MusicDirector.Base).ToString("0.00")).Append(" perc ").Append(m.Level(MusicDirector.Percussion).ToString("0.00"))
                    .Append(" master ").Append(PrototypeAudio.MasterVolume.ToString("0.00")).Append(" muted ").Append(_d.Mixer.Muted)
                    .Append(" listener ").Append(AudioListener.pause ? "paused " : "live ").Append(AudioListener.volume.ToString("0.00"))
                    .Append(" musicDuck ").Append(_d.Mixer.DuckLevel(AudioBus.Music).ToString("0.00")).Append(" musicUser ").Append(_d.Mixer.GetUserVolume(AudioBus.Music).ToString("0.00"))
                    .Append(" threatOvr ").Append(m.ThreatLayerOverride.HasValue ? m.ThreatLayerOverride.Value.ToString() : "-");
                for (int i = 1; i < AudioMixerBridge.VolumeParams.Length; i++)
                {
                    if (mixer != null && mixer.GetFloat(AudioMixerBridge.VolumeParams[i], out float db)) entry.Append(' ').Append(AudioMixerBridge.VolumeParams[i]).Append('=').Append(db.ToString("0.0"));
                    mixer?.ClearFloat(AudioMixerBridge.VolumeParams[i]);   // snapshot owns the groups; only Master is the director's parameter
                }

                int listeners = 0;
                foreach (var l in UnityEngine.Object.FindObjectsOfType<AudioListener>()) if (l.isActiveAndEnabled) listeners++;
                int playing = 0, virt = 0;
                foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>()) { if (src.isPlaying) playing++; if (src.isPlaying && src.isVirtual) virt++; }
                entry.Append(" listeners ").Append(listeners).Append(" sources ").Append(playing).Append(" virtual ").Append(virt)
                     .Append(" · ").Append(m.DescribeSources()).Append("layersPlaying ").Append(m.LayersPlaying)
                     .Append(" · audioConfigResets ").Append(PrototypeAudioDirector.AudioConfigurationChanges)
                     .Append(" layerRestarts ").Append(m.LayerRestarts).Append(" bedRestarts ").Append(_d.Ambience.BedRestarts);
                Debug.Log("[DD-TEST] wp065e.entry " + entry);

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                AudioListener.pause = false;
                PrototypeAudio.MasterVolume = 1f;
                _d.Mixer.SetUserVolume(AudioBus.Music, 1f);
                m.ThreatLayerOverride = 0;
                m.IntensityOverride = 0.9f; m.AccentOverride = true;
                m.OnRunStarted();
                m.SetPhase(RunPhase.Fighting);
                _d.Ambience.SetPhase(RunPhase.Fighting);
                _d.Bridge.Transition(AudioSnapshot.Run, 0.1f);
                // A score found stopped here heals itself within 0.5 s (MusicDirector.HealLayers) - the product fix.
                yield return SpecialWeaponSahne.Bekle(() => m.LayersPlaying && m.Level(MusicDirector.Base) >= 0.55f && m.Level(MusicDirector.Percussion) >= 0.5f
                                                            && _d.Mixer.DuckLevel(AudioBus.Music) > 0.99f, 10f, "Dövüş katmanları kurulmadı / çalmıyor.");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Measure(v => fightDb = v);
                if (float.IsNaN(fightDb)) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                m.SetPhase(RunPhase.Upgrading); _d.Bridge.Transition(AudioSnapshot.RoundBreak, 0.2f);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Measure(v => breakDb = v);
                m.SetPhase(RunPhase.None); _d.Bridge.Transition(AudioSnapshot.Menu, 0.2f);
                yield return new WaitForSecondsRealtime(3.5f);
                yield return Measure(v => menuDb = v);
                Debug.Log("[DD-TEST] wp057.mix fight=" + fightDb.ToString("0.0") + " LUFS break=" + breakDb.ToString("0.0") + " menu=" + menuDb.ToString("0.0") + " · " + m.Describe());
                Assert.IsFalse(float.IsNaN(fightDb) || float.IsInfinity(fightDb), "Fight LUFS sonlu değil.");
                Assert.IsFalse(float.IsNaN(breakDb) || float.IsInfinity(breakDb), "Break LUFS sonlu değil.");
                Assert.IsFalse(float.IsNaN(menuDb) || float.IsInfinity(menuDb), "Menu LUFS sonlu değil.");
                Assert.Greater(fightDb, -60f, "Fight ölçümü anlamlı ses içermiyor.");
                Assert.Greater(breakDb, -60f, "Break ölçümü anlamlı ses içermiyor.");
                Assert.Greater(menuDb, -60f, "Menu ölçümü anlamlı ses içermiyor.");
                Assert.Greater(fightDb, menuDb - 0.1f, "Dövüş karışımı menüden sessiz olmamalı.");

                // WP-058B: L3 combat trim on the score.
                m.SetPhase(RunPhase.Fighting); _d.Bridge.Transition(AudioSnapshot.Run, 0.2f);
                m.ThreatLayerOverride = 3;
                yield return new WaitForSecondsRealtime(2.0f);
                Assert.Less(m.CombatTrim, 0.85f, "L3'te müzik −2 dB trim almalı.");
                m.ThreatLayerOverride = 0;
            }
            finally
            {
                m.IntensityOverride = null; m.AccentOverride = null; m.ThreatLayerOverride = null;
                m.SetPhase(RunPhase.Fighting); _d.Bridge.Transition(AudioSnapshot.Run, 0.2f);
            }

            // HudLowHealth ducks the Music bus (gate M10 / designer rule 2).
            Assert.AreEqual(1f, _d.Mixer.DuckLevel(AudioBus.Music), 0.01f, "öncül: müzik duck yok");
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.HudLowHealthEntered);
            yield return null; yield return null;
            Assert.Less(_d.Mixer.DuckLevel(AudioBus.Music), 0.95f, "HudLowHealth müzik bus'ını duck etmeli (−3 dB).");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.Greater(_d.Mixer.DuckLevel(AudioBus.Music), 0.9f, "duck 1 s içinde bırakılmalı");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SpecialTepkisi_SwShockSlideBeat_GovdeSesi()
        {
            AudioEventRouter r = _d.Router;
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 5f));
            yield return null;
            int once = r.ReactionCount;
            var shock = new DD.Prototype.Enemies.EnemyCombatEventInfo(DD.Prototype.Enemies.EnemyCombatEventKind.SpecialShock, z.transform, z.Tier, z.LifeId,
                z.transform.position, Vector3.forward, DD.Prototype.Core.Config.ArmorSlot.None, false, 0.4f);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in shock);
            Assert.AreEqual(once + 1, r.ReactionCount, "sw_shock gövde döngüsü başlamadı.");
            Assert.AreEqual(1, _d.Special.LoopsPlaying, "gövde başına bir çatırtı döngüsü");
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in shock);   // the same body again: extends, no second loop
            Assert.AreEqual(1, _d.Special.LoopsPlaying);
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(0, _d.Special.LoopsPlaying, "Electrocuted süresi bitince döngü söner");
            Assert.AreEqual(AudioSoundId.SwArcShockEnd, _d.Special.LastSound, "sönüş 'sss'");
            var slide = new DD.Prototype.Enemies.EnemyCombatEventInfo(DD.Prototype.Enemies.EnemyCombatEventKind.SpecialSlide, z.transform, z.Tier, z.LifeId,
                z.transform.position, Vector3.forward, DD.Prototype.Core.Config.ArmorSlot.None, false, 0f);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in slide);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in slide);   // a crowd in the same frame slides as one
            Assert.AreEqual(AudioSoundId.SwGelSlideEnemy, _d.LastPlayed);
            Assert.AreEqual(once + 2, r.ReactionCount, "sw_slide 60 ms kümeleme");
            Debug.Log("[DD-TEST] wp072b4.beats " + r.Describe());
        }

        // ================================================================== WP-058 · WP-056 Special kullanım sesleri

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SpecialKullanim_RaijuNamekuji_KendiSesleri_ZincirBirikinti()
        {
            AudioEventRouter r = _d.Router;
            SpecialAudio sp = _d.Special;
            Assert.IsTrue(sp.Produced, "v019 Special kimlikleri katalogda değil");
            Vector3 el = _s.Rig.transform.position + Vector3.up;
            Vector3 at = _s.Zemin(0f, 6f);
            Vector3 at2 = at + Vector3.right * 8f;
            var raiju = DD.Prototype.Core.Config.SpecialWeaponId.Raiju;
            var gel = DD.Prototype.Core.Config.SpecialWeaponId.Namekuji;
            void Yayinla(DD.Prototype.Core.Config.SpecialWeaponId id, DD.Prototype.Weapons.SpecialUsageKind k, Vector3 p, int count) =>
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(id, k, "wp072b4", p, Vector3.forward, 1.6f, 0.25f, count);

            // Raiju: ready → charge → fire; the legacy heavy fire gives way while it is in hand.
            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.Activated, el, 0);
            Assert.AreEqual(AudioSoundId.SwArcReady, _d.LastPlayed);
            Assert.IsTrue(sp.InHand);
            yield return new WaitForSecondsRealtime(0.1f);
            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.ChargeStarted, el, 0);
            Assert.AreEqual(AudioSoundId.SwArcCharge, _d.LastPlayed);
            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.Fired, el, 0);
            Assert.AreEqual(AudioSoundId.SwArcFire, _d.LastPlayed);
            AudioEvents.Raise(CombatAudioEvent.HeavyWeaponFire, el, 1f, 60);
            Assert.AreEqual(AudioSoundId.SwArcFire, _d.LastPlayed, "Special eldeyken eski HeavyWeaponFire çalmamalı");

            // Chain: hop 0..2 in one frame → three crackles, 40 ms apart; the orb's end right after = no fizzle.
            // WP-087 (C-00 05.10, BEKLEYEN §14): a test clock. The hops fall due on game time (Time.time, 40 ms apart) and the voice
            // pool's same-sound retrigger window is real time (Time.unscaledTime, 20 ms): on a loaded machine one long frame made all
            // three hops due in one SpecialAudio.Tick and the window kept only the first (1–2 of 3 in the full suite,
            // K/wp-085/evidence/sp); a fixed capture step does the opposite (game time races real time). At timeScale 0.1 the hops are
            // 400 ms of real time apart and a frame — even one at the 0.333 s cap — brings at most one due. Mechanism unchanged.
            float scale0 = Time.timeScale;
            Time.timeScale = 0.1f;
            try
            {
                int played = sp.Played;
                for (int hop = 0; hop < 3; hop++) Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.Hit, at + Vector3.right * hop, hop);
                Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, at, 0);
                Assert.AreEqual(played, sp.Played, "zincir sıçramaları 40 ms aralıkla gecikmeli");
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual(played + 3, sp.Played, "üç sıçrama, sönüş yok");
                Assert.AreEqual(AudioSoundId.SwArcChainJump, sp.LastSound);
                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                Time.timeScale = scale0;
            }

            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, at2, 0);
            Assert.AreEqual(AudioSoundId.SwArcShockEnd, _d.LastPlayed, "gövdesiz biten küre söner");
            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.Deactivated, el, 0);
            Assert.IsFalse(sp.InHand);
            AudioEvents.Raise(CombatAudioEvent.HeavyWeaponFire, el, 1f, 60);
            Assert.AreEqual(AudioSoundId.HeavyFire, _d.LastPlayed, "Special bırakılınca eski ağır ateş geri gelir");

            // Namekuji: puddles loop per id, two at most; FieldEnded by id; the Special's end clears the rest.
            yield return new WaitForSecondsRealtime(0.1f);
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.Fired, el, 0);
            Assert.AreEqual(AudioSoundId.SwGelFire, _d.LastPlayed);
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, at, 7);
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, at2, 8);
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, at + Vector3.left * 8f, 9);
            Assert.AreEqual(2, sp.PuddlesPlaying, "birikinti döngüsü tavanı 2");
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, at2, 8);
            Assert.AreEqual(AudioSoundId.SwGelPuddleExpire, _d.LastPlayed);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, sp.PuddlesPlaying, "id'si eşleşen birikinti söndü");
            Yayinla(gel, DD.Prototype.Weapons.SpecialUsageKind.Hit, at, 0);
            Assert.AreEqual(AudioSoundId.SwGelHitFlesh, _d.LastPlayed);
            DD.Prototype.Weapons.WeaponEvents.PublishSpecialEnded();
            Assert.AreEqual(0, _d.FieldLoopsPlaying, "Special bitişi birikinti döngülerini temizler");
            Assert.Greater(r.SpecialUsageCount, 0);

            AudioEvents.RaiseLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at, 0.5f, 55);
            AudioEvents.RaiseLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at2, 0.5f, 55);
            Assert.AreEqual(1, _d.LoopCount, "Paylaşılan beacon tek fiziksel loop olmalı.");
            AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.AreEqual(1, _d.LoopCount, "İlk claim bitişi ikinci claim'i kesmemeli.");
            Assert.Less(Vector3.Distance(at2, _d.LoopPosition(CombatAudioEvent.SpecialWeaponBeaconLoop)), 0.01f,
                "Kalan claim loop'un konumsal sahibini devralmalı.");

            AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at2);
            yield return new WaitForSecondsRealtime(0.05f);
            AudioEvents.RaiseLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at, 0.5f, 55);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(1, _d.LoopCount, "Fade sırasında yeni claim loop'u yeniden sahiplenmeli.");
            AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, at);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.AreEqual(0, _d.LoopCount, "Son claim bitince beacon loop kapanmalı.");
            Debug.Log("[DD-TEST] wp058.special " + r.Describe());
        }

        // ================================================================== WP-048B · WP-032C silah foley

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SilahFoley_KlipOlayi_SecilenSilahinKendiSesi()
        {
            AudioEventRouter r = _d.Router;
            var events = _s.Rig.GetComponentInChildren<DD.Prototype.Characters.CharacterAnimationEvents>(true);
            if (events == null)
            {
                Assert.Ignore("Oyuncu rig'inde CharacterAnimationEvents yok (prosedürel karakter modu).");
            }

            var weapon = DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon;
            int once = r.FoleyCount;
            if (_d.Library.IsProduced(AudioSoundId.FoleyMediumRaised))
            {
                // WP-072 B2: the equipped weapon's own v3 set (A → AR1); an auto rifle's per-shot eject is silent (the casing lands instead).
                string id = WeaponFoleyV3.Equipped();
                Assert.AreEqual("AR1", id, "A tüfeği WP-069 audioId'si AR1.");
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Eject);
                Assert.AreEqual(once, r.FoleyCount, "AR1 eject foley'si sessiz olmalı.");
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Chamber);
                Assert.AreEqual(AudioSoundId.FoleyAr1Chamber, _d.LastPlayed, "AR1 chamber = sürgü bırakma.");
                yield return new WaitForSecondsRealtime(0.1f);
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Charge);
                Assert.AreEqual(AudioSoundId.FoleyAr1Charge, _d.LastPlayed, "v3 charge olayı AR1 kurma sesi.");
                yield return new WaitForSecondsRealtime(0.1f);
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.AdsReady);
                Assert.AreEqual(AudioSoundId.FoleyMediumAds, _d.LastPlayed, "ADS = Medium ailesi kumaş + kundak.");
                once = r.FoleyCount - 3;
            }
            else
            {
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Eject);
                Assert.AreEqual(once + 1, r.FoleyCount, "eject foley üretmedi.");
                Assert.AreEqual(AudioEventMap.WeaponFoley(weapon, DD.Prototype.Characters.CharacterAnimationEvent.Eject), _d.LastPlayed, "Seçili silahın kendi eject sesi.");
                yield return new WaitForSecondsRealtime(0.1f);
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Chamber);
                Assert.AreEqual(AudioEventMap.WeaponFoley(weapon, DD.Prototype.Characters.CharacterAnimationEvent.Chamber), _d.LastPlayed);
            }
            yield return new WaitForSecondsRealtime(0.1f);
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.PickupDone);
            Assert.AreEqual(AudioSoundId.PickupDone, _d.LastPlayed);
            Assert.IsTrue(AmbienceDirector.WetLook, "Look B (WP-029 B4) → WetLook açık.");
            Debug.Log("[DD-TEST] wp048b.foley weapon=" + weapon + " " + r.Describe() + " · thunder " + _d.Ambience.ThunderCount + " pond " + _d.Ambience.PondPlaying);
        }

        // ================================================================== WP-072 B1 / WP-085 B1 · ateş sesi: her atış tam ses

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AtesV4_HerAtisTamSes_BirakistaTekKuyruk_MekanKuyrugu()
        {
            WeaponFireAudio f = _d.WeaponFire;
            if (!_d.Library.IsProduced(AudioSoundId.WeaponSmg1Fire))
            {
                Assert.Fail("v016 ateş kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 at = _s.Rig.transform.position + Vector3.up;
            int sesler = f.FireVoices, releases = f.Releases;

            // SMG1 kendi hızında (900 RPM, R2 S2): her atış tam SMG1 sesi — hibrit transient/döngü yok; bırakınca kuyruk bir kez.
            // Deterministik (WP-051C kararsızlığı): oyun zamanı kare başına tam 1/60 s — 4 kare = 66,7 ms aralık, duvar
            // saatindeki takılma seriyi bölemez; her atış 2 m ayrı → AudioEvents yinelenen-ipucu kısması (1,6 m / 40 ms) devreye girmez.
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    // Two points 2 m apart (beats the 1.6 m duplicate-cue merge) inside the 12 m Near band (WP-085 B5: a cue beyond
                    // it would now pick the weapon's DistanceMid voice).
                    AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", at + Vector3.right * (2f * (i % 2)), 0.75f, 50);
                    Assert.AreEqual(AudioSoundId.WeaponSmg1Fire, f.LastVoice, "Atış " + (i + 1) + ": tam SMG1 sesi.");
                    // R2 S2: her atış tam katman ister (hafif katman yok). Havuzun 30 ms yeniden tetik penceresi gerçek zamanla sayar;
                    // sabit oyun zamanında kareler çok hızlı akarsa reddedebilir — oyunda 900 RPM = 66 ms gerçek aralık, gerçek RPM kiti (B4) ölçer.
                    Assert.AreEqual(sesler + i + 1, f.FireVoices, "Atış " + (i + 1) + ": tam katman istendi.");
                    for (int k = 0; k < 4; k++) yield return null;
                }

                Assert.AreEqual(releases, f.Releases, "Tetik basılıyken kuyruk yok.");
                for (int k = 0; k < 15; k++) yield return null;   // 0,25 s oyun zamanı > bırakış penceresi (107 ms)
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }

            Assert.AreEqual(releases + 1, f.Releases, "Bırakış bir kez.");
            Assert.AreEqual(AudioSoundId.WeaponSmg1TailOpen, f.LastTail, "Varsayılan mekân Open — SMG1'in kendi AP-027 kuyruğu (WP-085 B5).");
            Assert.AreEqual(WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Open), f.LastTailDelay, 1e-5f, "Açık avlu ilk yansıma ofseti.");

            // Tek atış sınıfı: her atışta kuru ses + mekân kuyruğu; mekân çözücüsü kuyruğu seçer.
            System.Func<Vector3, AcousticSpace> onceki = AcousticSpaces.Resolver;
            AcousticSpaces.Resolver = _ => AcousticSpace.Hall;
            try
            {
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", at, 1.5f, 50);
                Assert.AreEqual(AudioSoundId.WeaponSg1Fire, f.LastVoice);
                Assert.AreEqual(AudioSoundId.WeaponSg1TailHall, f.LastTail, "Salon kuyruğu — SG1'in kendi AP-027 kuyruğu (WP-085 B5).");
            }
            finally
            {
                AcousticSpaces.Resolver = onceki;
            }

            // Kimliksiz ipucu eski yola düşer (A/B/C'nin v016'sız hali ve diğer üreticiler).
            yield return new WaitForSeconds(0.1f);
            AudioEvents.Raise(CombatAudioEvent.ClassicWeaponFire, at, 1f, 50);
            Assert.AreEqual(AudioSoundId.RifleFire, _d.LastPlayed, "Kimliksiz ateş eski RifleFire.");
            Debug.Log("[DD-TEST] wp085b1.fire voices=" + f.FireVoices + " refused=" + f.FireRefused + " releases=" + f.Releases + " tail=" + f.LastTail);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AtesV4_KatmanYolu_Mekanik_SilahKuyruguOfsetli_UzakBant()
        {
            // WP-085 B3: AP-027 layers stand in as produced v016 / v017 clips — the mechanism plays with the shot, the weapon's own
            // tail rings with the space's first-reflection offset, a far source picks its distance voice and drops the mechanism.
            WeaponFireAudio f = _d.WeaponFire;
            Assert.IsTrue(_d.Library.IsProduced(AudioSoundId.FoleySmg1Chamber) && _d.Library.IsProduced(AudioSoundId.WeaponTailArHall) && _d.Library.IsProduced(AudioSoundId.WeaponLmg1Fire));
            System.Func<Vector3, AcousticSpace> onceki = AcousticSpaces.Resolver;
            Vector3 at = _s.Rig.transform.position + Vector3.up;
            try
            {
                WeaponAudioIdentity.SetLayerForTests("SG1", WeaponLayer.Mechanical, AudioSoundId.FoleySmg1Chamber);
                WeaponAudioIdentity.SetLayerForTests("SG1", WeaponLayer.TailHall, AudioSoundId.WeaponTailArHall);
                WeaponAudioIdentity.SetLayerForTests("SG1", WeaponLayer.DistanceFar, AudioSoundId.WeaponLmg1Fire);
                AcousticSpaces.Resolver = _ => AcousticSpace.Hall;

                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", at, 1.5f, 50);
                Assert.AreEqual(WeaponDistanceBand.Near, f.LastBand, "Oyuncunun kendi atışı yakın.");
                Assert.AreEqual(AudioSoundId.WeaponSg1Fire, f.LastVoice);
                Assert.AreEqual(AudioSoundId.FoleySmg1Chamber, f.LastMechanical, "Mekanik katman atışla çalar.");
                Assert.AreEqual(AudioSoundId.WeaponTailArHall, f.LastTail, "Silahın kendi salon kuyruğu.");
                Assert.AreEqual(WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Hall), f.LastTailDelay, 1e-5f, "Salon ilk yansıma ofseti.");

                yield return new WaitForSeconds(0.2f);
                WeaponAudioIdentity.SetLayerForTests("SG1", WeaponLayer.Mechanical, AudioSoundId.FoleySmg2Chamber);
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", at + _s.Rig.transform.right * 60f, 1.5f, 50);
                Assert.AreEqual(WeaponDistanceBand.Far, f.LastBand, "60 m uzak bant.");
                Assert.AreEqual(AudioSoundId.WeaponLmg1Fire, f.LastVoice, "Uzak kaynak uzak kaydını çalar.");
                Assert.AreEqual(AudioSoundId.FoleySmg1Chamber, f.LastMechanical, "Uzakta mekanik kapalı.");

                // A layer that is not produced falls back to the class tail as rendered (no offset). WP-085 B5: SG1's own hall
                // tail ships now, so the test points the layer at an id no catalog carries.
                WeaponAudioIdentity.ClearLayersForTests();
                WeaponAudioIdentity.SetLayerForTests("SG1", WeaponLayer.TailHall, (AudioSoundId)9999);
                yield return new WaitForSeconds(0.2f);
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", at + Vector3.forward * 2f, 1.5f, 50);
                Assert.AreEqual(AudioSoundId.WeaponTailSgHall, f.LastTail);
                Assert.AreEqual(0f, f.LastTailDelay, "v016 sınıf kuyruğu kendi zamanlamasıyla.");
            }
            finally
            {
                WeaponAudioIdentity.ClearLayersForTests();
                AcousticSpaces.Resolver = onceki;
            }

            Debug.Log("[DD-TEST] wp085b3.layers band=" + f.LastBand + " voice=" + f.LastVoice + " mech=" + f.LastMechanical + " tail=" + f.LastTail);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator KovanV2_AyniKareIkiKabukAyriDuser_GecikmeBandi_HavuzTavani_SifirAyirma()
        {
            if (!_d.Library.IsProduced(AudioSoundId.CasingRifleStone))
            {
                Assert.Fail("v017 kovan kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            CasingAudio c = _d.Casings;
            c.ResetStats();
            Vector3 port = _s.Rig.transform.position + Vector3.up * 1.3f;
            var kovan = new DD.Prototype.Weapons.WeaponMechanismEvent(
                DD.Prototype.Weapons.WeaponMechanismKind.CasingEjected, DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, port, _s.Rig.transform.right, false);

            // The surface the casing should speak: the same down ray CasingAudio casts (port + 0.6 m along the throw).
            int mask = (1 << DD.Prototype.Core.PrototypeLayers.Environment) | (1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder);
            Assert.IsTrue(Physics.Raycast(port + _s.Rig.transform.right * 0.6f + Vector3.up * 0.2f, Vector3.down, out RaycastHit zemin, 4f, mask, QueryTriggerInteraction.Ignore), "Kovanın düşeceği zemin yok.");
            SurfaceKind zeminTuru = AudioSurfaceHints.Resolve(zemin.collider, zemin.point);
            string esli = WeaponFoleyV3.Equipped();
            AudioSoundId v4 = WeaponFoleyV3.CasingV4(esli, zeminTuru, AmbienceDirector.WetLook);   // WP-085 B5: the AP-027 voice when produced
            AudioSoundId beklenen = v4 != AudioSoundId.None && _d.Library.IsProduced(v4) ? v4 : WeaponFoleyV3.CasingOn(WeaponFoleyV3.Casing(esli), zeminTuru);
            int uyumsuz = 0, sesTavan = 0, kuyrukTavan = 0;
            IEnumerator Izle(float saniye)
            {
                float bitis = Time.realtimeSinceStartup + saniye;
                int landed = c.Landed;
                while (Time.realtimeSinceStartup < bitis)
                {
                    int canli = 0;
                    foreach (AudioSource src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                    {
                        if (src.isPlaying && src.clip != null && src.clip.name.StartsWith("Casing", StringComparison.Ordinal)) canli++;
                    }

                    sesTavan = Mathf.Max(sesTavan, canli);
                    kuyrukTavan = Mathf.Max(kuyrukTavan, c.InAir);
                    if (c.Landed != landed)
                    {
                        landed = c.Landed;
                        if (c.LastSound != beklenen) uyumsuz++;
                    }

                    yield return null;
                }
            }

            // M-21 (sg_2 kırma): aynı karede iki kabuk → ikisi de duyulur (aynı-ses yeniden tetik kısması tek vuruşa indirmez).
            int once = c.Landed;
            DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
            DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
            yield return Izle(1f);
            Assert.AreEqual(once + 2, c.Landed, "Aynı karedeki iki kovan ayrı ayrı düşer.");
            Assert.GreaterOrEqual(c.LastFallSeconds, 0.25f, "R2 S5: atış → düşme ≥ 250 ms.");
            Assert.LessOrEqual(c.LastFallSeconds, 0.7f, "R2 S5: atış → düşme ≤ 700 ms.");

            // Havuz tavanı: bir karede 30 kovan → kuyruk tavanı aşılmaz, fazlası sayılarak düşer, kuyruk boşalır; eşzamanlı kovan sesi ≤ 3.
            int dusen = c.Dropped;
            for (int i = 0; i < 30; i++) DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
            kuyrukTavan = Mathf.Max(kuyrukTavan, c.InAir);
            Assert.LessOrEqual(c.InAir, CasingAudio.Capacity);
            Assert.Greater(c.Dropped, dusen, "Taşan kovan sayılır.");
            yield return Izle(1f);
            Assert.AreEqual(0, c.InAir, "Kuyruk boşaldı.");
            Assert.LessOrEqual(sesTavan, AudioMixPlan.PlayerCasing(AudioSoundId.CasingRifleStone).MaxInstances, "Eşzamanlı kovan sesi tavanı.");

            // Sıfır ayırma (R2 S5): ısınmış yolda kovan olayı + kuyruk ses yolu GC ayırmaz.
            DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
            yield return Izle(1f);
            long b0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5; i++) DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
            long b1 = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, b1 - b0, "Kovan olayı yolu ayırma yapmamalı.");
            yield return Izle(1f);

            // Roketatar: kovan yok (CasingFamily.None) — kuşanılı rl_1 ile olay gelse de ses çıkmaz.
            int roket = 0;
            DD.Prototype.Core.Config.ClassicWeaponId oncekiSecim = DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon;
            try
            {
                foreach (DD.Prototype.Core.Weapons.ClassicWeaponDefinition d in DD.Prototype.Core.Weapons.WeaponRoster.Enabled)
                {
                    DD.Prototype.Core.Weapons.WeaponDefinition wd = DD.Prototype.Core.Weapons.Arsenal.Get(d.Id);
                    if (wd == null || wd.weaponId != "rl_1") continue;
                    _s.Controller.EquipClassic(d.Id, _s.Config);
                    yield return new WaitForSeconds(0.3f);
                    int r0 = c.Landed;
                    for (int i = 0; i < 3; i++) DD.Prototype.Weapons.WeaponEvents.PublishMechanism(kovan);
                    yield return new WaitForSeconds(1f);
                    roket = c.Landed - r0;
                }
            }
            finally
            {
                // C-00 05.10 (main-ci 285b7fe8): EquipClassic writes the process-wide WeaponRegistry.SelectedWeapon — give it back
                // (and the live controller its weapon), as CoreCombat T1 / RocketSliceTests d1c0f025 do.
                DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon = oncekiSecim;
                if (_s.Controller != null) _s.Controller.EquipClassic(oncekiSecim, _s.Config);
            }

            Assert.AreEqual(0, roket, "Roketatarda kovan sesi yok.");
            Assert.AreEqual(0, uyumsuz, "Her iniş zeminin varyantı (" + beklenen + ").");
            R2Olcum.Yaz("S5", "DD.Prototype.Tests.PlayMode.AudioSliceTests.KovanV2_AyniKareIkiKabukAyriDuser_GecikmeBandi_HavuzTavani_SifirAyirma", new Dictionary<string, double>
            {
                { "dropDelayMsMin", Math.Round(c.FallMinHeard * 1000.0, 1) }, { "dropDelayMsMax", Math.Round(c.FallMaxHeard * 1000.0, 1) },
                { "surfaceMismatches", uyumsuz }, { "poolMax", sesTavan }, { "queueMax", kuyrukTavan }, { "gcBytesPerCasing", (b1 - b0) / 5.0 }, { "rocketCasingSounds", roket }
            }, c.Landed - once);
            Debug.Log("[DD-TEST] wp085b2.casing landed=" + c.Landed + " bounces=" + c.Bounces + " yielded=" + c.BouncesYielded + " dropped=" + c.Dropped + " fall=" + c.FallMinHeard.ToString("0.000")
                      + "–" + c.FallMaxHeard.ToString("0.000") + " voicesMax=" + sesTavan + " queueMax=" + kuyrukTavan + " surface=" + beklenen + " mismatches=" + uyumsuz + " rocket=" + roket);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FoleyV3_KovanYuzeyeDuser_KuruTetikAilesi()
        {
            if (!_d.Library.IsProduced(AudioSoundId.CasingRifleStone))
            {
                Assert.Fail("v017 kovan kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 port = _s.Rig.transform.position + Vector3.up * 1.3f;
            int once = _d.Casings.Landed;
            DD.Prototype.Weapons.WeaponEvents.PublishMechanism(new DD.Prototype.Weapons.WeaponMechanismEvent(
                DD.Prototype.Weapons.WeaponMechanismKind.CasingEjected, DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, port, _s.Rig.transform.right, false));
            Assert.AreEqual(once, _d.Casings.Landed, "Kovan düşmeden ses yok (düşüş süresi).");
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(once + 1, _d.Casings.Landed, "Kovan yere düştü.");
            StringAssert.StartsWith("CasingAr1", _d.Casings.LastSound.ToString(), "AR1 = kendi AP-027 kovanı (WP-085 B5).");

            // Magazin boşken tetik: silahın ağırlık ailesi (A → Medium).
            AudioEvents.Raise(CombatAudioEvent.ClassicWeaponDryFire, port, 1f, 40);
            Assert.AreEqual(AudioSoundId.FoleyMediumDryFire, _d.LastPlayed, "AR1 kuru tetik Medium ailesi.");
            Debug.Log("[DD-TEST] wp072b2.casing landed=" + _d.Casings.Landed + " last=" + _d.Casings.LastSound);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator DarbeV3_MermiZemine_KalibreSesi_EskiIpucuYerineGecer()
        {
            if (!_d.Impacts.Active)
            {
                Assert.Fail("v018 darbe kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 yer = _s.Rig.transform.position + new Vector3(4f, 0f, 3f);
            Vector3 from = yer + Vector3.up * 3f;
            int once = _d.Impacts.Played, yerine = _d.Impacts.Replaced;
            var shot = new DD.Prototype.Weapons.ShotRequest
            {
                Origin = from, Direction = Vector3.down, Damage = 1f, Range = 10f,
                ImpactForce = DD.Prototype.Damage.DamageForce.RifleBullet, Source = DD.Prototype.Damage.DamageSourceKind.Bullet,
                Power = DD.Prototype.Feedback.WeaponPower.Heavy, PierceDamageFalloff = 1f
            };
            var launch = new DD.Prototype.Weapons.ProjectileLaunch
            {
                WeaponId = DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, Kind = DD.Prototype.Core.Weapons.ProjectileKind.Bullet,
                Caliber = DD.Prototype.Core.Weapons.WeaponCaliber.Heavy, Speed = 60f, Gravity = 0f, MaxRange = 10f, MaxLifetime = 2f,
                FalloffStart = 10f, FalloffEnd = 10f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
            DD.Prototype.Weapons.ProjectileSystem.Spawn(shot, launch);
            yield return SpecialWeaponSahne.Bekle(() => DD.Prototype.Weapons.ProjectileSystem.LiveCount == 0, 3f, "mermi zemine ulaşmadı");
            yield return null;   // LateUpdate: tutulan eski ipuçları boşaltılır

            Assert.AreEqual(once + 1, _d.Impacts.Played, "Zemine çarpan mermi v3 darbe sesi üretmedi.");
            StringAssert.EndsWith("Heavy", _d.Impacts.LastSound.ToString(), "Ağır kalibre → Heavy darbe.");
            Assert.AreEqual(yerine + 1, _d.Impacts.Replaced, "Aynı isabetin eski SurfaceImpact ipucu v3 ile değiştirilmeli (çift ses yok).");
            Debug.Log("[DD-TEST] wp072b3.impact played=" + _d.Impacts.Played + " replaced=" + _d.Impacts.Replaced + " last=" + _d.Impacts.LastSound);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UcusDongusu_RoketMenziliBitince_Kapanir_PatlamaMesafeKatmani()
        {
            if (!_d.Library.IsProduced(AudioSoundId.RocketFlightLoop))
            {
                Assert.Fail("v020 fırlatıcı kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 from = _s.Rig.transform.position + new Vector3(2f, 3f, 0f);
            int started = _d.Flight.Started, ended = _d.Flight.Ended;
            var shot = new DD.Prototype.Weapons.ShotRequest
            {
                Origin = from, Direction = Vector3.up, Damage = 1f, Range = 6f,
                ImpactForce = DD.Prototype.Damage.DamageForce.RifleBullet, Source = DD.Prototype.Damage.DamageSourceKind.Bullet,
                Power = DD.Prototype.Feedback.WeaponPower.Heavy, PierceDamageFalloff = 1f
            };
            var launch = new DD.Prototype.Weapons.ProjectileLaunch
            {
                WeaponId = DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle, Kind = DD.Prototype.Core.Weapons.ProjectileKind.Rocket,
                Caliber = DD.Prototype.Core.Weapons.WeaponCaliber.Explosive, Speed = 12f, Gravity = 0f, MaxRange = 6f, MaxLifetime = 2f,
                FalloffStart = 6f, FalloffEnd = 6f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
            DD.Prototype.Weapons.ProjectileSystem.Spawn(shot, launch);
            yield return SpecialWeaponSahne.Bekle(() => _d.Flight.Started > started, 2f, "roket uçuş döngüsü başlamadı");
            Assert.AreEqual(1, _d.Flight.Playing, "tek roket, tek döngü");
            yield return SpecialWeaponSahne.Bekle(() => DD.Prototype.Weapons.ProjectileSystem.LiveCount == 0, 3f, "roket menzili bitmedi");
            Assert.AreEqual(ended + 1, _d.Flight.Ended, "roket bitince döngü tam bir kez kapanmalı");
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(0, _d.Flight.Playing, "döngü söndü");

            // Patlama: dinleyiciye mesafeyle tek katman seçilir.
            AudioEvents.RaiseCritical(CombatAudioEvent.Explosion, Konum(3f), 1f, 90);
            Assert.AreEqual(AudioSoundId.ExplosionNear, _d.LastPlayed, "≤ 12 m yakın katman");
            yield return new WaitForSeconds(0.2f);
            AudioEvents.RaiseCritical(CombatAudioEvent.Explosion, _s.Rig.transform.position + new Vector3(40f, 1f, 20f), 1f, 90);
            Assert.AreEqual(AudioSoundId.ExplosionFar, _d.LastPlayed, "> 35 m uzak katman");
            Debug.Log("[DD-TEST] wp072b5.flight started=" + _d.Flight.Started + " ended=" + _d.Flight.Ended);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Rl1_Bombacik_KucukPatlamaDizisi_Dud_CizSesi()
        {
            if (!_d.RocketBlasts.Active)
            {
                Assert.Fail("v021 bombacık / dud kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 gogus = _s.Rig.transform.position + Vector3.up * 1.39f;
            void Firlat(Vector3 hedef, int bombacik)
            {
                var def = DD.Prototype.Core.Weapons.Arsenal.Get(DD.Prototype.Core.Config.ClassicWeaponId.Rl1);
                var shot = new DD.Prototype.Weapons.ShotRequest
                {
                    Origin = gogus, Direction = (hedef - gogus).normalized, Damage = def.fire.damage, Range = def.projectile.maxRange,
                    ImpactForce = def.projectile.impulse, Source = DD.Prototype.Damage.DamageSourceKind.Bullet,
                    Power = DD.Prototype.Feedback.WeaponPower.Heavy, PierceDamageFalloff = 1f
                };
                var launch = DD.Prototype.Weapons.ProjectileLaunch.From(def, DD.Prototype.Core.Config.ClassicWeaponId.Rl1, def.projectile.maxRange);
                launch.ClusterBomblets = bombacik;
                DD.Prototype.Weapons.ProjectileSystem.Spawn(shot, launch);
            }

            // Kurma mesafesi (4 m) içinde yere: patlama yok, düşüp cızlar.
            int duds = _d.RocketBlasts.Duds;
            Firlat(_s.Zemin(0f, 2.5f), 0);
            yield return SpecialWeaponSahne.Bekle(() => DD.Prototype.Weapons.RocketImpacts.Duds > 0 && _d.RocketBlasts.Duds > duds, 3f, "dud sesi yok");
            Assert.AreEqual(AudioSoundId.RocketDud, _d.RocketBlasts.LastSound);

            // Cluster 3: roketin patlaması mesafe katmanıyla bir kez, üç bombacık küçük pop dizisi (tam patlama yığını değil).
            int pops = _d.RocketBlasts.Bomblets;
            int detonations = DD.Prototype.Weapons.RocketImpacts.Detonations;
            Firlat(_s.Zemin(0f, 14f), 3);
            yield return SpecialWeaponSahne.Bekle(() => DD.Prototype.Weapons.RocketImpacts.Detonations > detonations, 4f, "roket patlamadı");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(pops + 3, _d.RocketBlasts.Bomblets, "üç bombacık, üç küçük pop");
            Debug.Log("[DD-TEST] wp072rl1 duds=" + _d.RocketBlasts.Duds + " pops=" + _d.RocketBlasts.Bomblets);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator YagmurSonrasi_Kuslar_SacakDamlasi_BambuKorusu_IslakAdim()
        {
            AfterRainAmbience ar = _d.Ambience.AfterRain;
            Assert.IsTrue(AmbienceDirector.WetLook && AfterRainAmbience.SunnyAfterRain, "DEC-102: ıslak görünüm + güneşli yağmur sonrası");
            if (!_d.Library.IsProduced(AudioSoundId.AmbBirdsDistant)) Assert.Fail("v022 ortam kimlikleri katalogda değil (AudioCatalogBuilder).");

            Vector3 kulak = _s.Rig.transform.position + Vector3.up * 1.6f;
            Vector3 koru = _s.Rig.transform.position + new Vector3(6f, 0f, 4f);
            AudioSurfaceHints.BambooGroves.Add(koru);
            int thunder = _d.Ambience.ThunderCount;
            try
            {
                int kus = ar.BirdCount, damla = ar.EaveDripCount, ruzgar = ar.GustCount, yaprak = ar.LeafDripCount;
                // A natural bird / drip just before may still hold the voice (MaxInstances, retrigger) — re-arm only the
                // layers that have not played yet, up to 5 s (gust → leaf drips 0.4–0.9 s later).
                float bitis = Time.realtimeSinceStartup + 5f;
                do
                {
                    ar.ForceDue(ar.BirdCount == kus, ar.EaveDripCount == damla, ar.GustCount == ruzgar);
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                while (Time.realtimeSinceStartup < bitis
                       && (ar.BirdCount == kus || ar.EaveDripCount == damla || ar.GustCount == ruzgar || ar.LeafDripCount == yaprak));

                Assert.Greater(ar.BirdCount, kus, "uzak kuşlar (BirdLevel " + AfterRainAmbience.BirdLevel + ")");
                Assert.Greater(ar.EaveDripCount, damla, "saçak damlası: " + ar.LastDrip);
                Assert.Greater(ar.GustCount, ruzgar, "bambu korusunda rüzgâr");
                Assert.Greater(ar.LeafDripCount, yaprak, "rüzgâr sonrası yaprak damlası");
                Assert.IsTrue(ar.GrovePlaying, "35 m içindeki koru yatağı çalar");
                Assert.AreEqual(thunder, _d.Ambience.ThunderCount, "güneşli yağmur sonrası: gök gürültüsü yok");

                // Akustik harita: salonda kuş sesi kısılır (Hall = çatı altı).
                System.Func<Vector3, AcousticSpace> onceki = AcousticSpaces.Resolver;
                float oncekiKus = AfterRainAmbience.BirdLevel;
                AcousticSpaces.Resolver = _ => AcousticSpace.Hall;
                try
                {
                    ar.ForceDue();
                    yield return null; yield return null;
                    Assert.AreEqual(AcousticSpace.Hall, ar.LastSpace);

                    // Tek ayar: BirdLevel = 0 → kuş yok (dinleme turu öncesi kapatma anahtarı).
                    AfterRainAmbience.BirdLevel = 0f;
                    int sessiz = ar.BirdCount;
                    ar.ForceDue();
                    yield return null; yield return null;
                    Assert.AreEqual(sessiz, ar.BirdCount, "BirdLevel 0 kuşları kapatır");
                }
                finally
                {
                    AcousticSpaces.Resolver = onceki;
                    AfterRainAmbience.BirdLevel = oncekiKus;
                }
            }
            finally
            {
                AudioSurfaceHints.BambooGroves.Remove(koru);
            }

            // Islak arduvaz: taşta oyuncu adımının altına ince su katmanı.
            var events = _s.Rig.GetComponentInChildren<DD.Prototype.Characters.CharacterAnimationEvents>(true);
            if (events != null && AyakBeklenen(_s.Rig.transform.position, out SurfaceKind altta) != AudioSoundId.None
                && (altta == SurfaceKind.Stone || altta == SurfaceKind.Default))
            {
                yield return new WaitForSecondsRealtime(0.2f);
                int islak = _d.Router.WetStepCount;
                events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.FootLeft);
                Assert.AreEqual(islak + 1, _d.Router.WetStepCount, "taşta ıslak adım katmanı");
            }

            Debug.Log("[DD-TEST] wp072dec102 birds=" + ar.BirdCount + " drips=" + ar.EaveDripCount + " gusts=" + ar.GustCount + " leaf=" + ar.LeafDripCount + " wet=" + _d.Router.WetStepCount + " kulak=" + kulak);
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator V6_VarsayilanDunya_YuzeyKoruSacakAkustik_SesYakalanir()
        {
            // C-00 25.09: World v5 (V6) is the default world — its surfaces, groves, eaves and acoustic spaces come from the world itself
            // (nothing injected here), and the after-rain layers are heard on the default path.
            GameObject world = GameObject.Find("[AP005 World]");
            if (world == null || world.transform.Find("Greybox v6") == null)
            {
                Assert.Ignore("Varsayılan dünya V6 değil (" + DD.Prototype.World.WorldSourceSettings.Resolve() + ").");
            }

            AfterRainAmbience ar = _d.Ambience.AfterRain;
            int tagged = world.GetComponentsInChildren<SurfaceTag>(true).Length;
            Assert.GreaterOrEqual(tagged, 2000, "V6 yüzey etiketleri (kit örnekleri + greybox)");
            Assert.GreaterOrEqual(AudioSurfaceHints.BambooGroves.Count, 1, "V6 bambu korusu (batı bandı)");
            Assert.GreaterOrEqual(AudioSurfaceHints.EavePoints.Count, 40, "V6 saçak noktaları (çatı kenarları)");
            Assert.AreEqual(SurfaceKind.Stone, YuzeyAltinda(new Vector3(0f, 0.05f, 30f)), "avlu arduvazı taş okunur");
            Assert.AreEqual(AcousticSpace.Hall, AcousticSpaces.At(new Vector3(0f, 2.5f, 44f)), "R1 ana salon akustik haritada");

            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "wp072-v6-ambience.wav");
            AudioOutputCapture cap = null;
            int damla = ar.EaveDripCount, ruzgar = ar.GustCount;
            try
            {
                mixer.SetFloat(AudioMixerBridge.VolumeParams[1], -80f);   // music out: the ambience alone
                cap = AudioOutputCapture.Arm(_d.gameObject, path);

                // The forecourt before the hall: drips from the hall's eave line.
                _s.Rig.Teleport(new Vector3(0f, 0.1f, 33f), Quaternion.identity);
                string eave = null;
                float bitis = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < bitis && eave == null)
                {
                    ar.ForceDue(false, true, false);
                    yield return new WaitForSecondsRealtime(0.4f);
                    if (ar.EaveDripCount > damla && ar.LastDrip.StartsWith("eave", StringComparison.Ordinal)) eave = ar.LastDrip;
                }

                Assert.IsNotNull(eave, "salon önünde saçak damlası: " + ar.LastDrip);

                // Beside the west grove band: the rustle bed and a gust.
                Vector3 koru = AudioSurfaceHints.BambooGroves[0];
                _s.Rig.Teleport(new Vector3(-34f, 0.1f, Mathf.Clamp(koru.z, -20f, 20f)), Quaternion.identity);
                bitis = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < bitis && (ar.GustCount == ruzgar || !ar.GrovePlaying))
                {
                    ar.ForceDue(false, false, ar.GustCount == ruzgar);
                    yield return new WaitForSecondsRealtime(0.4f);
                }

                Assert.Greater(ar.GustCount, ruzgar, "koru rüzgârı");
                Assert.IsTrue(ar.GrovePlaying, "koru yatağı (" + koru + ")");
                yield return new WaitForSecondsRealtime(1f);
                long frames = cap.FramesWritten;
                UnityEngine.Object.DestroyImmediate(cap);
                cap = null;
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");

                byte[] raw = System.IO.File.ReadAllBytes(path);
                int ch = raw[22] | (raw[23] << 8), rate = raw[24] | (raw[25] << 8) | (raw[26] << 16) | (raw[27] << 24);
                int n = (raw.Length - 44) / (2 * ch);
                var mono = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < ch; c++) { int o = 44 + 2 * (i * ch + c); sum += (short)(raw[o] | (raw[o + 1] << 8)) / 32768f; }
                    mono[i] = sum / ch;
                }

                float lufs = LoudnessMeter.Measure(mono, rate).IntegratedLufs;
                Assert.Greater(lufs, -60f, "varsayılan yolda ortam katmanları duyulur");
                Debug.Log("[DD-TEST] wp072.v6 tagged=" + tagged + " groves=" + AudioSurfaceHints.BambooGroves.Count + " eaves=" + AudioSurfaceHints.EavePoints.Count
                          + " ponds=" + AudioSurfaceHints.PondCenters.Count + " eave=" + eave + " gusts=" + ar.GustCount + " leaf=" + ar.LeafDripCount
                          + " capture " + (n / (float)rate).ToString("0.0") + " s " + lufs.ToString("0.0") + " LUFS-I (music out)");
            }
            finally
            {
                if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                mixer.ClearFloat(AudioMixerBridge.VolumeParams[1]);
            }
        }

        /// <summary>
        /// WP-072 · V6 mix pass (C-00 26.09): the default world, a scripted fight (6 zombies closing in, SMG1 bursts from the player),
        /// captured post-mixer per camera mode — the full mix and each bus solo (music / ambience / world SFX / player SFX).
        /// Measurement only (not a gate): Explicit, out of CI; the table goes to the log.
        /// </summary>
        [UnityTest]
        [Explicit("WP-072 V6 mix ölçümü — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(400000)]
        public IEnumerator MixV6_UcKameraModu_BusSeviyeleri_Olcum()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "wp072-mix.wav");
            _s.Rig.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                _s.Dogur(_s.Zemin(Mathf.Cos(a) * 9f, Mathf.Sin(a) * 9f));
            }

            var modlar = new[] { DD.Prototype.CameraSystem.CameraMode.TopDown, DD.Prototype.CameraSystem.CameraMode.ThirdPerson, DD.Prototype.CameraSystem.CameraMode.FirstPerson };
            var dallar = new[] { ("full", 0), ("music", 1), ("ambience", 2), ("world", 3), ("player", 4) };
            var rapor = new System.Text.StringBuilder("[DD-TEST] wp072.mix v6 (LUFS-I / PLR dB / LRA400 LU)");
            try
            {
                foreach (var mod in modlar)
                {
                    DD.Prototype.CameraSystem.CameraModeService.Request(mod, true);
                    yield return new WaitForSecondsRealtime(0.6f);
                    rapor.Append("\n  ").Append(mod).Append(':');
                    foreach (var (ad, bus) in dallar)
                    {
                        for (int k = 1; k <= 4; k++)
                        {
                            if (bus == 0 || k == bus) mixer.ClearFloat(AudioMixerBridge.VolumeParams[k]);
                            else mixer.SetFloat(AudioMixerBridge.VolumeParams[k], -80f);
                        }

                        AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, path);
                        float bitis = Time.realtimeSinceStartup + 4.8f, sonraki = 0f;
                        int atis = 0;
                        while (Time.realtimeSinceStartup < bitis)
                        {
                            float t = Time.realtimeSinceStartup;
                            // 1,2 s burst (SMG1 ~ 8 shots/s) then 0,8 s off — a player's rhythm, not a wall of fire.
                            if (t >= sonraki && (bitis - t) % 2f > 0.8f)
                            {
                                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", _s.Rig.transform.position + Vector3.up * 1.3f + _s.Rig.transform.forward * (0.1f * (atis % 2)), 0.75f, 50);
                                atis++;
                                sonraki = t + 0.125f;
                            }

                            yield return null;
                        }

                        long frames = cap.FramesWritten;
                        UnityEngine.Object.DestroyImmediate(cap);
                        foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                        if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                        float[] mono = OkuMono(path, out int rate);
                        LoudnessMeter.Result r = LoudnessMeter.Measure(mono, rate);
                        int win = (int)(0.4f * rate), hop = rate / 10;
                        var m = new System.Collections.Generic.List<float>();
                        var w = new float[win];
                        for (int s0 = 0; s0 + win <= mono.Length; s0 += hop) { Array.Copy(mono, s0, w, 0, win); float l = LoudnessMeter.WindowLoudness(w, rate); if (l > -70f) m.Add(l); }
                        m.Sort();
                        float lra = m.Count > 4 ? m[(int)(m.Count * 0.95f) - 1] - m[(int)(m.Count * 0.10f)] : 0f;
                        rapor.Append(' ').Append(ad).Append(' ').Append(r.Silent ? "sil" : r.IntegratedLufs.ToString("0.0")).Append(" / ").Append((r.TruePeakDb - r.IntegratedLufs).ToString("0.0")).Append(" / ").Append(lra.ToString("0.0")).Append(" ·");
                        if (bus == 0) Assert.IsFalse(r.Silent, mod + " tam miks sessiz");
                    }
                }
            }
            finally
            {
                for (int k = 1; k <= 4; k++) mixer.ClearFloat(AudioMixerBridge.VolumeParams[k]);
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, true);
            }

            Debug.Log(rapor.ToString());
        }

        /// <summary>
        /// WP-072 · user listening package (C-00 28.09): three A/B pairs from the game's real output on the default world (V6) —
        /// 1 top-down fight and 2 first-person fight — WP-072 B7 mix panel (C-00 01.10): one 60 s take per mode, 4 s blocks rotated
        /// A B C W R ×3 (A = today, B = SfxPlayer +3 / SfxWorld −1 dB = the Run snapshot proposal +1 / −3, C = SfxPlayer +2 and SfxWorld
        /// ducked −3 dB under player fire; W = the weapon alone, R = everything but the weapon — stems for the margin, since the SMG room
        /// tail rings through any "gap" window); block starts (capture frames) go to a CSV and wp-072/tools/kit_splice.py splices,
        /// level-matches and measures them. 3 the calm after-rain
        /// courtyard (A = birds 0.35 as shipped — near the floor at 25–40 m, B = the designed level 1.0). Written under temporaryCachePath/wp072-listening; the batch copies them.
        /// </summary>
        [UnityTest]
        [Explicit("WP-072 dinleme paketi — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator DinlemePaketi_AB_Uret()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            MusicDirector m = _d.Music;
            AfterRainAmbience ar = _d.Ambience.AfterRain;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp072-listening");
            System.IO.Directory.CreateDirectory(dir);
            string playerParam = AudioMixerBridge.VolumeParams[4];
            float oncekiKus = AfterRainAmbience.BirdLevel;
            _s.Rig.Health.DiagnosisInvulnerable = true;

            AudioOutputCapture aktifCap = null;
            IEnumerator Kaydet(string ad, float saniye, System.Action<float> her)
            {
                AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, System.IO.Path.Combine(dir, ad + ".wav"));
                aktifCap = cap;
                float bas = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - bas < saniye)
                {
                    her?.Invoke(Time.realtimeSinceStartup - bas);
                    yield return null;
                }

                long frames = cap.FramesWritten;
                UnityEngine.Object.DestroyImmediate(cap);
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
            }

            float sonraki = 0f;
            int atis = 0;
            void Ates(float t)
            {
                // 1,2 s burst (SMG1 ~8 shots/s), 0,8 s off.
                if (t >= sonraki && t % 2f < 1.2f)
                {
                    AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", _s.Rig.transform.position + Vector3.up * 1.3f + _s.Rig.transform.forward * (0.1f * (atis++ % 2)), 0.75f, 50);
                    sonraki = t + 0.125f;
                }
            }

            try
            {
                // 3 · calm courtyard after the rain: quiet music floor, three bird calls in 18 s (the game spaces them 14–30 s).
                m.ThreatLayerOverride = 0;
                m.IntensityOverride = 0.05f;
                yield return new WaitForSecondsRealtime(3f);
                foreach (var (ad, kus) in new[] { ("3A_avlu_kus-oyundaki", oncekiKus), ("3B_avlu_kus-tam", 1f) })
                {
                    AfterRainAmbience.BirdLevel = kus;
                    int cagri = 0;
                    int kusOnce = ar.BirdCount;
                    yield return Kaydet(ad, 18f, t =>
                    {
                        if (t > 1f + cagri * 6f) { ar.ForceDue(true, false, false); cagri++; }
                        foreach (AudioSource src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                        {
                            if (!src.isPlaying || src.clip == null || !src.clip.name.StartsWith("AmbBirdsDistant", StringComparison.Ordinal) || src.time > 0.05f) continue;
                            AudioListener l = UnityEngine.Object.FindObjectOfType<AudioListener>();
                            float d = l != null ? Vector3.Distance(l.transform.position, src.transform.position) : -1f;
                            float lin = src.rolloffMode == AudioRolloffMode.Linear ? Mathf.Clamp01(1f - (d - src.minDistance) / Mathf.Max(0.01f, src.maxDistance - src.minDistance)) : -1f;
                            float eff = src.volume * ((1f - src.spatialBlend) + src.spatialBlend * Mathf.Max(0f, lin));
                            mixer.GetFloat(AudioMixerBridge.VolumeParams[2], out float ambDb);
                            Debug.Log("[DD-TEST] wp072.bird " + ad + " t=" + t.ToString("0.0") + " vol=" + src.volume.ToString("0.000") + " blend=" + src.spatialBlend.ToString("0.00")
                                      + " d=" + d.ToString("0.0") + " rolloff=" + src.rolloffMode + " " + src.minDistance + "–" + src.maxDistance + " lin=" + lin.ToString("0.00")
                                      + " eff=" + (20f * Mathf.Log10(Mathf.Max(1e-6f, eff))).ToString("0.0") + " dB group=" + (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "none")
                                      + " ambBus=" + ambDb.ToString("0.0") + " dB space=" + ar.LastSpace + " phase fighting");
                        }
                    });
                    Debug.Log("[DD-TEST] wp072.bird " + ad + " level " + AfterRainAmbience.BirdLevel + " played " + (ar.BirdCount - kusOnce) + " · " + _d.WorldPool.Describe());
                }

                AfterRainAmbience.BirdLevel = oncekiKus;
                m.ThreatLayerOverride = null;
                m.IntensityOverride = null;

                // 1 / 2 · a fight: six zombies close in, the player fires bursts; A/B/C rotate every 4 s (see the summary).
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    _s.Dogur(_s.Zemin(Mathf.Cos(a) * 9f, Mathf.Sin(a) * 9f));
                }

                mixer.GetFloat(playerParam, out float tabanDb);
                string worldParam = AudioMixerBridge.VolumeParams[3];
                mixer.GetFloat(worldParam, out float tabanWorldDb);
                mixer.GetFloat(AudioMixerBridge.VolumeParams[1], out float tabanMusicDb);
                mixer.GetFloat(AudioMixerBridge.VolumeParams[2], out float tabanAmbDb);
                var modlar = new[] { (DD.Prototype.CameraSystem.CameraMode.TopDown, "1", "ustten"), (DD.Prototype.CameraSystem.CameraMode.FirstPerson, "2", "birinci-sahis") };
                foreach (var (mod, no, ad) in modlar)
                {
                    DD.Prototype.CameraSystem.CameraModeService.Request(mod, true);
                    yield return new WaitForSecondsRealtime(3f);
                    // The fire clock restarts per take (the FP take once inherited the top-down clock and barely fired). Blocks switch at a
                    // burst onset (t = 4k s); C's World duck: −3 dB, 30 ms down, held 150 ms after the last shot, 350 ms back.
                    sonraki = 0f;
                    atis = 0;
                    int blok = -1, sonAtis = 0;
                    float duck = 0f, sonAtisT = -10f, onceki = 0f;
                    var bloklar = new System.Text.StringBuilder("frame,variant\n");
                    yield return Kaydet(no + "_" + ad + "_take", 60f, t =>
                    {
                        int b = Mathf.Min(14, (int)(t / 4f));
                        if (b != blok)
                        {
                            blok = b;
                            bloklar.Append(aktifCap != null ? aktifCap.FramesWritten : 0).Append(',').Append("ABCWR"[b % 5]).Append('\n');
                        }

                        Ates(t);
                        if (atis != sonAtis) { sonAtis = atis; sonAtisT = t; }
                        char v = "ABCWR"[blok % 5];
                        float dt = Mathf.Max(0f, t - onceki);
                        onceki = t;
                        bool ateste = t - sonAtisT < 0.15f;
                        duck = v == 'C' ? (ateste ? Mathf.Max(-3f, duck - 3f * dt / 0.03f) : Mathf.Min(0f, duck + 3f * dt / 0.35f)) : 0f;
                        mixer.SetFloat(playerParam, v == 'R' ? -80f : tabanDb + (v == 'B' ? 3f : v == 'C' ? 2f : 0f));
                        mixer.SetFloat(worldParam, v == 'W' ? -80f : tabanWorldDb + (v == 'B' ? -1f : 0f) + duck);
                        mixer.SetFloat(AudioMixerBridge.VolumeParams[1], v == 'W' ? -80f : tabanMusicDb);   // music
                        mixer.SetFloat(AudioMixerBridge.VolumeParams[2], v == 'W' ? -80f : tabanAmbDb);     // ambience
                    });
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, no + "_" + ad + "_blocks.csv"), bloklar.ToString());
                    Debug.Log("[DD-TEST] wp072.weapon mode=" + mod + " shots=" + atis + " blocks=" + (blok + 1) + " last=" + _d.WeaponFire.LastVoice);
                }

                mixer.SetFloat(playerParam, tabanDb);
                mixer.SetFloat(worldParam, tabanWorldDb);
            }
            finally
            {
                foreach (int k in new[] { 1, 2, 3 }) mixer.ClearFloat(AudioMixerBridge.VolumeParams[k]);
                AfterRainAmbience.BirdLevel = oncekiKus;
                m.ThreatLayerOverride = null;
                m.IntensityOverride = null;
                mixer.ClearFloat(playerParam);
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, true);
            }

            Debug.Log("[DD-TEST] wp072.listening " + string.Join(" ", System.IO.Directory.GetFiles(dir, "*.wav")));
        }

        /// <summary>
        /// WP-085 B4 · in-game real-RPM weapon kit (C-00 05.10; event plan after AP-027 Faz A AP027-A-ab-kiti-tasarimi §2):
        /// per weapon (DD_KIT_WEAPONS, default ar_1,sg_1; "all" = the enabled roster) × camera mode (top-down, FP) × variant
        /// (DD_KIT_VARIANTS, default AB: A = today's game, B = the pre-v3 voices via WeaponFireAudio.LegacyVoicesForKit):
        /// 0.5 s courtyard + crowd, three single trigger pulls, 1 s, a held string at the weapon's own WD rate (Auto: 1.6 s;
        /// Burst: pulls for 1.6 s; Single: four cycles), 2.1 s of tail, 1 s calm. Fire cues at the WD timing on the equipped
        /// weapon (so casings speak its calibre) and its casing event (sg_1 at the pump, 0.4 s; sg_2 none at the shot).
        /// Each take twice: the full mix (Kit_*) and the weapon bus alone (Stem_*, every other bus at −80 dB). Shot capture frames
        /// go to a CSV. Variant A's stems give R2 S1 (below-400 Hz share of each held shot 2–10, first 150 ms after its onset)
        /// and S2 (full voice per shot, no transient / loop voice, no pool refusal) — r2-S1 / r2-S2 when DD_R2_MEASURE_DIR is set.
        /// Written under temporaryCachePath/wp085-kit; the batch copies them.
        /// </summary>
        [UnityTest]
        [Explicit("WP-085 B4 gerçek RPM silah kiti — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(1800000)]
        public IEnumerator SilahKiti_GercekRpm_OyunIci_Kayit()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp085-kit");
            System.IO.Directory.CreateDirectory(dir);
            string istek = Environment.GetEnvironmentVariable("DD_KIT_WEAPONS");
            if (string.IsNullOrEmpty(istek)) istek = "ar_1,sg_1";
            string varyantlar = Environment.GetEnvironmentVariable("DD_KIT_VARIANTS");
            if (string.IsNullOrEmpty(varyantlar)) varyantlar = "ABC";
            string ap027 = Environment.GetEnvironmentVariable("DD_AP027_DIR");
            // The KADRO export, relative to home exactly as tools/dd_paths.py KADRO_REL ("Desktop/KADRO"); the R2 chain passes
            // DD_AP027_DIR. C asked for and its folder missing is a red test, never a silent skip (S-10 05.10).
            if (string.IsNullOrEmpty(ap027)) ap027 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/exports/v001-prototip");
            if (varyantlar.IndexOf('C') >= 0) Assert.IsTrue(System.IO.Directory.Exists(ap027), "Kit C istendi ama AP-027 klasörü yok: " + ap027 + " (DD_AP027_DIR)");
            var kitKliplar = new List<AudioClip>();
            _s.Rig.Health.DiagnosisInvulnerable = true;
            DD.Prototype.Core.Config.ClassicWeaponId oncekiSecim = DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon;

            var silahlar = new List<(DD.Prototype.Core.Config.ClassicWeaponId id, DD.Prototype.Core.Weapons.WeaponDefinition wd)>();
            foreach (DD.Prototype.Core.Weapons.ClassicWeaponDefinition d in DD.Prototype.Core.Weapons.WeaponRoster.Enabled)
            {
                DD.Prototype.Core.Weapons.WeaponDefinition wd = DD.Prototype.Core.Weapons.Arsenal.Get(d.Id);
                if (wd == null) continue;
                if (istek == "all" || Array.IndexOf(istek.Split(','), wd.weaponId) >= 0) silahlar.Add((d.Id, wd));
            }

            Assert.IsNotEmpty(silahlar, "Kit silahı bulunamadı: " + istek);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                _s.Dogur(_s.Zemin(Mathf.Cos(a) * 14f, Mathf.Sin(a) * 14f));
            }

            string[] buslar = AudioMixerBridge.VolumeParams;
            const int PlayerBus = 4;   // the stem mutes every other bus and never touches Master / SFX-Player: the snapshot owns them
            var basta = new System.Text.StringBuilder("[DD-TEST] wp085.kit buses at start:");
            foreach (string b in buslar) { mixer.GetFloat(b, out float db); basta.Append(' ').Append(b).Append('=').Append(db.ToString("0.0")); }
            Debug.Log(basta.ToString());

            var csv = new System.Text.StringBuilder("take,weapon,mode,variant,stem,kind,shot,frame,sampleRate\n");
            var ozet = new System.Text.StringBuilder("[");
            float s1Min = 1f, s1MinC = 1f;
            int s1Shots = 0, s2Deficit = 0, s2Transients = 0, s2Loops = 0, s2Refused = 0, s2Shots = 0;
            var s1Detay = new List<string>();

            AudioOutputCapture cap = null;
            try
            {
                foreach (var (cid, wd) in silahlar)
                {
                    _s.Controller.EquipClassic(cid, _s.Config);
                    yield return new WaitForSecondsRealtime(1f);
                    string audioId = wd.AudioId;
                    var mode = wd.fire.fireMode;
                    float period = 60f / Mathf.Max(1f, wd.fire.rpm);
                    float burstGap = wd.fire.burstIntervalSeconds;
                    int burstN = Mathf.Max(1, wd.fire.burstCount);
                    float intensity = audioId.StartsWith("SMG", StringComparison.Ordinal) ? 0.75f : audioId.StartsWith("SG", StringComparison.Ordinal) ? 1.5f : 1f;
                    float casingDelay = audioId == "SG1" ? 0.4f : -1f;   // pump: at the pump-back (WeaponMechanismTimeline.Shotgun)
                    if (audioId != "SG1" && audioId != "SG2") casingDelay = 0f;   // magazine weapons eject at the shot; sg_2 break-open at reload only

                    // The event plan (seconds from take start): kind 's' single pull, 'h' held string.
                    var plan = new List<(float t, char kind)>();
                    float gap = mode == DD.Prototype.Core.Weapons.WeaponFireMode.Single ? Mathf.Max(1.8f, period) : 1.2f;
                    float t0 = 0.5f;
                    void Pull(float at, char kind)
                    {
                        if (mode == DD.Prototype.Core.Weapons.WeaponFireMode.Burst) for (int b = 0; b < burstN; b++) plan.Add((at + b * burstGap, kind));
                        else plan.Add((at, kind));
                    }

                    for (int i = 0; i < 3; i++) Pull(t0 + i * gap, 's');
                    float held = t0 + 3 * gap + 1f;
                    float heldEnd;
                    if (mode == DD.Prototype.Core.Weapons.WeaponFireMode.Auto)
                    {
                        int n = Mathf.FloorToInt(1.6f / period);
                        for (int i = 0; i < n; i++) plan.Add((held + i * period, 'h'));
                        heldEnd = held + (n - 1) * period;
                    }
                    else if (mode == DD.Prototype.Core.Weapons.WeaponFireMode.Burst)
                    {
                        float cycle = (burstN - 1) * burstGap + wd.fire.burstDelaySeconds;
                        float at = held;
                        while (at <= held + 1.6f) { Pull(at, 'h'); at += cycle; }
                        heldEnd = at - cycle + (burstN - 1) * burstGap;
                    }
                    else
                    {
                        for (int i = 0; i < 4; i++) plan.Add((held + i * period, 'h'));
                        heldEnd = held + 3 * period;
                    }

                    float takeLen = heldEnd + 2.1f + 1f;

                    foreach (var (camMode, modAd) in new[] { (DD.Prototype.CameraSystem.CameraMode.TopDown, "TopDown"), (DD.Prototype.CameraSystem.CameraMode.FirstPerson, "FP") })
                    {
                        DD.Prototype.CameraSystem.CameraModeService.Request(camMode, true);
                        yield return new WaitForSecondsRealtime(2f);
                        foreach (char v in varyantlar)
                        {
                            // C = the AP-027 v4 prototype (kit-only override of this weapon's Fire / own open tail / far voice / casings).
                            if (v == 'C' && !KitCYukle(wd.weaponId, audioId, ap027, kitKliplar)) continue;
                            WeaponFireAudio.LegacyVoicesForKit = v == 'B';
                            try
                            {
                            foreach (bool stem in new[] { false, true })
                            {
                                // A full-mix take touches no bus. (The first kit wrote a start-of-test baseline back each take — SFX-Player
                                // read −80 dB mid snapshot transition — and the weapon was missing from every take, C-00 05.10.)
                                for (int i = 0; i < buslar.Length; i++)
                                {
                                    if (i == PlayerBus || i == 0) continue;
                                    if (stem) mixer.SetFloat(buslar[i], -80f);
                                    else mixer.ClearFloat(buslar[i]);
                                }
                                string ad = (stem ? "Stem_" : "Kit_") + wd.weaponId + "_" + modAd + "_" + v;
                                yield return new WaitForSecondsRealtime(0.5f);
                                WeaponFireAudio f = _d.WeaponFire;
                                var takeCsv = new System.Text.StringBuilder();
                                var heldFrames = new List<long>();
                                int gecici = 0, dongu = 0, voices = 0, refused = 0;
                                string yigilma = null;
                                // A frame longer than the shot period fires the catch-up cues together — a double shot in the
                                // recording (C-00 05.10, kit-v2 ar_1 top-down B / C): the take is re-recorded, at most three times.
                                for (int deneme = 1; deneme <= 3; deneme++)
                                {
                                    takeCsv.Clear();
                                    heldFrames.Clear();
                                    int ses0 = f.FireVoices, red0 = f.FireRefused;
                                    cap = AudioOutputCapture.Arm(_d.gameObject, System.IO.Path.Combine(dir, ad + ".wav"));
                                    var kovanlar = new List<float>();
                                    int sira = 0;
                                    float bas = Time.realtimeSinceStartup;
                                    while (Time.realtimeSinceStartup - bas < takeLen)
                                    {
                                        float t = Time.realtimeSinceStartup - bas;
                                        while (sira < plan.Count && plan[sira].t <= t)
                                        {
                                            Vector3 at = _s.Rig.transform.position + Vector3.up * 1.3f + _s.Rig.transform.forward * 0.3f;
                                            AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, audioId, at, intensity, 50);
                                            if (casingDelay >= 0f) kovanlar.Add(t + casingDelay);
                                            long fr = cap.FramesCaptured;   // the audio thread's position, not the drained file length
                                            takeCsv.Append(ad).Append(',').Append(wd.weaponId).Append(',').Append(modAd).Append(',').Append(v).Append(',').Append(stem ? 1 : 0)
                                               .Append(',').Append(plan[sira].kind).Append(',').Append(sira).Append(',').Append(fr).Append(',').Append(AudioSettings.outputSampleRate).Append('\n');
                                            if (plan[sira].kind == 'h') heldFrames.Add(fr);
                                            sira++;
                                        }

                                        for (int k = kovanlar.Count - 1; k >= 0; k--)
                                        {
                                            if (kovanlar[k] > t) continue;
                                            kovanlar.RemoveAt(k);
                                            Transform port = _s.Rig.transform;
                                            DD.Prototype.Weapons.WeaponEvents.PublishMechanism(new DD.Prototype.Weapons.WeaponMechanismEvent(
                                                DD.Prototype.Weapons.WeaponMechanismKind.CasingEjected, cid, port.position + Vector3.up * 1.3f + port.right * 0.2f, port.right, false));
                                        }

                                        yield return null;   // no per-frame scene scan: it lengthened frames (S2 counts come from the take's telemetry)
                                    }

                                    long frames = cap.FramesWritten;
                                    UnityEngine.Object.DestroyImmediate(cap);
                                    cap = null;
                                    foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                                    if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                                    voices = f.FireVoices - ses0;
                                    refused = f.FireRefused - red0;
                                    TelemetriKatman(System.IO.Path.Combine(dir, ad + ".wav.telemetry.tsv"), out gecici, out dongu);
                                    yigilma = Yigilma(heldFrames, plan, AudioSettings.outputSampleRate);
                                    if (yigilma == null) break;
                                    Debug.Log("[DD-TEST] wp085.kit retake " + ad + " #" + deneme + ": " + yigilma);
                                    yield return new WaitForSecondsRealtime(1f);
                                }

                                Assert.IsNull(yigilma, ad + ": basılı atışlar yığıldı (3 denemede) — " + yigilma);
                                csv.Append(takeCsv);
                                if (stem)
                                {
                                    float duyulur = SilahTepesiDb(System.IO.Path.Combine(dir, ad + ".wav"), heldFrames.Count > 0 ? heldFrames : new List<long>());
                                    Assert.GreaterOrEqual(duyulur, -30f, ad + ": silah sesi kayıtta yok (atış penceresi tepe medyanı " + duyulur.ToString("0.0") + " dBFS)");
                                }

                                ozet.Append(ozet.Length > 1 ? "," : "").Append("{\"take\":\"").Append(ad).Append("\",\"weapon\":\"").Append(wd.weaponId)
                                    .Append("\",\"audioId\":\"").Append(audioId).Append("\",\"mode\":\"").Append(modAd).Append("\",\"variant\":\"").Append(v)
                                    .Append("\",\"stem\":").Append(stem ? "true" : "false").Append(",\"rpm\":").Append(wd.fire.rpm.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                    .Append(",\"fireMode\":\"").Append(mode).Append("\",\"shots\":").Append(plan.Count).Append(",\"fireVoices\":").Append(voices)
                                    .Append(",\"fireRefused\":").Append(refused).Append(",\"transients\":").Append(gecici).Append(",\"loopStarts\":").Append(dongu)
                                    .Append(",\"casingsLanded\":").Append(_d.Casings.Landed).Append(",\"lastCasing\":\"").Append(_d.Casings.LastSound).Append("\"}");

                                if (v == 'C' && stem)
                                {
                                    float payC = AltBantPayiMin(System.IO.Path.Combine(dir, ad + ".wav"), heldFrames, out int olculenC);
                                    if (olculenC > 0) s1MinC = Mathf.Min(s1MinC, payC);
                                    s1Detay.Add(ad + " below400Min=" + payC.ToString("0.000") + " shots=" + olculenC);
                                }

                                if (v == 'A' && stem)
                                {
                                    s2Shots += plan.Count;
                                    s2Deficit += plan.Count - voices;
                                    s2Transients += gecici;
                                    s2Loops += dongu;
                                    s2Refused += refused;
                                    float pay = AltBantPayiMin(System.IO.Path.Combine(dir, ad + ".wav"), heldFrames, out int olculen);
                                    if (olculen > 0) { s1Min = Mathf.Min(s1Min, pay); s1Shots += olculen; }
                                    s1Detay.Add(ad + " below400Min=" + pay.ToString("0.000") + " shots=" + olculen);
                                }
                            }
                            }
                            finally
                            {
                                _d.Library.ClearKitOverrides();
                            }
                        }
                    }
                }
            }
            finally
            {
                // C-00 05.10 (main-ci 285b7fe8 — four CoreCombat reds: the next run spawned with sg_1): EquipClassic writes the
                // process-wide WeaponRegistry.SelectedWeapon; give it back and re-arm the live controller (CoreCombat T1 pattern).
                DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon = oncekiSecim;
                if (_s.Controller != null) _s.Controller.EquipClassic(oncekiSecim, _s.Config);
                WeaponFireAudio.LegacyVoicesForKit = false;
                _d.Library.ClearKitOverrides();
                foreach (AudioClip k in kitKliplar) if (k != null) UnityEngine.Object.Destroy(k);
                if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
                for (int i = 0; i < buslar.Length; i++) if (i != PlayerBus && i != 0) mixer.ClearFloat(buslar[i]);
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, true);
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "kit-events.csv"), csv.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "kit-summary.json"), ozet.Append(']').ToString());
            Debug.Log("[DD-TEST] wp085.kit " + string.Join(" · ", s1Detay) + " · S2 shots=" + s2Shots + " deficit=" + s2Deficit + " transients=" + s2Transients
                      + " loops=" + s2Loops + " refused=" + s2Refused + (varyantlar.IndexOf('C') >= 0 ? " · C below400Min=" + s1MinC.ToString("0.000") : "") + " · files " + System.IO.Directory.GetFiles(dir, "*.wav").Length);

            const string kaynak = "DD.Prototype.Tests.PlayMode.AudioSliceTests.SilahKiti_GercekRpm_OyunIci_Kayit";
            if (varyantlar.IndexOf('A') >= 0)
            {
                var s1 = new Dictionary<string, double> { { "below400ShareMin", Math.Round(s1Min, 4) } };
                if (varyantlar.IndexOf('C') >= 0 && s1MinC < 1f) s1["below400ShareMinV4"] = Math.Round(s1MinC, 4);   // report (S-10 2ccda99b)
                R2Olcum.Yaz("S1", kaynak, s1, s1Shots);
                R2Olcum.Yaz("S2", kaynak, new Dictionary<string, double>
                {
                    { "fireVoiceDeficit", s2Deficit }, { "transients", s2Transients }, { "loopStarts", s2Loops }, { "rejectedNotStolen", s2Refused }
                }, s2Shots);
                Assert.AreEqual(0, s2Deficit + s2Transients + s2Loops + s2Refused, "R2 S2: her atış tam katman, hafif katman / döngü / havuz reddi yok.");
                Assert.GreaterOrEqual(s1Min, 0.2f, "R2 S1: basılı tutmada 2.–10. atış, ilk 150 ms, 400 Hz altı pay ≥ %20 (" + string.Join(" · ", s1Detay) + ")");
            }
        }

        /// <summary>
        /// WP-085 · R2 S4 room tails: per weapon (DD_EDC_WEAPONS, default every audioId) × space (Open / Corridor / Hall, forced via
        /// AcousticSpaces.Resolver) four single cues 2.5 s apart on the weapon bus alone (every other bus at −80 dB), captured
        /// post-mixer. Per shot: Schroeder backward-integrated energy decay from the onset (first sample within 100 ms of the cue
        /// frame above 10 % of the window peak) over the next 2.3 s; the time it first falls 30 dB (direct, no extrapolation).
        /// Median of the four per weapon × space. r2-S4: openMsMin / corridorMsMin / hallMsMin (minimum over weapons), hallMsMax,
        /// orderViolations (weapons where open + 100 ms ≤ corridor and corridor + 100 ms ≤ hall does not hold).
        /// What plays is what the game plays: an automatic's single cue is a string of one, its tail rings at the release.
        /// Needs a quiet machine (the frame clock places the cues). Written under temporaryCachePath/wp085-edc.
        /// </summary>
        [UnityTest]
        [Explicit("WP-085 R2 S4 mekân kuyruğu ölçümü — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(1200000)]
        public IEnumerator MekanKuyrugu_EDC_Olcum()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp085-edc");
            System.IO.Directory.CreateDirectory(dir);
            string istek = Environment.GetEnvironmentVariable("DD_EDC_WEAPONS");
            var silahlar = new List<string>();
            foreach (WeaponVoiceSet set in WeaponAudioIdentity.All)
            {
                if (string.IsNullOrEmpty(istek) || Array.IndexOf(istek.Split(','), set.AudioId) >= 0) silahlar.Add(set.AudioId);
            }

            _s.Rig.Health.DiagnosisInvulnerable = true;
            string[] buslar = AudioMixerBridge.VolumeParams;
            const int PlayerBus = 4;   // the stem mutes every other bus and never touches Master / SFX-Player: the snapshot owns them
            var mekanlar = new[] { AcousticSpace.Open, AcousticSpace.Corridor, AcousticSpace.Hall };
            var sonuc = new Dictionary<string, float[]>();
            var csv = new System.Text.StringBuilder("weapon,space,shot,edcMs\n");
            System.Func<Vector3, AcousticSpace> onceki = AcousticSpaces.Resolver;
            AudioOutputCapture cap = null;
            try
            {
                for (int i = 0; i < buslar.Length; i++) if (i != PlayerBus && i != 0) mixer.SetFloat(buslar[i], -80f);
                foreach (string id in silahlar)
                {
                    var med = new float[3];
                    for (int m = 0; m < 3; m++)
                    {
                        AcousticSpace mekan = mekanlar[m];
                        AcousticSpaces.Resolver = _ => mekan;
                        yield return new WaitForSecondsRealtime(0.5f);
                        string ad = "Edc_" + id + "_" + mekan;
                        cap = AudioOutputCapture.Arm(_d.gameObject, System.IO.Path.Combine(dir, ad + ".wav"));
                        var kareler = new List<long>();
                        float bas = Time.realtimeSinceStartup;
                        int atis = 0;
                        while (Time.realtimeSinceStartup - bas < 4 * 2.5f + 0.5f)
                        {
                            if (atis < 4 && Time.realtimeSinceStartup - bas >= 0.3f + atis * 2.5f)
                            {
                                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, id, _s.Rig.transform.position + Vector3.up * 1.3f + Vector3.right * (2f * atis), 1f, 50);
                                kareler.Add(cap.FramesCaptured);
                                atis++;
                            }

                            yield return null;
                        }

                        UnityEngine.Object.DestroyImmediate(cap);
                        cap = null;
                        foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                        float duyulur = SilahTepesiDb(System.IO.Path.Combine(dir, ad + ".wav"), kareler);
                        Assert.GreaterOrEqual(duyulur, -30f, ad + ": silah sesi kayıtta yok (" + duyulur.ToString("0.0") + " dBFS)");
                        float[] edc = EdcMs(System.IO.Path.Combine(dir, ad + ".wav"), kareler, 2.3f);
                        for (int k = 0; k < edc.Length; k++) csv.Append(id).Append(',').Append(mekan).Append(',').Append(k).Append(',').Append(edc[k].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                        Array.Sort(edc);
                        med[m] = edc.Length == 0 ? 0f : edc.Length % 2 == 1 ? edc[edc.Length / 2] : 0.5f * (edc[edc.Length / 2 - 1] + edc[edc.Length / 2]);
                    }

                    sonuc[id] = med;
                }
            }
            finally
            {
                AcousticSpaces.Resolver = onceki;
                if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
                for (int i = 0; i < buslar.Length; i++) if (i != PlayerBus && i != 0) mixer.ClearFloat(buslar[i]);
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "edc.csv"), csv.ToString());
            float openMin = float.MaxValue, corrMin = float.MaxValue, hallMin = float.MaxValue, hallMax = 0f;
            int sira = 0;
            var detay = new List<string>();
            foreach (var kv in sonuc)
            {
                float o = kv.Value[0], c = kv.Value[1], h = kv.Value[2];
                openMin = Mathf.Min(openMin, o);
                corrMin = Mathf.Min(corrMin, c);
                hallMin = Mathf.Min(hallMin, h);
                hallMax = Mathf.Max(hallMax, h);
                if (!(o + 100f <= c && c + 100f <= h)) sira++;
                detay.Add(kv.Key + " " + o.ToString("0") + "/" + c.ToString("0") + "/" + h.ToString("0"));
            }

            Debug.Log("[DD-TEST] wp085.s4 open/corridor/hall ms (median of 4): " + string.Join(" · ", detay) + " · orderViolations " + sira);
            R2Olcum.Yaz("S4", "DD.Prototype.Tests.PlayMode.AudioSliceTests.MekanKuyrugu_EDC_Olcum", new Dictionary<string, double>
            {
                { "openMsMin", Math.Round(openMin, 1) }, { "corridorMsMin", Math.Round(corrMin, 1) }, { "hallMsMin", Math.Round(hallMin, 1) },
                { "hallMsMax", Math.Round(hallMax, 1) }, { "orderViolations", sira }
            }, sonuc.Count * 12);
            Assert.IsNotEmpty(sonuc, "Ölçülen silah yok.");
            Assert.GreaterOrEqual(openMin, 500f, "R2 S4 açık ≥ 500 ms (" + string.Join(" · ", detay) + ")");
            Assert.GreaterOrEqual(corrMin, 750f, "R2 S4 koridor ≥ 750 ms");
            Assert.GreaterOrEqual(hallMin, 1100f, "R2 S4 salon ≥ 1100 ms");
            Assert.LessOrEqual(hallMax, 2000f, "R2 S4 salon ≤ 2000 ms");
            Assert.AreEqual(0, sira, "R2 S4 sıra açık < koridor < salon (≥ 100 ms adım)");
        }

        /// <summary>
        /// Held cues bunched in the recording: two at one audio-thread frame, or closer than half the plan's shortest held
        /// interval (a long game frame fired the catch-up cues together). Null when the take is clean.
        /// </summary>
        private static string Yigilma(List<long> frames, List<(float t, char kind)> plan, int rate)
        {
            float minGap = float.MaxValue;
            float onceki = -1f;
            foreach (var (t, kind) in plan)
            {
                if (kind != 'h') continue;
                if (onceki >= 0f) minGap = Mathf.Min(minGap, t - onceki);
                onceki = t;
            }

            if (minGap == float.MaxValue) return null;
            long esik = (long)(0.5f * minGap * rate);
            for (int i = 1; i < frames.Count; i++)
            {
                long d = frames[i] - frames[i - 1];
                if (d < esik) return "atış " + (i + 1) + " öncekinden " + (1000.0 * d / rate).ToString("0") + " ms sonra (en az " + (1000.0 * esik / rate).ToString("0") + " ms)";
            }

            return null;
        }

        /// <summary>Weapon transient / loop voices started in a take (the capture's telemetry table, column "started").</summary>
        private static void TelemetriKatman(string tsv, out int transients, out int loops)
        {
            transients = 0;
            loops = 0;
            if (!System.IO.File.Exists(tsv)) return;
            bool tablo = false;
            foreach (string satir in System.IO.File.ReadAllLines(tsv))
            {
                if (satir.StartsWith("# sound", StringComparison.Ordinal)) { tablo = true; continue; }
                if (!tablo || satir.StartsWith("#", StringComparison.Ordinal)) { if (tablo && satir.StartsWith("#", StringComparison.Ordinal)) tablo = false; continue; }
                string[] c = satir.Split('\t');
                if (c.Length < 4 || !c[0].StartsWith("Weapon", StringComparison.Ordinal) || !int.TryParse(c[3], out int started)) continue;
                if (c[0].Contains("Transient")) transients += started;
                else if (c[0].Contains("Loop")) loops += started;
            }
        }

        /// <summary>Median over cues of the capture's peak in the 150 ms after each cue frame (dBFS); −120 with no cue.</summary>
        private static float SilahTepesiDb(string wav, List<long> frames)
        {
            float[] x = WavMono(wav, out int rate);
            var p = new List<float>();
            foreach (long f in frames)
            {
                int a = (int)f, b = Math.Min(x.Length, a + (int)(0.15f * rate));
                float pk = 0f;
                for (int i = a; i < b; i++) pk = Mathf.Max(pk, Mathf.Abs(x[i]));
                p.Add(20f * Mathf.Log10(Mathf.Max(1e-6f, pk)));
            }

            if (p.Count == 0) return -120f;
            p.Sort();
            return p[p.Count / 2];
        }

        /// <summary>Per cue: ms from the onset until the Schroeder EDC of the next <paramref name="window"/> s first reaches −30 dB.</summary>
        private static float[] EdcMs(string wav, List<long> frames, float window)
        {
            float[] x = WavMono(wav, out int rate);
            var r = new List<float>();
            foreach (long f in frames)
            {
                int f0 = (int)f, search = rate / 10, n = (int)(window * rate);
                if (f0 + search + n >= x.Length) continue;
                float peak = 0f;
                for (int i = f0; i < f0 + search + n; i++) peak = Mathf.Max(peak, Mathf.Abs(x[i]));
                if (peak <= 0f) continue;
                int on = f0;
                while (on < f0 + search && Mathf.Abs(x[on]) < 0.1f * peak) on++;
                double total = 0;
                for (int i = on; i < on + n; i++) total += (double)x[i] * x[i];
                if (total <= 0) continue;
                double rest = total, limit = total * 1e-3;   // −30 dB of the integral
                int t = on;
                while (t < on + n && rest > limit) { rest -= (double)x[t] * x[t]; t++; }
                r.Add(1000f * (t - on) / rate);
            }

            return r.ToArray();
        }

        /// <summary>16-bit PCM capture (AudioOutputCapture) → mono floats.</summary>
        private static float[] WavMono(string wav, out int rate)
        {
            byte[] b = System.IO.File.ReadAllBytes(wav);
            int ch = BitConverter.ToInt16(b, 22);
            rate = BitConverter.ToInt32(b, 24);
            int pos = 12;
            while (pos + 8 <= b.Length && !(b[pos] == 'd' && b[pos + 1] == 'a' && b[pos + 2] == 't' && b[pos + 3] == 'a')) pos += 8 + BitConverter.ToInt32(b, pos + 4);
            int dataStart = pos + 8, n = (b.Length - dataStart) / (2 * ch);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int c = 0; c < ch; c++) s += BitConverter.ToInt16(b, dataStart + (i * ch + c) * 2) / 32768f;
                x[i] = s / ch;
            }

            return x;
        }

        /// <summary>
        /// R2 S1 on a stem capture: for held shots 2–10, the first 150 ms after the onset (first sample within 100 ms of the cue frame
        /// above 10 % of that window's peak), share of 30–400 Hz energy in 30 Hz–16 kHz energy (4th-order Butterworth biquads, mono sum).
        /// </summary>
        private static float AltBantPayiMin(string wav, List<long> heldFrames, out int measured)
        {
            measured = 0;
            byte[] b = System.IO.File.ReadAllBytes(wav);
            int ch = BitConverter.ToInt16(b, 22), rate = BitConverter.ToInt32(b, 24);
            int pos = 12;
            while (pos + 8 <= b.Length && !(b[pos] == 'd' && b[pos + 1] == 'a' && b[pos + 2] == 't' && b[pos + 3] == 'a')) pos += 8 + BitConverter.ToInt32(b, pos + 4);
            int dataStart = pos + 8, n = (b.Length - dataStart) / (2 * ch);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int c = 0; c < ch; c++) s += BitConverter.ToInt16(b, dataStart + (i * ch + c) * 2) / 32768f;
                x[i] = s / ch;
            }

            float[] tot = Biquad(Biquad(Biquad(Biquad(x, rate, 30f, 0.5412f, true), rate, 30f, 1.3066f, true), rate, 16000f, 0.5412f, false), rate, 16000f, 1.3066f, false);
            float[] low = Biquad(Biquad(tot, rate, 400f, 0.5412f, false), rate, 400f, 1.3066f, false);
            float min = 1f;
            for (int k = 1; k < Mathf.Min(10, heldFrames.Count); k++)
            {
                int f0 = (int)heldFrames[k], search = rate / 10, win = (int)(0.15f * rate);
                if (f0 + search + win >= n) break;
                float peak = 0f;
                for (int i = f0; i < f0 + search + win; i++) peak = Mathf.Max(peak, Mathf.Abs(x[i]));
                int on = f0;
                while (on < f0 + search && Mathf.Abs(x[on]) < 0.1f * peak) on++;
                double el = 0, et = 0;
                for (int i = on; i < on + win; i++) { el += low[i] * low[i]; et += tot[i] * tot[i]; }
                if (et <= 0) continue;
                min = Mathf.Min(min, (float)(el / et));
                measured++;
            }

            return measured > 0 ? min : 0f;
        }

        /// <summary>
        /// WP-085 B5 kit C: the AP-027 Faz A prototype for this weapon (Weapon_ar1_Fire_01.wav …) as kit-only library overrides —
        /// Fire (the authoritative pre-mix; Mechanical is inside it and is not added again), the weapon's own TailOpen (played with
        /// the open-court reflection offset), DistanceFar, and the stone / wood casings of its calibre. False when the export has
        /// no Fire for this weapon (the kit skips C for it).
        /// </summary>
        private bool KitCYukle(string weaponId, string audioId, string dir, List<AudioClip> sahip)
        {
            string k = weaponId.Replace("_", "");
            AudioClip[] fire = WavKlipleri(dir, "Weapon_" + k + "_Fire_", sahip);
            if (fire.Length == 0) return false;
            WeaponAudioIdentity.TryGet(audioId, out WeaponVoiceSet set);
            _d.Library.OverrideForKit(set.Fire, fire);
            _d.Library.OverrideForKit(WeaponAudioIdentity.Layer(audioId, WeaponLayer.TailOpen), WavKlipleri(dir, "Weapon_" + k + "_TailOpen_", sahip));
            _d.Library.OverrideForKit(WeaponAudioIdentity.Layer(audioId, WeaponLayer.DistanceFar), WavKlipleri(dir, "Weapon_" + k + "_DistanceFar_", sahip));
            CasingFamily aile = WeaponFoleyV3.Casing(audioId);
            _d.Library.OverrideForKit(WeaponFoleyV3.CasingOn(aile, SurfaceKind.Stone), WavKlipleri(dir, "Casing_" + k + "_StoneSlate_", sahip));
            _d.Library.OverrideForKit(WeaponFoleyV3.CasingOn(aile, SurfaceKind.Wood), WavKlipleri(dir, "Casing_" + k + "_Wood_", sahip));
            return true;
        }

        private static AudioClip[] WavKlipleri(string dir, string onEk, List<AudioClip> sahip)
        {
            if (!System.IO.Directory.Exists(dir)) return new AudioClip[0];
            string[] yollar = System.IO.Directory.GetFiles(dir, onEk + "*.wav");
            Array.Sort(yollar, StringComparer.Ordinal);
            var kl = new List<AudioClip>();
            foreach (string y in yollar)
            {
                AudioClip c = WavOku(y);
                if (c == null) continue;
                kl.Add(c);
                sahip.Add(c);
            }

            return kl.ToArray();
        }

        /// <summary>PCM 16 / 24-bit WAV → AudioClip (the AP-027 exports are 48 kHz PCM24, mono or stereo).</summary>
        private static AudioClip WavOku(string path)
        {
            byte[] b = System.IO.File.ReadAllBytes(path);
            int pos = 12, ch = 1, rate = 48000, bits = 16, dataStart = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
                else if (id == "data") { dataStart = pos + 8; dataLen = Math.Min(len, b.Length - dataStart); break; }
                pos += 8 + len + (len & 1);
            }

            if (dataStart < 0 || (bits != 16 && bits != 24)) return null;
            int bps = bits / 8, n = dataLen / bps;
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = dataStart + i * bps;
                x[i] = bits == 16 ? BitConverter.ToInt16(b, o) / 32768f : ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) / 8388608f);
            }

            AudioClip clip = AudioClip.Create(System.IO.Path.GetFileNameWithoutExtension(path), n / ch, ch, rate, false);
            clip.SetData(x, 0);
            return clip;
        }

        /// <summary>RBJ biquad, high- or low-pass, one pass.</summary>
        private static float[] Biquad(float[] x, int rate, float fc, float q, bool highPass)
        {
            double w = 2 * Math.PI * fc / rate, cw = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            double b0 = highPass ? (1 + cw) / 2 : (1 - cw) / 2, b1 = highPass ? -(1 + cw) : 1 - cw, b2 = b0, a0 = 1 + al, a1 = -2 * cw, a2 = 1 - al;
            var y = new float[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double o = (b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = o;
                y[i] = (float)o;
            }

            return y;
        }

        /// <summary>
        /// WP-072 B7 bird measurement (C-00 30.09 panel): post-mixer solo renders in the open V6 courtyard, Fighting phase, no fire —
        /// bed only (BirdLevel 0, 30 s) and birds only at 1.0 and 0.35 (every non-bird source muted, the bird pinned 20 m ahead /
        /// 10 m up, 24 forced calls 3 s apart: ~4 events per variant of the shuffle deck — 2 were too few, ±2 dB per run). Music / SFX / UI buses muted in all three.
        /// Each bird's onset (capture frame) and clip go to a sidecar CSV; tools/bird_contrast.py measures the own-band contrast.
        /// </summary>
        [UnityTest]
        [Explicit("WP-072 B7 kuş ölçümü — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator KusKontrasti_SoloRender_Olcum()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            AfterRainAmbience ar = _d.Ambience.AfterRain;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp072-birds");
            System.IO.Directory.CreateDirectory(dir);
            float oncekiKus = AfterRainAmbience.BirdLevel;
            _s.Rig.Health.DiagnosisInvulnerable = true;
            _s.Rig.Teleport(new Vector3(0f, 0.1f, 0f), Quaternion.identity);   // V6 plaza centre: open sky
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual(AcousticSpace.Open, ar.LastSpace, "ölçüm noktası açık avlu olmalı");
            var muted = new System.Collections.Generic.List<AudioSource>();
            try
            {
                foreach (int k in new[] { 1, 3, 4, 5 }) mixer.SetFloat(AudioMixerBridge.VolumeParams[k], -80f);

                // Bed only.
                AfterRainAmbience.BirdLevel = 0f;
                yield return new WaitForSecondsRealtime(1f);
                yield return KusKaydet(System.IO.Path.Combine(dir, "bed.wav"), 30f, null);

                foreach (var (ad, seviye) in new[] { ("birds-1.00", 1f), ("birds-0.35", 0.35f) })
                {
                    AfterRainAmbience.BirdLevel = seviye;
                    AfterRainAmbience.PinnedBirdOffset = _s.Rig.transform.forward * 20f + Vector3.up * 10f;
                    var csv = new System.Text.StringBuilder("seconds,clip\n");
                    int cagri = 0, kusLog = ar.BirdCount;
                    yield return KusKaydet(System.IO.Path.Combine(dir, ad + ".wav"), 74f, (c, t) =>
                    {
                        if (cagri < 24 && t > 1f + cagri * 3f) { ar.ForceDue(true, false, false); cagri++; }
                        foreach (AudioSource src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                        {
                            bool kus = src.clip != null && src.clip.name.StartsWith("AmbBirdsDistant", StringComparison.Ordinal);
                            if (!kus && src.isPlaying && !src.mute) { src.mute = true; muted.Add(src); }
                            if (kus && src.mute) src.mute = false;   // a pooled voice muted as a drip may carry the next bird
                            if (kus && src.isPlaying && ar.BirdCount != kusLog && src.time < 0.1f)
                            {
                                kusLog = ar.BirdCount;
                                csv.Append((c.FramesWritten / (double)AudioSettings.outputSampleRate).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(src.clip.name).Append('\n');
                            }
                        }
                    });
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ad + ".csv"), csv.ToString());
                    foreach (AudioSource src in muted) if (src != null) src.mute = false;
                    muted.Clear();
                }
            }
            finally
            {
                foreach (AudioSource src in muted) if (src != null) src.mute = false;
                AfterRainAmbience.PinnedBirdOffset = null;
                AfterRainAmbience.BirdLevel = oncekiKus;
                foreach (int k in new[] { 1, 3, 4, 5 }) mixer.ClearFloat(AudioMixerBridge.VolumeParams[k]);
            }

            Debug.Log("[DD-TEST] wp072.birds " + string.Join(" ", System.IO.Directory.GetFiles(dir)));
        }

        private IEnumerator KusKaydet(string path, float saniye, System.Action<AudioOutputCapture, float> her)
        {
            AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, path);
            float bas = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - bas < saniye)
            {
                her?.Invoke(cap, Time.realtimeSinceStartup - bas);
                yield return null;
            }

            long frames = cap.FramesWritten;
            UnityEngine.Object.DestroyImmediate(cap);
            foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
            if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
        }

        /// <summary>
        /// WP-072 B7 A9 (STANDARDS: limiter gain reduction ≤ 4 dB in the highest stack, master ≤ −1 dBFS) under the user's option B buses:
        /// top-down, six zombies closing in, SG1 every 0.9 s, the launcher (RL1) every 2 s with its explosion 8 m out. Rendered with the
        /// master limiter threshold opened to 0 dB (MasterLimiterThreshold, exposed) so the output is uncompressed: the envelope above
        /// −8 dBFS is the gain reduction the real −8 dB limiter would apply (wp-072/tools/a9_stack.py). Explicit, out of CI.
        /// </summary>
        [UnityTest]
        [Explicit("WP-072 B7 A9 yığın ölçümü — kanıt, CI dışı")]
        [Category("Diagnostics")]
        [Timeout(120000)]
        public IEnumerator A9_EnYuksekYigin_LimiterOncesiZarf_Olcum()
        {
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "wp072-a9");
            System.IO.Directory.CreateDirectory(dir);
            _s.Rig.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                _s.Dogur(_s.Zemin(Mathf.Cos(a) * 9f, Mathf.Sin(a) * 9f));
            }

            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, true);
            yield return new WaitForSecondsRealtime(3f);
            try
            {
                mixer.SetFloat("MasterLimiterThreshold", 0f);
                AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, System.IO.Path.Combine(dir, "a9-stack.wav"));
                float bas = Time.realtimeSinceStartup, sg = 0f, rl = 0.5f;
                while (Time.realtimeSinceStartup - bas < 12f)
                {
                    float t = Time.realtimeSinceStartup - bas;
                    Vector3 el = _s.Rig.transform.position + Vector3.up * 1.3f;
                    if (t >= sg) { AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", el, 1f, 50); sg = t + 0.9f; }
                    if (t >= rl)
                    {
                        AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "RL1", el + Vector3.right * 0.5f, 1f, 60);
                        AudioEvents.Raise(CombatAudioEvent.Explosion, _s.Zemin(0f, 8f) + Vector3.up, 1.4f, 80);
                        rl = t + 2f;
                    }

                    yield return null;
                }

                long frames = cap.FramesWritten;
                UnityEngine.Object.DestroyImmediate(cap);
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
                if (frames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                mixer.GetFloat(AudioMixerBridge.VolumeParams[4], out float player);
                mixer.GetFloat(AudioMixerBridge.VolumeParams[3], out float world);
                Assert.AreEqual(0f, player, 0.01f, "Run snapshot SFX-Player 0 (kullanıcı B, A4 kalibrasyonu −1)");
                Assert.AreEqual(-4f, world, 0.01f, "Run snapshot SFX-World −4 (kullanıcı B, A4 kalibrasyonu −1)");
                Debug.Log("[DD-TEST] wp072.a9 stack rendered " + frames + " frames · SfxPlayer " + player + " · SfxWorld " + world);
            }
            finally
            {
                mixer.ClearFloat("MasterLimiterThreshold");
            }
        }

        private static float[] OkuMono(string path, out int rate)
        {
            byte[] raw = System.IO.File.ReadAllBytes(path);
            int ch = raw[22] | (raw[23] << 8);
            rate = raw[24] | (raw[25] << 8) | (raw[26] << 16) | (raw[27] << 24);
            int n = (raw.Length - 44) / (2 * ch);
            var mono = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++) { int o = 44 + 2 * (i * ch + c); sum += (short)(raw[o] | (raw[o + 1] << 8)) / 32768f; }
                mono[i] = sum / ch;
            }

            return mono;
        }

        // ================================================================== WP-068 dilim 11 · UI ses v3

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UiV3_RuloYerlesinceDurur_HoldBirakilincaSoner_BootAtlamadaSoner()
        {
            AudioEventRouter r = _d.Router;
            int once = r.UiVoiceCount;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CounterRolling);
            yield return null;
            Assert.IsTrue(_d.UiVoicePlaying(AudioSoundId.UiCounterRoll), "Rulo başlamadı.");
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CounterSettled);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.UiCounterRoll), "Rulo CounterSettled'da 30 ms içinde durmalı.");

            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CounterRolling);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.UiCounterRoll), "Rulo 0,6 s tavanında kendiliğinden bitmeli.");

            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.RerollHoldStarted);
            yield return null;
            Assert.IsTrue(_d.UiVoicePlaying(AudioSoundId.UiHoldRub), "Hold sürtmesi başlamadı.");
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.Back);   // erken bırakma
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.UiHoldRub), "Erken bırakma sürtmeyi 20 ms'de kesmeli.");

            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.BootLogo);
            yield return null;
            Assert.IsTrue(_d.UiVoicePlaying(AudioSoundId.UiBootSeal), "Boot mührü (U7) çalmadı.");
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.TimelineSkipped);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.UiBootSeal), "Atlama boot mührünü 120 ms'de söndürmeli.");
            Assert.AreEqual(once + 4, r.UiVoiceCount);
            Debug.Log("[DD-TEST] wp068.voices " + r.UiVoiceCount + " · " + r.Describe());
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UiV3_PauseArayuzuDuyulur_PauseSesleri_UiVolumeAyriKaydirici()
        {
            try
            {
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                yield return null;
                Assert.AreEqual(AudioSoundId.UiPauseOpen, _d.LastPlayed, "Pause açılışı perde kısa varyantını çalmalı.");
                Assert.Greater(_d.Mixer.Gain(AudioBus.Interface), 0f, "Pause arayüzü susturmamalı.");
                Assert.AreEqual(0f, _d.Mixer.Gain(AudioBus.Impact), 1e-4f, "Pause dünyayı susturmalı.");
                int once = _d.PlayedCount;
                DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.DialogOpened);
                Assert.AreEqual(once + 1, _d.PlayedCount, "Pause menüsünde UI olayı çalmalı.");
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                yield return null;
                Assert.AreEqual(AudioSoundId.UiPauseClose, _d.LastPlayed, "RESUME açılış kaydırmasını çalmalı.");

                DD.Prototype.UI.UiSettings.UiVolume = 0.5f;   // U8
                Assert.AreEqual(0.5f, PrototypeAudio.UiVolume, 1e-4f);
                Assert.AreEqual(0.5f, _d.Mixer.GetPlayerVolume(AudioBus.Interface), 1e-4f, "UI SOUNDS Interface bus'a uygulanmalı.");
                Assert.AreEqual(1f, _d.Mixer.GetPlayerVolume(AudioBus.Weapon), 1e-4f, "Diğer bus'lar etkilenmemeli.");
            }
            finally
            {
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                DD.Prototype.UI.UiSettings.UiVolume = 1f;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator UiV3_KartBasamagi_DamgaKatmani_HoverSonumu_SliderPerdesi_SeritErteleme()
        {
            AudioEventRouter r = _d.Router;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CardDealt, "0");
            Assert.AreEqual(1.00f, r.LastUiPitch, 1e-4f);
            yield return new WaitForSecondsRealtime(0.07f);
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CardDealt, "1");
            Assert.AreEqual(1.02f, r.LastUiPitch, 1e-4f);
            yield return new WaitForSecondsRealtime(0.07f);
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.CardDealt, "2 last");
            Assert.AreEqual(1.04f, r.LastUiPitch, 1e-4f, "Dağıtma +0/+2/+4 %.");

            yield return new WaitForSecondsRealtime(0.2f);
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.StampLanded, "epic");
            Assert.AreEqual(AudioSoundId.UiStampEpic, _d.LastPlayed, "Epic madalya kendi katmanı.");

            yield return new WaitForSecondsRealtime(0.4f);
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.ButtonHover);
            Assert.AreEqual(1f, r.LastUiGain, 1e-4f);
            yield return null;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.ButtonHover);
            yield return null;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.ButtonHover);
            Assert.Less(r.LastUiGain, 0.7f, "Hover süpürmesi sönümlenmeli (× 0,8 adım).");

            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.SliderChanged, "UI SOUNDS", 1f);
            Assert.AreEqual(1.10f, r.LastUiPitch, 1e-3f, "Slider perdesi değeri izler.");

            int ertelenen = r.NewBestDeferred;
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.HudThreatEncircled);
            DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.HudNewBestRound);
            Assert.AreEqual(ertelenen + 1, r.NewBestDeferred, "Tehdit çipi varken şerit bekler.");
            yield return SpecialWeaponSahne.Bekle(() => _d.LastPlayed == AudioSoundId.HudNewBestRound, 4f, "Şerit çip sustuktan sonra çalmadı.");
            Debug.Log("[DD-TEST] wp068.ui gain " + r.LastUiGain.ToString("0.00") + " deferred " + r.NewBestDeferred);
        }

        /// <summary>
        /// WP-068 slice 11 acceptance "≥ 6 LU": every v3 menu cue stands at least 6 LU above the menu bed.
        /// Both are measured on the recorded output (<see cref="AudioOutputCapture"/>, post mixer / limiter):
        /// the bed (menu loop + beds on the Menu snapshot) as gated integrated loudness over 3 s; each cue on the
        /// same chain with the Music / Ambience groups pulled to −80 dB, as its loudest K-weighted 100 ms window.
        /// Separation = cue − bed. (A cue measured on top of the bed would read the bed's loop position, ±3 LU.)
        /// PressDown (a ≤ 40 ms tick under the Confirm that follows on release) is reported, not gated: a 100 ms
        /// short-term window cannot rate a 40 ms transient.
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator UiV3_MenuCuelari_YatakUstunde_6LU()
        {
            MusicDirector m = _d.Music;
            UnityEngine.Audio.AudioMixer mixer = _d.Bridge.Mixer;
            string bedPath = System.IO.Path.Combine(Application.temporaryCachePath, "wp068-bed.wav");
            string cuePath = System.IO.Path.Combine(Application.temporaryCachePath, "wp068-cues.wav");
            AudioOutputCapture cap = null;

            float[] ReadMono(string path, out int rate)
            {
                byte[] raw = System.IO.File.ReadAllBytes(path);
                int ch = raw[22] | (raw[23] << 8);
                rate = raw[24] | (raw[25] << 8) | (raw[26] << 16) | (raw[27] << 24);
                int n = (raw.Length - 44) / (2 * ch);
                var mono = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < ch; c++) { int o = 44 + 2 * (i * ch + c); sum += (short)(raw[o] | (raw[o + 1] << 8)) / 32768f; }
                    mono[i] = sum / ch;
                }

                return mono;
            }

            void StopCapture()
            {
                if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
                cap = null;
                foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
            }

            try
            {
                m.SetPhase(RunPhase.None);
                _d.Bridge.Transition(AudioSnapshot.Menu, 0.1f);
                yield return SpecialWeaponSahne.Bekle(() => m.PhaseLoop == AudioSoundId.MusicMenu && m.PhaseLoopLevel > 0.5f, 12f, "Menü döngüsü yükselmedi.");

                cap = AudioOutputCapture.Arm(_d.gameObject, bedPath);
                yield return new WaitForSecondsRealtime(3.2f);
                long bedFrames = cap.FramesWritten;
                StopCapture();
                if (bedFrames == 0) Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok).");
                float[] bedMono = ReadMono(bedPath, out int rate);
                float bedLufs = LoudnessMeter.Measure(bedMono, rate).IntegratedLufs;

                // The cues alone, same chain: music and beds out of the way.
                mixer.SetFloat(AudioMixerBridge.VolumeParams[1], -80f);
                mixer.SetFloat(AudioMixerBridge.VolumeParams[2], -80f);
                yield return new WaitForSecondsRealtime(0.3f);
                var cues = new (DD.Prototype.UI.UiEvent e, string d)[]
                {
                    (DD.Prototype.UI.UiEvent.PressDown, null), (DD.Prototype.UI.UiEvent.PaneOpened, null), (DD.Prototype.UI.UiEvent.CardDealt, "0"),
                    (DD.Prototype.UI.UiEvent.StampLanded, null), (DD.Prototype.UI.UiEvent.CurtainClosed, null), (DD.Prototype.UI.UiEvent.CurtainOpened, null),
                    (DD.Prototype.UI.UiEvent.ChoiceSelected, null), (DD.Prototype.UI.UiEvent.ScreenExiting, null)
                };
                cap = AudioOutputCapture.Arm(_d.gameObject, cuePath);
                var at = new long[cues.Length];

                // Paced on captured frames, not wall-clock: on a loaded machine (CI 2b300f06 ran beside a fullscreen dev
                // Player) the mixer falls behind real time, and a wall-clock wait could stop the capture before the last
                // cue's window was written — the clamped window then read as silence.
                System.Collections.Generic.IEnumerator<object> Frames(long target)
                {
                    float limit = Time.realtimeSinceStartup + 5f;
                    while (cap.FramesWritten < target && Time.realtimeSinceStartup < limit) yield return null;
                }

                for (int i = 0; i < cues.Length; i++)
                {
                    long from = i == 0 ? cap.FramesWritten : at[i - 1];
                    yield return new WaitForSecondsRealtime(0.1f);
                    yield return Frames(from + rate / 2);
                    at[i] = cap.FramesWritten;
                    DD.Prototype.UI.UiEvents.Raise(cues[i].e, cues[i].d);
                }

                yield return Frames(at[cues.Length - 1] + (long)(0.6f * rate));
                StopCapture();
                float[] cue = ReadMono(cuePath, out rate);
                Assert.GreaterOrEqual(cue.Length, at[cues.Length - 1] + (long)(0.45f * rate), "Yakalama son cue penceresinden önce bitti (ses hattı gerçek zamanın gerisinde).");

                int win = rate / 10, hop = rate / 100, n = cue.Length;
                var w = new float[win];
                var zayif = new System.Collections.Generic.List<string>();
                var rapor = new System.Text.StringBuilder();
                for (int i = 0; i < cues.Length; i++)
                {
                    float peak = -120f;
                    for (long s0 = at[i] - rate / 20; s0 < at[i] + (long)(0.4f * rate); s0 += hop)
                    {
                        long p0 = Math.Max(0, Math.Min(s0, n - win));
                        Array.Copy(cue, p0, w, 0, win);
                        peak = Mathf.Max(peak, LoudnessMeter.WindowLoudness(w, rate));
                    }

                    float fark = peak - bedLufs;
                    rapor.Append(cues[i].e).Append(' ').Append(fark.ToString("0.0")).Append(" LU · ");
                    if (fark < 6f && cues[i].e != DD.Prototype.UI.UiEvent.PressDown) zayif.Add(cues[i].e + " " + fark.ToString("0.0") + " LU");
                }

                Debug.Log("[DD-TEST] wp068.lu bed " + bedLufs.ToString("0.0") + " LUFS-I (3 s) · " + rapor);
                Assert.IsEmpty(zayif, "Menü yatağı üstünde < 6 LU kalan UI cue'ları");
            }
            finally
            {
                StopCapture();
                mixer.ClearFloat(AudioMixerBridge.VolumeParams[1]);
                mixer.ClearFloat(AudioMixerBridge.VolumeParams[2]);
                m.OnRunStarted();
                m.SetPhase(RunPhase.Fighting);
                _d.Bridge.Transition(AudioSnapshot.Run, 0.1f);
            }
        }

        // ================================================================== WP-065C · fūrin rüzgâr çanı

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Furin_RuzgarliHavada_EnYakinCanCalar_DurgunHavadaSessiz()
        {
            var cans = AudioSurfaceHints.WindChimes;
            var eski = new System.Collections.Generic.List<Vector3>(cans);
            try
            {
                cans.Clear();
                cans.Add(_s.Rig.transform.position + new Vector3(4f, 2.6f, 3f));    // yakın (≤ 16 m)
                cans.Add(_s.Rig.transform.position + new Vector3(40f, 2.6f, 0f));   // menzil dışı
                _d.Ambience.SetPhase(RunPhase.Upgrading);

                AmbienceDirector.FurinWindOverride = 0.05f;   // durgun hava
                int once = _d.Ambience.FurinCount;
                yield return new WaitForSecondsRealtime(4f);
                Assert.AreEqual(once, _d.Ambience.FurinCount, "Durgun havada fūrin çalmamalı.");

                AmbienceDirector.FurinWindOverride = 1f;      // tam rüzgâr: ~2,2 s ± %35 aralık
                yield return SpecialWeaponSahne.Bekle(() => _d.Ambience.FurinCount >= once + 2, 12f, "Rüzgârda fūrin iki kez çalmadı.");
                Debug.Log("[DD-TEST] wp065c.furin count=" + (_d.Ambience.FurinCount - once) + " chimes=" + cans.Count);
            }
            finally
            {
                AmbienceDirector.FurinWindOverride = null;
                cans.Clear();
                cans.AddRange(eski);
            }
        }

        // ================================================================== WP-021C · master limiter ölçümü

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Limiter_EnYuksekYigin_CikisTepesi_TavaninAltinda()
        {
            // The loudest stack the game can produce in one frame, at its loudest gains.
            var buffer = new float[2048];
            float peak = 0f; double sumSq = 0; long count = 0; int silentFrames = 0;
            for (int rep = 0; rep < 3; rep++)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.ClassicWeaponFire, Vector3.zero, 1.4f, 50);
                AudioEvents.RaiseCritical(CombatAudioEvent.Explosion, Konum(0.5f), 1.4f, 90);
                AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponSpawn, Konum(0.5f), 1.4f, 100);
                AudioEvents.RaiseCritical(CombatAudioEvent.HeavyWeaponFire, Konum(0.5f), 1.4f, 60);
                AudioEvents.RaiseCritical(CombatAudioEvent.ArmorBreak, Konum(0.5f), 1.4f, 80);
                RunAudioEvents.Raise(RunAudioEvent.RoundStart, default, 1.4f, 0);
                DD.Prototype.UI.UiEvents.Raise(DD.Prototype.UI.UiEvent.HudThreatEncircled);
                RunAudioEvents.Raise(RunAudioEvent.GoldCollected, Konum(0.2f), 1.4f, 5);
                for (int f = 0; f < 20; f++)
                {
                    yield return null;
                    AudioListener.GetOutputData(buffer, 0);
                    float frameMax = 0f;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        float a = Mathf.Abs(buffer[i]);
                        if (a > frameMax) frameMax = a;
                        sumSq += buffer[i] * buffer[i];
                    }

                    count += buffer.Length;
                    if (frameMax < 1e-6f) silentFrames++;
                    if (frameMax > peak) peak = frameMax;
                }
            }

            if (peak < 1e-5f)
            {
                Assert.Inconclusive("Ses çıkışı sessiz (batch/aygıt yok): limiter ölçümü bu ortamda yapılamıyor.");
            }

            float peakDb = 20f * Mathf.Log10(peak);
            float rmsDb = 20f * Mathf.Log10((float)Math.Sqrt(sumSq / Math.Max(1, count)) + 1e-9f);
            Debug.Log("[DD-TEST] wp021c.limiter stack peak=" + peakDb.ToString("0.00") + " dBFS rms=" + rmsDb.ToString("0.0") + " dBFS silentFrames=" + silentFrames + "/60");
            Assert.LessOrEqual(peakDb, -1.0f, "Master çıkış tepesi −1 dBFS tavanını aştı: " + peakDb.ToString("0.00"));
        }

        // ================================================================== WP-060 · output capture

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CikisKaydi_DinlemeTuru_WavVeFazIsaretleri()
        {
            string captureDirectory = System.IO.Path.Combine(Application.temporaryCachePath, "wp065-capture-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(captureDirectory);
            string path = System.IO.Path.Combine(captureDirectory, "first.wav");
            AudioOutputCapture cap = AudioOutputCapture.Arm(_d.gameObject, path);
            for (int rep = 0; rep < 3; rep++)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.ClassicWeaponFire, Vector3.zero, 1f, 50);
                RunAudioEvents.Raise(RunAudioEvent.RoundStart, default, 1f, 0);
                yield return new WaitForSecondsRealtime(0.4f);
            }

            RunEvents.PublishPhaseChanged(RunPhase.Fighting);
            yield return new WaitForSecondsRealtime(0.3f);
            long frames = cap.FramesWritten;
            long dropped = cap.DroppedSamples;
            UnityEngine.Object.DestroyImmediate(cap);   // Finish(): header patched, markers closed
            Assert.IsNull(AudioOutputCapture.Armed, "Destroy sonrası statik capture sahibi bırakılmalı.");

            string secondPath = System.IO.Path.Combine(captureDirectory, "second.wav");
            AudioOutputCapture second = AudioOutputCapture.Arm(_d.gameObject, secondPath);
            Assert.AreEqual(secondPath, second.Path, "Re-arm yeni dosya yolunu kullanmalı.");
            AudioEvents.RaiseCritical(CombatAudioEvent.ClassicWeaponFire, Vector3.zero, 1f, 50);
            yield return new WaitForSecondsRealtime(0.3f);
            long secondFrames = second.FramesWritten;
            long secondDropped = second.DroppedSamples;
            UnityEngine.Object.DestroyImmediate(second);
            Assert.IsNull(AudioOutputCapture.Armed, "İkinci capture kapanınca statik sahip bırakılmalı.");

            var info = new System.IO.FileInfo(path);
            string markers = System.IO.File.ReadAllText(path + ".markers.txt");
            string telemetry = System.IO.File.ReadAllText(path + ".telemetry.tsv");
            string secondMarkers = System.IO.File.ReadAllText(secondPath + ".markers.txt");
            string secondTelemetry = System.IO.File.ReadAllText(secondPath + ".telemetry.tsv");
            Debug.Log("[DD-TEST] wp060.capture frames=" + frames + " dropped=" + dropped + " bytes=" + info.Length + " markers=" + markers.Split('\n').Length);
            if (frames == 0 || secondFrames == 0)
            {
                Assert.Inconclusive("Ses çıkışı yok (batch/aygıt yok): kayıt bu ortamda alınamıyor.");
            }

            Assert.AreEqual(0, dropped, "Kayıt halkası örnek düşürdü.");
            Assert.Greater(info.Length, 44 + frames, "WAV gövdesi yazılmadı.");
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
            {
                Assert.AreEqual("RIFF", new string(r.ReadChars(4)));
                r.ReadInt32();
                Assert.AreEqual("WAVE", new string(r.ReadChars(4)));
            }

            StringAssert.Contains("phase\tFighting", markers, "Faz işareti yazılmadı.");
            StringAssert.Contains("density\t", markers, "Yoğunluk işareti yazılmadı.");
            StringAssert.Contains("capture\tend", markers, "Kapanış işareti yazılmadı.");
            var requested = System.Text.RegularExpressions.Regex.Match(telemetry, @"(?m)^Requested\t([1-9][0-9]*)$");
            var started = System.Text.RegularExpressions.Regex.Match(telemetry, @"(?m)^Started\t([1-9][0-9]*)$");
            Assert.IsTrue(requested.Success, "İstek yaşam döngüsü pozitif sayıyla yazılmadı.");
            Assert.IsTrue(started.Success, "Voice başlangıcı pozitif sayıyla yazılmadı.");
            StringAssert.Contains("# combat_event\tdelivered\tsuppressed", telemetry, "Event teslim/bastırma tablosu yazılmadı.");
            StringAssert.Contains("# ring_overflow\t0", telemetry, "Telemetri halkası taştı.");
            Assert.Greater(secondFrames, 0, "İkinci capture mevcut listener tap'ini yeniden bağlayamadı.");
            Assert.AreEqual(0, secondDropped, "İkinci capture halkası örnek düşürdü.");
            Assert.Greater(new System.IO.FileInfo(secondPath).Length, 44 + secondFrames, "İkinci WAV gövdesi yazılmadı.");
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(secondPath)))
            {
                Assert.AreEqual("RIFF", new string(r.ReadChars(4)));
                r.ReadInt32();
                Assert.AreEqual("WAVE", new string(r.ReadChars(4)));
            }
            StringAssert.Contains("capture\tend", secondMarkers, "İkinci capture kapanış işareti yazılmadı.");
            StringAssert.Contains("# ring_overflow\t0", secondTelemetry, "İkinci telemetry raporu yazılmadı ya da taştı.");
        }

        // ================================================================== GC 0

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AmbiyansYolu_V6Fp_YagmurSonrasi_SifirAyirma()
        {
            // DEC-088 GC 0 (S-06 30.09 profiler: PrototypeAudioDirector.Update 8 × ~51 B / 300 frames after V6 + FP) — the
            // after-rain drip built a diagnosis string per drip. The ambience tick with every after-rain clock forced due (birds,
            // eave drips, gusts, leaf drips, grove bed) on the default world, first-person camera, allocates nothing.
            AfterRainAmbience ar = _d.Ambience.AfterRain;
            Assert.IsTrue(AmbienceDirector.WetLook && AfterRainAmbience.SunnyAfterRain, "V6 ıslak görünüm + yağmur sonrası");
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, true);
            try
            {
                yield return new WaitForSecondsRealtime(0.5f);
                for (int i = 0; i < 64; i++)
                {
                    ar.ForceDue();
                    _d.Ambience.Tick(1f / 60f, false);
                    _d.Router.Tick();
                }

                yield return null;
                byte[] kontrol = null;
                Assert.That(() => { kontrol = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "Pozitif kontrol: GC ölçümü ayırmayı görmedi.");
                GC.KeepAlive(kontrol);

                // Past the drip retrigger window (0.25 s unscaled) so the measured ticks really play a drip, not only gate it.
                yield return new WaitForSecondsRealtime(0.3f);
                int damla = ar.EaveDripCount;
                Assert.That(() =>
                {
                    for (int i = 0; i < 200; i++)
                    {
                        ar.ForceDue();
                        _d.Ambience.Tick(1f / 60f, false);
                        _d.Router.Tick();
                    }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Ambiyans yolu (yağmur sonrası, V6 + FP) ayırdı — DEC-088 GC 0.");
                Assert.Greater(ar.EaveDripCount, damla, "ölçüm penceresinde damla çaldı (yol gerçekten koştu)");
                Debug.Log("[DD-TEST] wp072.gc ambience threadAlloc=none over 200 forced after-rain ticks · drips +" + (ar.EaveDripCount - damla) + " · " + ar.LastDrip);
            }
            finally
            {
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, true);
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SesYolu_KararliDurumda_SifirAyirma()
        {
            // Isınma: her yolun ilk çağrısı (klip deck'i, sözlük girdisi) kabul edilir.
            for (int i = 0; i < 64; i++)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.FleshHit, Konum(i % 5), 1f, 30);
                AudioEvents.RaiseCritical(CombatAudioEvent.SurfaceImpactStone, Konum(i % 7), 1f, 20);
                RunAudioEvents.Raise(RunAudioEvent.GoldCollected, Konum(1f), 1f, i % 6);
            }

            yield return null;
            yield return null;

            // Pozitif kontrol: ölçüm yöntemi ayırmayı görüyor (WP-005 §35).
            // WP-051C: the process-wide used heap (GC.GetTotalMemory) also sees other Editor threads' allocations; the test
            // framework's GC.Alloc recorder is filtered to the calling thread only, and the positive control proves it is not blind.
            byte[] kontrol = null;
            Assert.That(() => { kontrol = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "Pozitif kontrol: GC ölçümü ayırmayı görmedi.");
            GC.KeepAlive(kontrol);

            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    AudioEvents.RaiseCritical(CombatAudioEvent.FleshHit, Konum(i % 5), 1f, 30);
                    AudioEvents.RaiseCritical(CombatAudioEvent.SurfaceImpactStone, Konum(i % 7), 1f, 20);
                    RunAudioEvents.Raise(RunAudioEvent.GoldCollected, Konum(1f), 1f, i % 6);
                    _d.Router.Tick();
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Ses yolu 600 cue'da ayırdı (bu iş parçacığında GC.Alloc örneği; pencere 0 hedef).");
            Debug.Log("[DD-TEST] wp021.gc threadAlloc=none over 600 cues + 200 router ticks");
        }
    }
}
