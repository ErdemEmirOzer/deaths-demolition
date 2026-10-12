#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// S-02 12.10 (C-00 ONAY, bisect K/wp-104/b2/evidence/BISECT-SpecialAlmaVeBitim.txt): Special alındıktan 1–2 kare sonra ara sıra
    /// binlerce ayırmalık bir kare var. Bu tanı testi alımdan sonraki <see cref="Kare"/> kareyi ProfilerRecorder'larla izler: karedeki
    /// ayırma sayısı / baytı, aday işaretçilerin süresi ve örnek sayısı, Editor'ün asenkron gölgelendirici derlemesi sürüyor mu
    /// (ShaderUtil.anythingCompiling) ve kare süresi; iki Special'ı sırayla alır (ilk kullanım iki kez). [DD-PROBE] satırları.
    /// (ilk sürüm ProfilerDriver + ayırma çağrı yığınlarıyla batch modda Editor'ü kilitledi - o yol kullanılmıyor.)
    /// Explicit: yalnız adıyla koşar, kapı değildir.
    /// </summary>
    [TestFixture]
    [Explicit]
    [Category("Diagnostics")]
    public sealed class Wp104SpecialPickupAllocProbeTests
    {
        private const int Kare = 10;

        private static readonly string[] Isaretciler =
        {
            "GC.Alloc", "GC.Collect", "Shader.CreateGPUProgram", "CreateGPUProgram", "Shader.Parse", "Instantiate", "Loading.AwakeFromLoad",
            "Animator.Initialize", "Animator.Rebind", "Canvas.SendWillRenderCanvases", "Canvas.BuildBatch", "Font.CacheFontForText",
            "TextMeshPro.GenerateText", "Mesh.UploadMeshData", "ParticleSystem.Play", "AudioSource.Play", "Resources.Load",
            "WP107.WallBuy.Update", "WP104.Tree.ShotHooks", "BehaviourUpdate", "LateBehaviourUpdate", "CoroutinesDelayedCalls"
        };

        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SpecialAlma_SonrakiKareler_IsaretcilerVeDerleme()
        {
            Application.targetFrameRate = -1;   // test.sh: ProfilerRecorder LastValue needs it in the Editor runner
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();

            // markers by name, whatever their category (ProfilerRecorderHandle.GetAvailable lists every registered marker / counter)
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var rec = new List<ProfilerRecorder>(Isaretciler.Length);
            foreach (string m in Isaretciler)
            {
                ProfilerRecorderHandle h = default;
                foreach (ProfilerRecorderHandle c in handles)
                {
                    if (ProfilerRecorderHandle.GetDescription(c).Name == m) { h = c; break; }
                }

                rec.Add(h.Valid ? new ProfilerRecorder(h, 4, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.StartImmediately) : default);
            }
            var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var rapor = new StringBuilder();
            // which Special / UI events fired in which frame (the allocation read after a yield is the previous frame's)
            System.Action<DD.Prototype.Weapons.SpecialWeaponHudState> onChanged = st => Olay("Changed:" + st.Phase);
            System.Action<float> onPicked = d => Olay("PickedUp");
            System.Action<DD.Prototype.Weapons.SpecialUsageEvent> onUsage = e => Olay("Usage:" + e.Kind);
            System.Action<DD.Prototype.UI.UiEvent, string> onUi = (e, d) => Olay("Ui:" + e);
            DD.Prototype.Weapons.WeaponEvents.SpecialChanged += onChanged;
            DD.Prototype.Weapons.WeaponEvents.SpecialWeaponPickedUp += onPicked;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Usage += onUsage;
            DD.Prototype.UI.UiEvents.Raised += onUi;
            try
            {
                for (int tur = 0; tur < 2; tur++)
                {
                    yield return _s.SpawnEt(0.5f);
                    for (int i = 0; i < 4; i++) yield return null;
                    rapor.Append("\n  TUR ").Append(tur).Append(" önce: ").Append(Satir(count, bytes, rec));
                    _s.PickupaGit(_s.CanliPickup());
                    for (int i = 0; i < Kare; i++)
                    {
                        yield return null;
                        rapor.Append("\n   +").Append(i + 1).Append(' ').Append(Satir(count, bytes, rec));
                    }

                    // end this activation before the next pickup
                    Time.captureDeltaTime = 0.05f;
                    float r0 = Time.realtimeSinceStartup;
                    while (_s.Controller.SpecialWeaponActive && Time.realtimeSinceStartup - r0 < 60f) yield return null;
                    Time.captureDeltaTime = 0f;
                    for (int i = 0; i < 3; i++) yield return null;
                }
            }
            finally
            {
                DD.Prototype.Weapons.WeaponEvents.SpecialChanged -= onChanged;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponPickedUp -= onPicked;
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Usage -= onUsage;
                DD.Prototype.UI.UiEvents.Raised -= onUi;
                Time.captureDeltaTime = 0f;
                foreach (ProfilerRecorder r in rec) r.Dispose();
                count.Dispose();
                bytes.Dispose();
            }

            Debug.Log("[DD-PROBE] wp104.specialAlloc" + rapor);
        }

        /// <summary>
        /// The control: no pickup at all - the same scene idles <see cref="BosKare"/> frames and every frame over <see cref="BuyukKare"/>
        /// allocations is logged with its time. Bursts here too mean the burst is not the Special's (Editor / harness work on the main thread).
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Bosta_AlimYok_BuyukAyiranKareler()
        {
            Application.targetFrameRate = -1;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            yield return _s.SpawnEt(0.5f);
            var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var rapor = new StringBuilder();
            int buyuk = 0;
            long enFazla = 0;
            float t0 = Time.realtimeSinceStartup;
            rapor.Append("\n   başta ").Append(Durum());
            try
            {
                for (int i = 0; i < 4; i++) yield return null;
                for (int i = 0; i < BosKare; i++)
                {
                    yield return null;
                    long c = count.LastValue;
                    if (c > enFazla) enFazla = c;
                    if (c <= BuyukKare) continue;
                    buyuk++;
                    if (buyuk <= 20)
                        rapor.Append("\n   kare +").Append(i + 1).Append(" t=").Append((Time.realtimeSinceStartup - t0).ToString("F3")).Append("s alloc=")
                            .Append(c).Append('/').Append(bytes.LastValue).Append("B dt=").Append((Time.unscaledDeltaTime * 1000f).ToString("F1")).Append("ms ")
                            .Append(Durum());
                }
            }
            finally
            {
                count.Dispose();
                bytes.Dispose();
            }

            Debug.Log("[DD-PROBE] wp104.idleAlloc kare=" + BosKare + " esikUstu(" + BuyukKare + ")=" + buyuk + " enFazla=" + enFazla + " sure=" +
                      (Time.realtimeSinceStartup - t0).ToString("F1") + "s" + rapor);
        }

        private const int BosKare = 600, BuyukKare = 1000;

        /// <summary>
        /// The run-start burst, recorded for an offline look: the Unity Profiler's binary log (Profiler.logFile, the runtime API - no live
        /// ProfilerDriver, which locked the batch Editor) over the first <see cref="LogKare"/> frames after the scene is up, to
        /// DD_PROBE_RAW. Wp104ProfilerRawDumpTests (EditMode, Explicit) opens it and names who allocates in the biggest frames.
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Bosta_ProfilLogu_KosuBasi()
        {
            string raw = System.Environment.GetEnvironmentVariable("DD_PROBE_RAW");
            Assert.IsFalse(string.IsNullOrEmpty(raw), "DD_PROBE_RAW yok");
            Application.targetFrameRate = -1;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            UnityEngine.Profiling.Profiler.logFile = raw;
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            long enFazla = 0;
            int enFazlaKare = -1;
            try
            {
                yield return _s.SpawnEt(0.5f);
                for (int i = 0; i < LogKare; i++)
                {
                    yield return null;
                    if (count.LastValue > enFazla) { enFazla = count.LastValue; enFazlaKare = Time.frameCount - 1; }
                }
            }
            finally
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                UnityEngine.Profiling.Profiler.logFile = "";
                count.Dispose();
            }

            Debug.Log("[DD-PROBE] wp104.runStartRaw raw=" + raw + " kare=" + LogKare + " enFazla=" + enFazla + " frame=" + enFazlaKare);
        }

        private const int LogKare = 120;

        /// <summary>The post-pickup burst, recorded the same way (DD_PROBE_RAW): the log starts 60 frames after the run-start scans, then two
        /// pickups with <see cref="AlimKare"/> frames after each.</summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Alim_ProfilLogu_IkiAlim()
        {
            string raw = System.Environment.GetEnvironmentVariable("DD_PROBE_RAW");
            Assert.IsFalse(string.IsNullOrEmpty(raw), "DD_PROBE_RAW yok");
            Application.targetFrameRate = -1;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            for (int i = 0; i < 60; i++) yield return null;   // past the run-start settle scans
            var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            var rapor = new StringBuilder();
            UnityEngine.Profiling.Profiler.logFile = raw;
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            try
            {
                for (int tur = 0; tur < 2; tur++)
                {
                    yield return _s.SpawnEt(0.5f);
                    for (int i = 0; i < 4; i++) yield return null;
                    _s.PickupaGit(_s.CanliPickup());
                    rapor.Append("\n  tur ").Append(tur).Append(" f").Append(Time.frameCount).Append(':');
                    for (int i = 0; i < AlimKare; i++)
                    {
                        yield return null;
                        rapor.Append(' ').Append(count.LastValue);
                    }

                    Time.captureDeltaTime = 0.05f;
                    float r0 = Time.realtimeSinceStartup;
                    while (_s.Controller.SpecialWeaponActive && Time.realtimeSinceStartup - r0 < 60f) yield return null;
                    Time.captureDeltaTime = 0f;
                    for (int i = 0; i < 3; i++) yield return null;
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                UnityEngine.Profiling.Profiler.logFile = "";
                count.Dispose();
            }

            Debug.Log("[DD-PROBE] wp104.pickupRaw raw=" + raw + rapor);
        }

        private const int AlimKare = 15;

        /// <summary>What may have just been built: the wall buy stations, the zombies in the scene, the run phase / round, the frame.</summary>
        private static string Durum()
        {
            DD.Prototype.Weapons.WallBuyDirector wb = DD.Prototype.Weapons.WallBuyDirector.Instance;
            DD.Prototype.Rounds.RunDirector run = DD.Prototype.Rounds.RunDirector.Instance;
            return "f" + Time.frameCount + " wallBuyStations=" + (wb != null ? wb.Stations.Count.ToString() : "-") +
                   " zombies=" + Object.FindObjectsByType<DD.Prototype.Enemies.ZombieEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length +
                   " active=" + Object.FindObjectsByType<DD.Prototype.Enemies.ZombieEnemy>(FindObjectsSortMode.None).Length +
                   " phase=" + (run != null ? run.Phase.ToString() : "-") + " round=" + (run != null ? run.Round.ToString() : "-");
        }

        private static readonly List<KeyValuePair<int, string>> s_olaylar = new List<KeyValuePair<int, string>>();

        private static void Olay(string ad)
        {
            if (s_olaylar.Count < 400) s_olaylar.Add(new KeyValuePair<int, string>(Time.frameCount, ad));
        }

        private static string Satir(ProfilerRecorder count, ProfilerRecorder bytes, List<ProfilerRecorder> rec)
        {
            var sb = new StringBuilder();
            int onceki = Time.frameCount - 1;
            sb.Append("[f").Append(onceki);
            foreach (KeyValuePair<int, string> o in s_olaylar) if (o.Key == onceki) sb.Append(' ').Append(o.Value);
            sb.Append("] ");
            sb.Append("alloc=").Append(count.LastValue).Append('/').Append(bytes.LastValue).Append("B dt=").Append((Time.unscaledDeltaTime * 1000f).ToString("F1"))
              .Append("ms compiling=").Append(UnityEditor.ShaderUtil.anythingCompiling ? 1 : 0);
            for (int i = 0; i < rec.Count; i++)
            {
                ProfilerRecorder r = rec[i];
                if (!r.Valid) continue;
                long ns = r.LastValue;   // SumAllSamplesInFrame: the marker's total time in the last frame
                if (ns <= 0) continue;
                sb.Append(' ').Append(Isaretciler[i]).Append('=').Append((ns / 1e6).ToString("F2")).Append("ms");
            }

            return sb.ToString();
        }
    }
}
#endif
