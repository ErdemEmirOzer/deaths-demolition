#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-095: opt-in, DEV-only illustrative feel take. DD_FEEL_EVIDENCE=1 plus td-capture video is required.
    /// Uses actual weapon/damage/death paths; never publishes FeelEvents itself. The population is shortened explicitly
    /// through the F1 ledger's debug drain and debug kills. This is a staged illustration, never a gameplay/perf PASS.
    /// S-01 owns the capture implementation; this separate agent only observes its sync.json clock.
    /// </summary>
    public sealed class FeelEvidenceAgent : MonoBehaviour
    {
        [Serializable] private sealed class Anchor { public double realtimeSinceStartup; }
        [Serializable] private sealed class Sync { public Anchor t0; }
        [Serializable] private sealed class Summary
        {
            public string kind = "staged-dev-feel-illustration";
            public bool gameplayPass = false;
            public bool performancePass = false;
            public int rifleShots, rocketShots, explosions, lowHealthEntries, lastZombieEvents;
            public int debugCleanupKills;
            public bool debugFinalKill, poolDrained;
            public double videoT0;
            public string error;
        }
        private readonly Summary _summary = new Summary();
        private StreamWriter _events;
        private string _out;
        private double _t0;
        private ZombieEnemy _lastCandidate;
        private bool _armed;

        private static string Arg(string[] args, string flag)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Arm()
        {
            if (Environment.GetEnvironmentVariable("DD_FEEL_EVIDENCE") != "1") return;
            string[] args = Environment.GetCommandLineArgs();
            string path = Arg(args, "-ddTdCapture");
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || Arg(args, "-ddTdCaptureMode") != "video") return;
            var go = new GameObject("WP095_FeelEvidence");
            DontDestroyOnLoad(go);
            go.AddComponent<FeelEvidenceAgent>()._out = path;
        }

        private IEnumerator Start()
        {
            string syncPath = Path.Combine(_out, "video", "sync.json");
            float deadline = Time.realtimeSinceStartup + 180f;
            while (!File.Exists(syncPath) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!File.Exists(syncPath)) { _summary.error = "video sync did not start"; Save(); yield break; }
            try
            {
                Sync sync = JsonUtility.FromJson<Sync>(File.ReadAllText(syncPath));
                if (sync == null || sync.t0 == null || sync.t0.realtimeSinceStartup <= 0d) throw new InvalidDataException("video t0 missing");
                _summary.videoT0 = _t0 = sync.t0.realtimeSinceStartup;
                _events = new StreamWriter(Path.Combine(_out, "feel-events.csv")) { AutoFlush = true };
                _events.WriteLine("video_seconds,frame,event,detail,time_scale,health_ratio");
            }
            catch (Exception e) { _summary.error = e.Message; Save(); yield break; }
            if (CameraModeService.Current != CameraMode.TopDown) { _summary.error = "TopDown required"; Save(); yield break; }
            FeelEvents.ShotFired += OnShot;
            FeelEvents.Explosion += OnExplosion;
            FeelEvents.LowHealth += OnLowHealth;
            FeelEvents.LastZombie += OnLastZombie;
            _armed = true;
            Record("scenario-start", "staged DEV; not a gameplay or perf test");
            Select(ClassicWeaponId.AssaultRifle);
            yield return Until(15d);
            Select(ClassicWeaponId.Rl1); // QA bot fires the real equipped rocket at its real target.
            yield return Until(27d);
            Select(ClassicWeaponId.AssaultRifle);
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig != null && rig.Health != null)
            {
                rig.Health.DiagnosisInvulnerable = false;
                try
                {
                    float damage = Mathf.Max(0f, rig.Health.Current - rig.Health.Max * .20f);
                    if (damage > 0f) rig.Health.ApplyDamage(damage, rig.transform.position - rig.transform.forward * 2f, "WP095.FeelEvidence.LowHealth");
                }
                finally { rig.Health.DiagnosisInvulnerable = true; }
                Record("staged-damage", "real ApplyDamage to 20 percent");
            }
            yield return Until(39d);
            if (rig != null && rig.Health != null) { rig.Health.HealToFull(); Record("staged-heal", "real HealToFull"); }
            PrepareFinalEnemy();
            yield return Until(53d);
            // If the bot cannot reach the retained enemy in this bounded take, disclose the F1 kill fallback explicitly.
            if (_summary.lastZombieEvents == 0 && _lastCandidate != null && _lastCandidate.IsAlive)
            {
                _summary.debugFinalKill = true;
                Record("staged-final-debug-kill", "bot did not finish retained enemy by 53s");
                _lastCandidate.Kill();
            }
            yield return Until(56d);
            Record("scenario-end", "coverage is observations only");
            Save();
        }

        private IEnumerator Until(double seconds)
        {
            while (Time.realtimeSinceStartupAsDouble - _t0 < seconds) yield return null;
        }

        private void Select(ClassicWeaponId weapon)
        {
            bool ok = WeaponQaHooks.TrySetClassicWeapon(weapon);
            Record("staged-equip", weapon + " success=" + ok);
            if (!ok) _summary.error = "weapon equip failed: " + weapon;
        }

        private void PrepareFinalEnemy()
        {
            EnemySpawnDirector spawn = EnemySpawnDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            if (spawn == null || rig == null) { _summary.error = "final enemy missing director/player"; return; }
            // Read the existing F1 debug ledger; no shipping API or spawn implementation is modified.
            FieldInfo field = typeof(EnemySpawnDirector).GetField("_ledger", BindingFlags.Instance | BindingFlags.NonPublic);
            RoundLedger ledger = field != null ? field.GetValue(spawn) as RoundLedger : null;
            var living = new List<ZombieEnemy>();
            float nearest = float.PositiveInfinity;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsAlive) continue;
                living.Add(z);
                float sq = (z.transform.position - rig.transform.position).sqrMagnitude;
                if (sq < nearest) { nearest = sq; _lastCandidate = z; }
            }
            if (ledger == null || _lastCandidate == null) { _summary.error = "final stage has no ledger/live enemy"; return; }
            spawn.ForceActiveTarget(0, false);
            ledger.DebugDrainPool();
            _summary.poolDrained = true;
            Record("staged-pool-drain", "unspawned pool drained; retained nearest of " + living.Count);
            foreach (ZombieEnemy z in living)
                if (z != _lastCandidate && z.IsAlive) { _summary.debugCleanupKills++; z.Kill(); }
            Record("final-enemy-ready", "remainingPool=" + spawn.RemainingInPool + " alive=" + spawn.ActiveCount);
        }

        private void OnShot(FeelShot e)
        {
            if (e.Special) return;
            if (e.Classic == ClassicWeaponId.AssaultRifle) _summary.rifleShots++;
            if (e.Classic == ClassicWeaponId.Rl1) _summary.rocketShots++;
            Record("shot", e.Classic + " group=" + e.GroupId);
        }
        private void OnExplosion(FeelExplosion e) { _summary.explosions++; Record("explosion", e.Source + " group=" + e.GroupId); }
        private void OnLowHealth(FeelLowHealth e) { if (e.Entered) _summary.lowHealthEntries++; Record("low-health", "entered=" + e.Entered + " ratio=" + e.HealthRatio.ToString("F3", CultureInfo.InvariantCulture)); }
        private void OnLastZombie(FeelLastZombie e) { _summary.lastZombieEvents++; Record("last-zombie", "round=" + e.Round + " debugFinal=" + _summary.debugFinalKill); }
        private void Record(string kind, string detail)
        {
            if (_events == null) return;
            PlayerRig rig = PlayerRegistry.Primary;
            float health = rig != null && rig.Health != null ? rig.Health.Normalized : -1f;
            _events.WriteLine((Time.realtimeSinceStartupAsDouble - _t0).ToString("F4", CultureInfo.InvariantCulture) + "," + Time.frameCount + "," + kind + "," + detail.Replace(',', ';') + "," + Time.timeScale.ToString("F4", CultureInfo.InvariantCulture) + "," + health.ToString("F4", CultureInfo.InvariantCulture));
        }
        private void Save()
        {
            if (!string.IsNullOrEmpty(_out)) File.WriteAllText(Path.Combine(_out, "feel-scenario.json"), JsonUtility.ToJson(_summary, true));
        }
        private void OnApplicationQuit() { Save(); }
        private void OnDestroy()
        {
            if (_armed)
            {
                FeelEvents.ShotFired -= OnShot; FeelEvents.Explosion -= OnExplosion;
                FeelEvents.LowHealth -= OnLowHealth; FeelEvents.LastZombie -= OnLastZombie;
            }
            if (_events != null) _events.Dispose();
        }
    }
}
#endif
