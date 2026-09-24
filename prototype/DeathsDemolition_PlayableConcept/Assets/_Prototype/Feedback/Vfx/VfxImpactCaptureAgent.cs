using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B2 · AD evidence for impacts: <c>-ddVfxImpactCapture &lt;folder&gt;</c> rides along a run (QA bot or direct) and
    /// (1) stages the §1.4 matrix where the camera's centre ray meets the world — a three-round burst per surface × calibre
    /// (legacy card + hole, then the <see cref="ImpactSystem"/> layer), a grazing metal hit, a pierced body's exit spray
    /// against the surface and a head mist — each framed three frames later as <c>&lt;mode&gt;-stage-&lt;name&gt;.png</c>;
    /// (2) grabs up to six live frames two frames after real <see cref="ProjectileEvents.Impact"/>s
    /// (<c>&lt;mode&gt;-live-&lt;n&gt;.png</c>); logs the counters with each, then quits. Opt-in only; writes nothing else.
    /// </summary>
    public sealed class VfxImpactCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxImpactCapture";
        private string _folder;
        private int _impactFrame = -1;
        private bool _impactFlesh;

        private struct Stage
        {
            public string Name;
            public SurfaceKind Surface;
            public WeaponCaliber Calibre;
            public bool Graze;
            public Stage(string name, SurfaceKind surface, WeaponCaliber calibre, bool graze = false)
            {
                Name = name; Surface = surface; Calibre = calibre; Graze = graze;
            }
        }

        private static readonly Stage[] Stages =
        {
            new Stage("stone-light", SurfaceKind.Stone, WeaponCaliber.Light),
            new Stage("stone-medium", SurfaceKind.Stone, WeaponCaliber.Medium),
            new Stage("stone-heavy", SurfaceKind.Stone, WeaponCaliber.Heavy),
            new Stage("wood-medium", SurfaceKind.Wood, WeaponCaliber.Medium),
            new Stage("metal-medium", SurfaceKind.Metal, WeaponCaliber.Medium),
            new Stage("metal-heavy", SurfaceKind.Metal, WeaponCaliber.Heavy),
            new Stage("metal-graze", SurfaceKind.Metal, WeaponCaliber.Medium, true),
            new Stage("dirt-medium", SurfaceKind.Dirt, WeaponCaliber.Medium),
            new Stage("water-medium", SurfaceKind.Water, WeaponCaliber.Medium),
            new Stage("foliage-medium", SurfaceKind.Foliage, WeaponCaliber.Medium),
            new Stage("paper-medium", SurfaceKind.Paper, WeaponCaliber.Medium),
        };

        private void OnEnable() { ProjectileEvents.Impact += OnImpact; }
        private void OnDisable() { ProjectileEvents.Impact -= OnImpact; }

        private void OnImpact(ProjectileImpactInfo info)
        {
            if (_impactFrame < 0 && info.Kind < ProjectileKind.Rocket)
            {
                _impactFrame = Time.frameCount;
                _impactFlesh = info.Flesh;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != Flag) continue;
                var go = new GameObject("VfxImpactCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxImpactCaptureAgent>()._folder = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((WeaponRegistry.ActiveController == null || Camera.main == null || ImpactSystem.Instance == null || !HitFeedbackDirector.Instance.Vfx.IsReady)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(3f);
            string mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            ImpactSystem impacts = ImpactSystem.Instance;

            // ---- 1 · staged matrix at the camera's centre ray
            foreach (Stage s in Stages)
            {
                if (!Aim(out Vector3 point, out Vector3 normal, out Vector3 dir)) { yield return null; continue; }
                Vector3 side = Vector3.Cross(normal, Vector3.up).sqrMagnitude > 1e-3f ? Vector3.Cross(normal, Vector3.up).normalized : Vector3.right;
                Vector3 shot = s.Graze ? (Vector3.ProjectOnPlane(dir, normal).normalized - normal * 0.09f).normalized : dir;
                for (int r = 0; r < 3; r++)
                {
                    Vector3 at = point + side * ((r - 1) * 0.18f);
                    fx.ReportSurfaceImpact(at, normal, s.Surface, 1f);
                    impacts.Play(new ProjectileImpactInfo(1, 0, ClassicWeaponId.AssaultRifle, ProjectileKind.Bullet, s.Calibre, at, normal, shot, s.Surface, 200f, false, false, false, true, 0));
                }

                yield return Frame(mode + "-stage-" + s.Name, 3);
                yield return new WaitForSeconds(0.8f);
            }

            if (Aim(out Vector3 wall, out Vector3 wallNormal, out Vector3 aim))
            {
                // A body 1.2 m in front of the surface: the round passes through, sprays out of its back onto the surface.
                Vector3 body = wall - aim * 1.2f;
                impacts.Play(new ProjectileImpactInfo(1, 0, ClassicWeaponId.AssaultRifle, ProjectileKind.Bullet, WeaponCaliber.Heavy, body, -aim, aim, SurfaceKind.Flesh, 200f, true, false, false, false, 0));
                fx.Vfx.Blood.PlayHeadMist(body + Vector3.up * 0.3f);
                yield return Frame(mode + "-stage-exit-spray-mist", 4);
                yield return new WaitForSeconds(0.8f);
            }

            // ---- 2 · live impacts (the QA bot fights; nothing arrives on a direct run and the phase times out)
            int live = 0;
            float liveDeadline = Time.realtimeSinceStartup + 90f;
            float last = -10f;
            _impactFrame = -1;
            while (live < 6 && Time.realtimeSinceStartup < liveDeadline)
            {
                if (_impactFrame >= 0 && (Time.frameCount - _impactFrame > 3 || Time.unscaledTime - last < 0.4f)) _impactFrame = -1;
                if (_impactFrame >= 0 && Time.frameCount - _impactFrame >= 2)
                {
                    bool flesh = _impactFlesh;
                    _impactFrame = -1;
                    yield return Frame(mode + "-live-" + live + (flesh ? "-flesh" : "-world"), 0);
                    live++;
                    last = Time.unscaledTime;
                }

                yield return null;
            }

            Debug.Log("[VfxImpactCapture] done live=" + live);
            Application.Quit(0);
        }

        // A ground point at a readable distance: FP 3.5 m ahead and left of the viewmodel, TP 5 m, top-down 3 m ahead of the player.
        private static bool Aim(out Vector3 point, out Vector3 normal, out Vector3 dir)
        {
            Camera cam = Camera.main;
            point = normal = dir = Vector3.zero;
            if (cam == null) return false;
            Transform c = cam.transform;
            Vector3 flat = Vector3.ProjectOnPlane(c.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(c.up, Vector3.up);
            flat.Normalize();
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
            normal = hit.normal;
            dir = (point - c.position).normalized;
            return true;
        }

        private IEnumerator Frame(string name, int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_folder, name + ".png");
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(file, frame.EncodeToPNG());
            Destroy(frame);
            ImpactSystem s = ImpactSystem.Instance;
            MuzzleSystem m = MuzzleSystem.Instance;
            Debug.Log("[VfxImpactCapture] " + file + " impacts=" + s.Impacts + " skipped=" + s.Skipped + " debrisAlive=" + s.DebrisAlive + " sparksAlive=" + s.SparksAlive
                + " ricochets=" + s.Ricochets + " lights=" + (m != null ? m.LitCount : -1) + " exitSprays=" + HitFeedbackDirector.Instance.Vfx.Blood.ExitSprays
                + " mists=" + HitFeedbackDirector.Instance.Vfx.Blood.Mists + " tier=" + VfxTier.Current);
        }
    }
}
