using System;
using System.Collections;
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
            int played = sp.Played;
            for (int hop = 0; hop < 3; hop++) Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.Hit, at + Vector3.right * hop, hop);
            Yayinla(raiju, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, at, 0);
            Assert.AreEqual(played, sp.Played, "zincir sıçramaları 40 ms aralıkla gecikmeli");
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(played + 3, sp.Played, "üç sıçrama, sönüş yok");
            Assert.AreEqual(AudioSoundId.SwArcChainJump, sp.LastSound);
            yield return new WaitForSeconds(0.2f);
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

        // ================================================================== WP-072 B1 · ateş sesi v3

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AtesV3_HibritDongu_TekAtis_MekanKuyrugu()
        {
            WeaponFireAudio f = _d.WeaponFire;
            if (!_d.Library.IsProduced(AudioSoundId.WeaponSmg1Fire))
            {
                Assert.Fail("v016 ateş kimlikleri katalogda değil (AudioCatalogBuilder).");
            }

            Vector3 at = _s.Rig.transform.position + Vector3.up;
            int starts = f.LoopStarts, releases = f.Releases;

            // SMG1 900 RPM: ilk atış tam ses, sonrakiler transient + gövde döngüsü; bırakınca kuyruk bir kez.
            // Deterministik (WP-051C kararsızlığı): oyun zamanı kare başına tam 1/60 s — 4 kare = 66,7 ms aralık, duvar
            // saatindeki takılma seriyi bölemez; her atış 2 m ayrı → AudioEvents yinelenen-ipucu kısması (1,6 m / 40 ms) devreye girmez.
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SMG1", at + Vector3.right * (2f * i), 0.75f, 50);
                    if (i == 0) Assert.AreEqual(AudioSoundId.WeaponSmg1Fire, f.LastVoice, "İlk atış tam SMG1 sesi.");
                    for (int k = 0; k < 4; k++) yield return null;
                }

                Assert.AreEqual(AudioSoundId.WeaponSmg1Transient, f.LastVoice, "Seri içinde transient.");
                Assert.AreEqual(starts + 1, f.LoopStarts, "Seri başına tek döngü başlangıcı.");
                Assert.IsTrue(f.LoopPlaying, "Tetik basılıyken gövde döngüsü çalar.");
                for (int k = 0; k < 15; k++) yield return null;   // 0,25 s oyun zamanı: bırakış (107 ms) + 40 ms sönüş
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }

            Assert.IsFalse(f.LoopPlaying, "Tetik bırakılınca döngü söner.");
            Assert.AreEqual(releases + 1, f.Releases, "Bırakış bir kez.");
            Assert.AreEqual(AudioSoundId.WeaponTailSmgOpen, f.LastTail, "Varsayılan mekân Open.");

            // Tek atış sınıfı: her atışta kuru ses + mekân kuyruğu; mekân çözücüsü kuyruğu seçer.
            System.Func<Vector3, AcousticSpace> onceki = AcousticSpaces.Resolver;
            AcousticSpaces.Resolver = _ => AcousticSpace.Hall;
            try
            {
                AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, "SG1", at, 1.5f, 50);
                Assert.AreEqual(AudioSoundId.WeaponSg1Fire, f.LastVoice);
                Assert.AreEqual(AudioSoundId.WeaponTailSgHall, f.LastTail, "Salon kuyruğu.");
                Assert.IsFalse(f.LoopPlaying, "Tek atış silahı döngü açmaz.");
            }
            finally
            {
                AcousticSpaces.Resolver = onceki;
            }

            // Kimliksiz ipucu eski yola düşer (A/B/C'nin v016'sız hali ve diğer üreticiler).
            yield return new WaitForSeconds(0.1f);
            AudioEvents.Raise(CombatAudioEvent.ClassicWeaponFire, at, 1f, 50);
            Assert.AreEqual(AudioSoundId.RifleFire, _d.LastPlayed, "Kimliksiz ateş eski RifleFire.");
            Debug.Log("[DD-TEST] wp072b1.fire loopStarts=" + f.LoopStarts + " releases=" + f.Releases + " tail=" + f.LastTail);
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
            StringAssert.StartsWith("CasingRifle", _d.Casings.LastSound.ToString(), "AR1 = tüfek kovanı.");

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
                for (int i = 0; i < cues.Length; i++)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    at[i] = cap.FramesWritten;
                    DD.Prototype.UI.UiEvents.Raise(cues[i].e, cues[i].d);
                }

                yield return new WaitForSecondsRealtime(0.6f);
                StopCapture();
                float[] cue = ReadMono(cuePath, out rate);

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
