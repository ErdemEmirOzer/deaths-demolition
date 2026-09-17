using System;
using System.Collections;
using DD.Prototype.Audio;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.FootLeft);
            Assert.AreEqual(once + 1, r.FootstepCount, "foot_l ayak sesi üretmedi.");
            // WP-021C: the surface under the rig is whatever the world's tags + routes say (Default still reads as stone).
            AudioSoundId beklenen = AyakBeklenen(_s.Rig.transform.position, out SurfaceKind altta);
            Assert.AreEqual(beklenen, _d.LastPlayed, "Ayak sesi altındaki yüzeye/plakaya göre çalmalı (" + altta + ").");
            yield return new WaitForSecondsRealtime(0.2f);   // PlayerFootstep MinRetrigger 0.08 s
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.FootRight);
            Assert.AreEqual(once + 2, r.FootstepCount, "foot_r ayak sesi üretmedi.");
            Assert.AreEqual(AudioSoundId.FootstepWood, AudioEventRouter.FootstepFor(DD.Prototype.Feedback.SurfaceKind.Wood));
            Assert.AreEqual(AudioSoundId.FootstepWater, AudioEventRouter.FootstepFor(DD.Prototype.Feedback.SurfaceKind.Water));
            Debug.Log("[DD-TEST] wp021b.footstep " + r.Describe());
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

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator MixGecisi_FazOlcumu_LowHealthMuzikDuck()
        {
            // WP-057 mix pass: output RMS per phase (report) + the HudLowHealth ↔ taiko rule (Music bus ducks).
            var buffer = new float[2048];
            float fightDb = 0f, breakDb = 0f, menuDb = 0f;
            // WP-058B: K-weighted, gated loudness (BS.1770 via LoudnessMeter) of the captured output, not raw dBFS RMS.
            var captured = new System.Collections.Generic.List<float>(45 * 2048);
            IEnumerator Measure(System.Action<float> sink)
            {
                captured.Clear();
                for (int f = 0; f < 45; f++)
                {
                    yield return null;
                    AudioListener.GetOutputData(buffer, 0);
                    captured.AddRange(buffer);
                }

                LoudnessMeter.Result r = LoudnessMeter.Measure(captured.ToArray(), AudioSettings.outputSampleRate);
                sink(r.IntegratedLufs);
            }

            MusicDirector m = _d.Music;
            try
            {
                m.IntensityOverride = 0.9f; m.AccentOverride = true;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Measure(v => fightDb = v);
                m.SetPhase(RunPhase.Upgrading); _d.Bridge.Transition(AudioSnapshot.RoundBreak, 0.2f);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Measure(v => breakDb = v);
                m.SetPhase(RunPhase.None); _d.Bridge.Transition(AudioSnapshot.Menu, 0.2f);
                yield return new WaitForSecondsRealtime(3.5f);
                yield return Measure(v => menuDb = v);
                Debug.Log("[DD-TEST] wp057.mix fight=" + fightDb.ToString("0.0") + " LUFS break=" + breakDb.ToString("0.0") + " menu=" + menuDb.ToString("0.0") + " · " + m.Describe());
                if (fightDb > -60f)
                {
                    Assert.Greater(fightDb, menuDb - 0.1f, "Dövüş karışımı menüden sessiz olmamalı.");
                }

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
        public IEnumerator SpecialTepkisi_SwPushBeat_TepkiVokali()
        {
            AudioEventRouter r = _d.Router;
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 5f));
            yield return null;
            int once = r.ReactionCount;
            var info = new DD.Prototype.Enemies.EnemyCombatEventInfo(DD.Prototype.Enemies.EnemyCombatEventKind.SpecialPush, z.transform, z.Tier, z.LifeId,
                z.transform.position, Vector3.forward, DD.Prototype.Core.Config.ArmorSlot.None, false, 0f);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in info);
            Assert.AreEqual(once + 1, r.ReactionCount, "sw_push tepki vokali üretmedi.");
            Assert.AreEqual(AudioSoundId.ZombieReactPush, _d.LastPlayed);
            var pull = new DD.Prototype.Enemies.EnemyCombatEventInfo(DD.Prototype.Enemies.EnemyCombatEventKind.SpecialPull, z.transform, z.Tier, z.LifeId,
                z.transform.position, Vector3.forward, DD.Prototype.Core.Config.ArmorSlot.None, false, 0f);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in pull);
            DD.Prototype.Enemies.EnemyCombatEvents.Raise(in pull);   // same body, same frame → throttled to one
            Assert.AreEqual(once + 2, r.ReactionCount, "sw_pull beden başına 0,25 s kısma.");
            Debug.Log("[DD-TEST] wp058c.react " + r.Describe());
        }

        // ================================================================== WP-058 · WP-056 Special kullanım sesleri

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SpecialKullanim_FiredFieldHit_KendiSesleri_AlanDongusu()
        {
            AudioEventRouter r = _d.Router;
            int once = r.SpecialUsageCount;
            Vector3 at = _s.Zemin(0f, 6f);
            var id = DD.Prototype.Core.Config.SpecialWeaponId.TideJewel;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(id, DD.Prototype.Weapons.SpecialUsageKind.Fired, "sw_tide_pull", _s.Rig.transform.position, Vector3.forward, 0f, 0f, 1);
            Assert.AreEqual(once + 1, r.SpecialUsageCount, "Fired ses üretmedi.");
            Assert.AreEqual(AudioSoundId.SwFireTide, _d.LastPlayed);
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(id, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, "sw_tide_well", at, Vector3.forward, 4f, 6f, 0);
            yield return null;
            Assert.AreEqual(1, _d.FieldLoopsPlaying, "Tide kuyusu döngüsü çalmıyor.");
            yield return new WaitForSecondsRealtime(0.1f);
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(id, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, "sw_tide_well", at, Vector3.forward, 4f, 0f, 3);
            yield return null;
            Assert.AreEqual(0, _d.FieldLoopsPlaying, "FieldEnded döngüyü durdurmalı.");
            Assert.AreEqual(AudioSoundId.SwFieldEndTide, _d.LastPlayed);
            yield return new WaitForSecondsRealtime(0.1f);
            var kagura = DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(kagura, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, "sw_kagura_wave", at, Vector3.forward, 6f, 1.2f, 0);
            Assert.AreEqual(AudioSoundId.SwFieldKagura, _d.LastPlayed, "Kagura cephesi one-shot.");
            Assert.AreEqual(0, _d.FieldLoopsPlaying, "Kagura döngü değil.");
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
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Eject);
            Assert.AreEqual(once + 1, r.FoleyCount, "eject foley üretmedi.");
            Assert.AreEqual(AudioEventMap.WeaponFoley(weapon, DD.Prototype.Characters.CharacterAnimationEvent.Eject), _d.LastPlayed, "Seçili silahın kendi eject sesi.");
            yield return new WaitForSecondsRealtime(0.1f);
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.Chamber);
            Assert.AreEqual(AudioEventMap.WeaponFoley(weapon, DD.Prototype.Characters.CharacterAnimationEvent.Chamber), _d.LastPlayed);
            yield return new WaitForSecondsRealtime(0.1f);
            events.RaiseForTest(DD.Prototype.Characters.CharacterAnimationEvent.PickupDone);
            Assert.AreEqual(AudioSoundId.PickupDone, _d.LastPlayed);
            Assert.IsTrue(AmbienceDirector.WetLook, "Look B (WP-029 B4) → WetLook açık.");
            Debug.Log("[DD-TEST] wp048b.foley weapon=" + weapon + " " + r.Describe() + " · thunder " + _d.Ambience.ThunderCount + " pond " + _d.Ambience.PondPlaying);
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
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "wp060-capture-test.wav");
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

            var info = new System.IO.FileInfo(path);
            string markers = System.IO.File.ReadAllText(path + ".markers.txt");
            Debug.Log("[DD-TEST] wp060.capture frames=" + frames + " dropped=" + dropped + " bytes=" + info.Length + " markers=" + markers.Split('\n').Length);
            if (frames == 0)
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
            StringAssert.Contains("capture\tend", markers, "Kapanış işareti yazılmadı.");
            foreach (var tap in UnityEngine.Object.FindObjectsOfType<AudioOutputTap>(true)) UnityEngine.Object.DestroyImmediate(tap);
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
            long k0 = GC.GetTotalMemory(false);
            byte[] kontrol = new byte[64 * 1024];
            long k1 = GC.GetTotalMemory(false);
            Assert.Greater(k1 - k0, 32 * 1024, "Pozitif kontrol: GC ölçümü ayırmayı görmedi.");
            GC.KeepAlive(kontrol);

            int gen0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < 200; i++)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.FleshHit, Konum(i % 5), 1f, 30);
                AudioEvents.RaiseCritical(CombatAudioEvent.SurfaceImpactStone, Konum(i % 7), 1f, 20);
                RunAudioEvents.Raise(RunAudioEvent.GoldCollected, Konum(1f), 1f, i % 6);
                _d.Router.Tick();
            }

            long after = GC.GetTotalMemory(false);
            long delta = after - before;
            Assert.AreEqual(gen0, GC.CollectionCount(0), "Ölçüm penceresinde gen0 toplama oldu; pencere geçersiz.");
            Assert.LessOrEqual(delta, 2048, "Ses yolu 600 cue'da " + delta + " B ayırdı (eşik 2 KB, pencere 0 hedef).");
            Debug.Log("[DD-TEST] wp021.gc delta=" + delta + "B over 600 cues + 200 router ticks");
        }
    }
}
