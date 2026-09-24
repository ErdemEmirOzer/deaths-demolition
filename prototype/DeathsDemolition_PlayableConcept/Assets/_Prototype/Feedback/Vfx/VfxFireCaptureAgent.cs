using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 · AD evidence for the fire VFX: <c>-ddVfxFireCapture &lt;folder&gt;</c> rides along a QA bot run
    /// (<c>-ddQaBot … -ddCameraMode topdown|tp|fp</c>) and, while rounds are in flight, grabs frames per classic weapon
    /// (AR → SMG → shotgun → LMG → sniper, three each, two frames after a round spawns, ≥ 0.25 s apart) named <c>&lt;mode&gt;-&lt;weapon&gt;-&lt;n&gt;.png</c>, logs the ribbon /
    /// light / casing counters with each, then quits. Opt-in only; writes nothing else.
    /// </summary>
    public sealed class VfxFireCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxFireCapture";
        private static readonly ClassicWeaponId[] Weapons = { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Lmg1, ClassicWeaponId.Sr1 };
        private string _folder;
        private int _spawnedFrame = -1;

        private void OnEnable() { ProjectileEvents.Spawned += OnSpawned; }
        private void OnDisable() { ProjectileEvents.Spawned -= OnSpawned; }
        private void OnSpawned(ProjectileSpawnInfo info) { if (_spawnedFrame < 0) _spawnedFrame = Time.frameCount; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != Flag) continue;
                var go = new GameObject("VfxFireCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxFireCaptureAgent>()._folder = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            foreach (ClassicWeaponId weapon in Weapons)
            {
                WeaponController active = null;
                while ((active = WeaponRegistry.ActiveController) == null && Time.realtimeSinceStartup < deadline) yield return null;
                if (active == null) break;
                if (active.Classic == null || active.Classic.Id != weapon)
                {
                    active.EquipClassic(weapon);
                    yield return new WaitForSeconds(1.2f);
                }

                int shots = 0;
                float last = -10f;
                while (shots < 3 && Time.realtimeSinceStartup < deadline)
                {
                    // Timed to the shot: two frames after a round spawns the flash is still up (0.08–0.15 s) and the round
                    // is a few metres out — a frame taken at an arbitrary moment misses both.
                    if (_spawnedFrame >= 0 && (Time.frameCount - _spawnedFrame > 3 || Time.unscaledTime - last < 0.25f))
                    {
                        _spawnedFrame = -1;   // too old, or too soon after the last frame: wait for the next shot
                    }

                    if (_spawnedFrame >= 0 && Time.frameCount - _spawnedFrame >= 2)
                    {
                        _spawnedFrame = -1;
                        yield return new WaitForEndOfFrame();
                        string mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";
                        string file = Path.Combine(_folder, mode + "-" + weapon + "-" + shots + ".png");
                        Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                        File.WriteAllBytes(file, frame.EncodeToPNG());
                        Destroy(frame);
                        TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
                        MuzzleSystem m = MuzzleSystem.Instance;
                        Debug.Log("[VfxFireCapture] " + file + " projectiles=" + ProjectileSystem.LiveCount + " core=" + r.DrawnCore + " glow=" + r.DrawnGlow
                            + " glint=" + r.DrawnGlint + " tailouts=" + r.TailOuts + " lights=" + (m != null ? m.LitCount : -1) + " smoke=" + (m != null ? m.SmokeAlive : -1)
                            + " interim=" + ProjectileDriver.DrawInterimTrails + " tier=" + VfxTier.Current);
                        shots++;
                        last = Time.unscaledTime;
                    }

                    yield return null;
                }
            }

            Debug.Log("[VfxFireCapture] done");
            Application.Quit(0);
        }
    }
}
