using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · AD evidence for the Special VFX: <c>-ddVfxSpecialCapture &lt;folder&gt;</c> rides along a run (QA bot or direct)
    /// and stages, at a readable ground point (FP 3.5 m, TP 5 m, top-down 3 m ahead of the player): the Raiju discharge ring, a
    /// slow orb in flight, a three-hop chain (between the nearest zombies when there are three, else between points), the
    /// fizzle; the Namekuji blob in flight and a puddle at 1 s / 3 s — each framed as <c>&lt;mode&gt;-special-&lt;name&gt;.png</c>
    /// with the systems' counters logged, then quits. Opt-in only.
    /// </summary>
    public sealed class VfxSpecialCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxSpecialCapture";
        private string _folder;
        private string _mode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != Flag) continue;
                var go = new GameObject("VfxSpecialCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxSpecialCaptureAgent>()._folder = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((WeaponRegistry.ActiveController == null || Camera.main == null || ArcSystem.Instance == null || GelSystem.Instance == null)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;   // evidence only: the 21 s puddle outlives a death
            _mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";
            ArcSystem arc = ArcSystem.Instance;
            GelSystem gel = GelSystem.Instance;

            // ---- Raiju
            if (Aim(out Vector3 p, out Vector3 fwd))
            {
                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Fired, "sw_arc_orb", p + Vector3.up * 1.2f, fwd, 0f, 0f, 0));
                yield return Frame("raiju-discharge", 2);
                yield return new WaitForSeconds(0.8f);

                Spawn(ProjectileKind.Orb, 0, p + Vector3.up * 1.2f - fwd * 1.5f, fwd, 2.5f);
                yield return new WaitForSeconds(0.35f);
                yield return Frame("raiju-orb", 0);
                ProjectileSystem.Clear();
                yield return new WaitForSeconds(0.5f);

                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                Vector3 a = p + Vector3.up * 1.1f - side * 1.8f, b = p + Vector3.up * 1.1f, c = p + Vector3.up * 1.1f + side * 1.8f + fwd * 1.2f;
                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Hit, "sw_arc_orb", a - fwd * 2f, a - (a - fwd * 2f), 6f, 1.2f, 0, default, 1f, 1f, 0));
                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Hit, "sw_arc_orb", a, b - a, 6f, 1.2f, 1, default, 1f, 1f, 0));
                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Hit, "sw_arc_orb", b, c - b, 6f, 1.2f, 2, default, 1f, 1f, 0));
                yield return Frame("raiju-chain", 2);
                yield return new WaitForSeconds(0.25f);
                yield return Frame("raiju-chain-0.3s", 0);
                yield return new WaitForSeconds(1.2f);

                arc.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.FieldEnded, "sw_arc_orb", p + Vector3.up * 1.0f, Vector3.forward, 0f, 0.3f, 0));
                yield return Frame("raiju-fizzle-burn", 3);
                yield return new WaitForSeconds(1f);
            }

            // ---- Namekuji
            if (Aim(out Vector3 q, out Vector3 fwd2))
            {
                Spawn(ProjectileKind.Gel, 1, q + Vector3.up * 1.4f - fwd2 * 1.2f, fwd2, 2f);
                yield return new WaitForSeconds(0.25f);
                yield return Frame("gel-blob", 0);
                ProjectileSystem.Clear();

                gel.OnUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.FieldSpawned, "sw_gel_puddle", q, Vector3.up, 1.6f, 25f, 900));
                yield return new WaitForSeconds(1f);
                yield return Frame("gel-puddle-1s", 0);
                yield return new WaitForSeconds(2f);
                yield return Frame("gel-puddle-3s", 0);
                yield return new WaitForSeconds(18f);                  // 21 s of 25: the drying phase (AD B6 MUST 5)
                yield return Frame("gel-puddle-21s-drying", 0);
            }

            Debug.Log("[VfxSpecialCapture] done");
            Application.Quit(0);
        }

        private static void Spawn(ProjectileKind kind, int special, Vector3 at, Vector3 dir, float speed)
        {
            var shot = new ShotRequest { Origin = at, Direction = dir, Damage = 0f, Range = 30f, DrawTracer = false };
            var launch = new ProjectileLaunch
            {
                WeaponId = (ClassicWeaponId)(100 + special), Kind = kind, Caliber = WeaponCaliber.Heavy, Speed = speed, Gravity = 0f,
                MaxRange = 30f, MaxLifetime = 3f, SweepRadius = 0.1f, FalloffStart = 30f, FalloffEnd = 30f, FalloffMinShare = 1f,
                PenetrationDamageKeep = 1f, HeadMultiplier = 1f, ArmorMultiplier = 1f
            };
            ProjectileSystem.Spawn(shot, launch);
        }

        // A ground point at a readable distance (as the impact agent): FP 3.5 m ahead and left of the viewmodel, TP 5 m, top-down 3 m.
        private static bool Aim(out Vector3 point, out Vector3 forward)
        {
            Camera cam = Camera.main;
            point = forward = Vector3.zero;
            if (cam == null) return false;
            Transform c = cam.transform;
            Vector3 flat = Vector3.ProjectOnPlane(c.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(c.up, Vector3.up);
            flat.Normalize();
            forward = flat;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 origin;
            switch (CameraModeService.Current)
            {
                case CameraMode.FirstPerson: origin = c.position + flat * 3.5f - right * 0.7f; break;
                case CameraMode.ThirdPerson: origin = c.position + flat * 5f + right * 0.9f; break;
                default:
                    Transform player = WeaponRegistry.ActiveController != null ? WeaponRegistry.ActiveController.transform : c;
                    origin = player.position + flat * 3f + right * 0.8f;
                    break;
            }

            int mask = (1 << 0) | (1 << DD.Prototype.Core.PrototypeLayers.Environment);
            if (!Physics.Raycast(origin + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        private IEnumerator Frame(string name, int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_folder, _mode + "-special-" + name + ".png");
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(file, frame.EncodeToPNG());
            Destroy(frame);
            ArcSystem arc = ArcSystem.Instance;
            GelSystem gel = GelSystem.Instance;
            Debug.Log("[VfxSpecialCapture] " + file + " arcs=" + arc.LiveArcs + " branches=" + arc.LiveBranches + " orbs=" + arc.OrbsDrawn + " burns=" + arc.LiveBurns
                + " shocks=" + arc.LiveShocks + " puddles=" + gel.LivePuddles + " blobs=" + gel.BlobsDrawn + " drops=" + gel.LiveDrops + " tier=" + VfxTier.Current);
        }
    }
}
