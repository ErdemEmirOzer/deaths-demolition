#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 r2 (C-00 30.09) · the FP arms decision evidence, dev builds only: <c>-ddFpArmsCapture &lt;folder&gt;</c> on a
    /// <c>-ddDirectToRun -ddCameraMode fp</c> run with the profile / UI data / telemetry redirected (<c>-ddProfilePath</c>,
    /// <c>-ddUiDataRoot</c>, <c>-ddTelemetryDir</c> with values — it checks the live redirect state and otherwise stays inert: no capture,
    /// no quit (a quit saves the profile)). Spawning stops, the arms
    /// switch on for this run only, and per weapon (ar_1, ar_2, smg_1, smg_2) at rest and in ADS (the weight forced to 1) it freezes
    /// time and grabs three screens: the frame as played (arms + HUD → <c>&lt;key&gt;-&lt;rest|ads&gt;.png</c>), the same without the HUD
    /// and without HUD and arms — the arm mask (arms-on vs arms-off) and the HUD mask (HUD-on vs HUD-off) give the arm pixel share of the
    /// screen and the arm pixels under the HUD. summary.json adds the hand / rest / elbow numbers. Nothing else is written.
    /// </summary>
    public sealed class FirstPersonArmsCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddFpArmsCapture";
        private const int DiffThreshold = 10;   // per channel, 0..255 (frozen time: the toggled layer is the only change)

        private static readonly ClassicWeaponId[] Order = { ClassicWeaponId.AssaultRifle, ClassicWeaponId.Ar2, ClassicWeaponId.AutoSmg, ClassicWeaponId.Smg2 };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private string _folder;
        private readonly StringBuilder _json = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string folder = null, telemetry = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Flag) folder = args[i + 1];
                else if (args[i] == "-ddTelemetryDir") telemetry = args[i + 1];
            }

            if (folder == null) return;
            if (!Value(folder) || !Value(telemetry))
            {
                Debug.Log("[FpArmsCapture] refused: -ddFpArmsCapture <folder> and -ddTelemetryDir <dir> need values — inert (no capture, no quit)");
                return;
            }

            var go = new GameObject("FirstPersonArmsCaptureAgent");
            DontDestroyOnLoad(go);
            go.AddComponent<FirstPersonArmsCaptureAgent>()._folder = folder;
        }

        private static bool Value(string v) => !string.IsNullOrEmpty(v) && !v.StartsWith("-", StringComparison.Ordinal);

        private IEnumerator Start()
        {
            // the live redirect state, not the flags: the profile and the UI files must already point away from the user's data
            if (!ProfileStore.HasPathOverride || !UiDataPaths.IsProcessLocal)
            {
                Debug.Log("[FpArmsCapture] refused: profile / UI data not redirected (-ddProfilePath <file>, -ddUiDataRoot <dir>) — inert (no capture, no quit)");
                Destroy(gameObject);
                yield break;
            }

            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((WeaponRegistry.ActiveController == null || PlayerRegistry.Primary == null) && Time.realtimeSinceStartup < deadline) yield return null;
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerSkinnedVisual visual = rig != null ? rig.GetComponentInChildren<PlayerSkinnedVisual>(true) : null;
            if (rig == null || visual == null || CameraModeService.Current != CameraMode.FirstPerson || CameraView.ViewmodelAnchor == null)
            {
                Debug.Log("[FpArmsCapture] no player / skinned visual / first-person camera (-ddCameraMode fp) — abort");
                Application.Quit(3);   // the profile is redirected (checked above)
                yield break;
            }

            rig.Health.DiagnosisInvulnerable = true;   // evidence only
            StopSpawning();
            PlayerSkinnedVisual.FirstPersonV3Arms = true;   // this dev run only (the shipped default is untouched on disk)
            WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.Smg2);   // a real equip after the switch (ar_1 re-selected would not re-equip)
            yield return new WaitForSeconds(2f);
            _json.Append("{\n  \"schema\": \"wp071.fp-arms-capture.v1\",\n  \"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height).Append("],\n  \"weapons\": [");
            bool first = true;
            foreach (ClassicWeaponId id in Order)
            {
                WeaponDefinition def = Arsenal.Get(id);
                string key = def != null ? def.weaponId : id.ToString();
                StopSpawning();
                if (!WeaponQaHooks.TrySetClassicWeapon(id)) { Debug.Log("[FpArmsCapture] " + key + " not equippable"); continue; }
                FirstPersonArms arms = visual.FirstPersonArms;
                float until = Time.realtimeSinceStartup + 6f;
                while ((arms == null || !arms.Visible || !arms.FrameCalibrated) && Time.realtimeSinceStartup < until) { arms = visual.FirstPersonArms; yield return null; }
                if (arms == null || !arms.Visible || !arms.FrameCalibrated)
                {
                    _json.Append(first ? "\n    " : ",\n    ").Append("{\"weapon\": \"").Append(key).Append("\", \"skipped\": \"arms not visible / not calibrated in 6 s\"}");
                    first = false;
                    Debug.Log("[FpArmsCapture] " + key + " skipped: arms not visible / calibrated");
                    continue;
                }

                yield return new WaitForSeconds(1.2f);   // switch-in and the hand IK blend
                foreach (bool ads in new[] { false, true })
                {
                    arms.SetAds(ads);
                    yield return new WaitForSeconds(0.6f);
                    yield return Grab(arms, def, key + (ads ? "-ads" : "-rest"), first);
                    first = false;
                }

                arms.SetAds(false);
            }

            _json.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(_folder, "summary.json"), _json.ToString());
            Debug.Log("[FpArmsCapture] done → " + _folder);
            Application.Quit(0);
        }

        private IEnumerator Grab(FirstPersonArms arms, WeaponDefinition def, string name, bool first)
        {
            float scale = Time.timeScale;
            Color32[] full, noHud, bare;
            List<Canvas> hud = null;
            List<Renderer> armRenderers = null;
            Time.timeScale = 0f;   // the world holds still; the arms' LateUpdate keeps them placed
            try
            {
                yield return new WaitForEndOfFrame();
                full = Capture(Path.Combine(_folder, name + ".png"));
                hud = SetHud(false);
                yield return new WaitForEndOfFrame();
                noHud = Capture(null);
                armRenderers = SetArms(arms, false);
                yield return new WaitForEndOfFrame();
                bare = Capture(null);
            }
            finally
            {
                if (armRenderers != null) foreach (Renderer r in armRenderers) r.enabled = true;
                if (hud != null) foreach (Canvas c in hud) c.enabled = true;
                Time.timeScale = scale;
            }

            int armPx = 0, hudPx = 0, overlap = 0;
            for (int i = 0; i < full.Length; i++)
            {
                bool arm = Differs(noHud[i], bare[i]), inHud = Differs(full[i], noHud[i]);
                if (arm) armPx++;
                if (inHud) hudPx++;
                if (arm && inHud) overlap++;
            }

            Transform anchor = CameraView.ViewmodelAnchor;
            Camera vm = anchor != null ? anchor.GetComponentInParent<Camera>() : null;
            Animator a = arms.Animator;
            Vector3 le = vm != null ? vm.WorldToViewportPoint(a.GetBoneTransform(HumanBodyBones.LeftLowerArm).position) : Vector3.zero;
            float rest = float.NaN;
            if (anchor != null && arms.WeaponFrame != null)
            {
                var pose = def.FpViewPose;
                Vector3 root = arms.WeaponFrame.position - arms.WeaponFrame.rotation * (def.GripOffset * pose.scale);
                rest = Vector3.Distance(anchor.InverseTransformPoint(root), pose.position);
            }

            float n = full.Length;
            _json.Append(first ? "\n    " : ",\n    ").Append("{\"frame\": \"").Append(name).Append(".png\", \"armPct\": ").Append((armPx / n * 100f).ToString("F2", Inv))
                 .Append(", \"hudPx\": ").Append(hudPx).Append(", \"armUnderHudPx\": ").Append(overlap)
                 .Append(", \"rightCm\": ").Append((arms.LastRightError * 100f).ToString("F2", Inv)).Append(", \"leftCm\": ").Append((arms.LastLeftError * 100f).ToString("F2", Inv))
                 .Append(", \"restCm\": ").Append(float.IsNaN(rest) ? "null" : (rest * 100f).ToString("F2", Inv)).Append(", \"leftElbowY\": ").Append(((1f - le.y) * Screen.height).ToString("F0", Inv)).Append('}');
            Debug.Log("[FpArmsCapture] " + name + " arm " + (armPx / n * 100f).ToString("F2", Inv) + " % underHud " + overlap + " px");
        }

        private static bool Differs(Color32 x, Color32 y) =>
            Mathf.Abs(x.r - y.r) > DiffThreshold || Mathf.Abs(x.g - y.g) > DiffThreshold || Mathf.Abs(x.b - y.b) > DiffThreshold;

        private static Color32[] Capture(string file)
        {
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (file != null) File.WriteAllBytes(file, tex.EncodeToPNG());
            Color32[] px = tex.GetPixels32();
            Destroy(tex);
            return px;
        }

        private static List<Canvas> SetHud(bool on)
        {
            var changed = new List<Canvas>();
            foreach (Canvas c in FindObjectsOfType<Canvas>())
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || c.enabled == on) continue;
                c.enabled = on;
                changed.Add(c);
            }

            return changed;
        }

        private static List<Renderer> SetArms(FirstPersonArms arms, bool on)
        {
            var changed = new List<Renderer>();
            foreach (Renderer r in arms.Animator.GetComponentsInChildren<Renderer>())
            {
                if (r.enabled == on) continue;
                r.enabled = on;
                changed.Add(r);
            }

            return changed;
        }

        private static void StopSpawning()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir == null) return;
            dir.ForceActiveTarget(0, false);   // public API (no reflection)
            dir.ClearAllEnemies();
        }
    }
}
#endif
