using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · Fixed-camera captures and pixel comparison for the art pipeline.
    ///
    /// RUNTIME ON PURPOSE
    /// ------------------
    /// The reference captures are taken from a Player build (that is the only render that
    /// counts as evidence - the Editor Game view differs in MSAA, HDR and resolution), so
    /// nothing in here may touch UnityEditor. The PlayMode tests and PilotPerfProbe call
    /// the same code, which is what makes "editor vs build" a comparable pair at all.
    ///
    /// WHAT THE POSES ARE
    /// ------------------
    /// The five gameplay poses copy the prototype camera rig exactly: 15 m up, 13 m back,
    /// vertical FOV 42, i.e. a 49.09° downward pitch looking towards +Z. A capture from
    /// one of them therefore shows what the player sees at that spot, not an artist's
    /// framing. The remaining three re-create the AP-001 Presentation A cameras (final 4k,
    /// evocative, beauty overview) so the URP render can be put next to the Blender one.
    ///
    /// The comparison is deliberately naive - per-pixel absolute difference, no
    /// perceptual metric. It only has to answer "did this frame change at all, and by how
    /// much"; a threshold on the changed-pixel fraction is what the gate reads.
    /// </summary>
    public static class ArtVisualRegression
    {
        private const string LogPrefix = "[ArtPipeline] ";

        /// <summary>Same as the prototype camera rig: 0.3 near so the ground under the lens does not clip.</summary>
        private const float NearClip = 0.3f;

        /// <summary>Past the 138 m fog end from every pose, so nothing is clipped before the fog hides it.</summary>
        private const float FarClip = 300f;

        /// <summary>
        /// Rig offset from focus to camera is (0, 15, -13); the look target is therefore
        /// camera + (0, -15, +13). Kept as one vector so all five gameplay poses agree.
        /// </summary>
        private static readonly Vector3 GameplayLookOffset = new Vector3(0f, -15f, 13f);

        private const float GameplayFov = 42f;

        [Serializable]
        public sealed class CameraPose
        {
            public string name;
            public Vector3 position;
            public Vector3 lookAt;
            public float verticalFov;

            public CameraPose()
            {
            }

            public CameraPose(string name, Vector3 position, Vector3 lookAt, float verticalFov)
            {
                this.name = name;
                this.position = position;
                this.lookAt = lookAt;
                this.verticalFov = verticalFov;
            }
        }

        /// <summary>The fixed capture set. Order is stable; tests and probes index into it by name.</summary>
        public static readonly CameraPose[] StandardPoses =
        {
            Gameplay("gameplay_courtyard", new Vector3(7f, 16f, -13f)),
            Gameplay("gameplay_alternative", new Vector3(12f, 16f, -10f)),
            Gameplay("courtyard_ring_connection", new Vector3(22f, 16f, -12f)),
            Gameplay("sakura_density", new Vector3(30f, 16f, -12f)),
            Gameplay("silhouette_readability", new Vector3(12f, 16f, -13f)),
            new CameraPose("presentation_final4k", new Vector3(29f, 3.4f, 11f), new Vector3(9.3f, 3.4f, 34f), 28f),
            new CameraPose("evocative", new Vector3(29f, 3.4f, 11f), new Vector3(7f, 4.3f, 34f), 46f),
            new CameraPose("beauty_overview", new Vector3(46.04f, 61.40f, -61.39f), new Vector3(16f, 5f, 13.4f), 42f)
        };

        /// <summary>Finds a standard pose by name, or null.</summary>
        public static CameraPose FindPose(string name)
        {
            for (int i = 0; i < StandardPoses.Length; i++)
            {
                if (string.Equals(StandardPoses[i].name, name, StringComparison.Ordinal))
                {
                    return StandardPoses[i];
                }
            }

            return null;
        }

        private static CameraPose Gameplay(string name, Vector3 position)
        {
            return new CameraPose(name, position, position + GameplayLookOffset, GameplayFov);
        }

        // ---------------------------------------------------------------- capture

        /// <summary>
        /// Renders the scene from <paramref name="pose"/> with a temporary camera into a
        /// new readable RGB24 texture. The caller owns the texture and must destroy it.
        ///
        /// Must be called from normal script flow (Update, a coroutine, a test body), NOT
        /// from inside a render-pipeline callback: URP cannot start a nested camera render.
        /// </summary>
        public static Texture2D Capture(CameraPose pose, int width, int height)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Capture size must be positive: " + width + "x" + height);
            }

            // Built inactive so the Camera does not auto-render for one frame between
            // AddComponent and the explicit Render() below - the same idiom the camera rig
            // factory uses. hideFlags keep it out of a scene save should a capture ever be
            // triggered from an Editor tool while a scene is open.
            var go = new GameObject("ArtVisualRegression Camera (" + pose.name + ")");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);

            RenderTexture rt = null;
            Texture2D result = null;

            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.fieldOfView = pose.verticalFov;
                camera.nearClipPlane = NearClip;
                camera.farClipPlane = FarClip;
                camera.allowHDR = true;
                camera.allowMSAA = false;

                camera.transform.position = pose.position;
                camera.transform.LookAt(pose.lookAt, Vector3.up);

                // URP needs its additional data to know this is a Base camera with the
                // same post-processing state as the gameplay camera (off, see brief B-11).
                // GetUniversalAdditionalCameraData adds the component when it is missing.
                UniversalAdditionalCameraData urp = camera.GetUniversalAdditionalCameraData();
                urp.renderType = CameraRenderType.Base;
                urp.renderPostProcessing = false;
                urp.antialiasing = AntialiasingMode.None;
                urp.renderShadows = true;

                go.SetActive(true);

                rt = new RenderTexture(width, height, 24);
                rt.name = "ArtVisualRegression RT";
                rt.antiAliasing = 1;
                rt.Create();

                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = null;

                result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.name = pose.name;

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                result.Apply(false, false);
                RenderTexture.active = previous;
            }
            catch
            {
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }

                throw;
            }
            finally
            {
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                // Immediate, not deferred: a deferred Destroy would leave an enabled camera
                // alive until end of frame, and URP would render it once more into nothing.
                UnityEngine.Object.DestroyImmediate(go);
            }

            return result;
        }

        /// <summary>Captures and writes a PNG. Creates the directory; overwrites the file.</summary>
        public static void CaptureToPng(CameraPose pose, int width, int height, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            Texture2D texture = Capture(pose, width, height);
            try
            {
                byte[] png = texture.EncodeToPNG();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(path, png);
                Debug.Log(LogPrefix + "Visual capture '" + pose.name + "' " + width + "x" + height + " -> " + path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ---------------------------------------------------------------- compare

        public struct Diff
        {
            /// <summary>Mean absolute difference over every channel of every pixel (0..255).</summary>
            public double MeanAbs;

            /// <summary>Largest single-channel difference seen (0..255).</summary>
            public double MaxAbs;

            /// <summary>Fraction of pixels where at least one channel differs by more than the threshold (0..1).</summary>
            public double ChangedFraction;

            public int Width, Height;

            public override string ToString()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}x{1} meanAbs={2:0.000} maxAbs={3:0} changed={4:0.0000}",
                    Width, Height, MeanAbs, MaxAbs, ChangedFraction);
            }
        }

        /// <summary>
        /// Per-pixel, per-channel absolute difference of two readable textures. Alpha is
        /// ignored: captures are RGB24 and a PNG with alpha would only report its own
        /// encoding. A size mismatch is reported as "everything changed" rather than
        /// thrown, so a gate reading ChangedFraction fails the same way for both.
        /// </summary>
        public static Diff Compare(Texture2D a, Texture2D b, byte perChannelThreshold)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            var diff = new Diff { Width = a.width, Height = a.height };

            if (a.width != b.width || a.height != b.height)
            {
                diff.MeanAbs = 255d;
                diff.MaxAbs = 255d;
                diff.ChangedFraction = 1d;
                return diff;
            }

            // GetPixels32 normalises whatever format LoadImage or ReadPixels produced into
            // one layout, at the cost of two w*h*4 byte arrays. Acceptable for a tool that
            // runs a handful of times per gate, and far simpler than per-format raw reads.
            Color32[] pa = a.GetPixels32();
            Color32[] pb = b.GetPixels32();

            int count = Math.Min(pa.Length, pb.Length);
            if (count == 0)
            {
                return diff;
            }

            long sum = 0;
            int max = 0;
            int changed = 0;
            int threshold = perChannelThreshold;

            for (int i = 0; i < count; i++)
            {
                Color32 x = pa[i];
                Color32 y = pb[i];

                int dr = x.r > y.r ? x.r - y.r : y.r - x.r;
                int dg = x.g > y.g ? x.g - y.g : y.g - x.g;
                int db = x.b > y.b ? x.b - y.b : y.b - x.b;

                sum += dr + dg + db;

                int m = dr > dg ? dr : dg;
                if (db > m)
                {
                    m = db;
                }

                if (m > max)
                {
                    max = m;
                }

                if (m > threshold)
                {
                    changed++;
                }
            }

            diff.MeanAbs = sum / (3d * count);
            diff.MaxAbs = max;
            diff.ChangedFraction = changed / (double)count;
            return diff;
        }

        /// <summary>Loads two PNG files and compares them. Temporary textures are destroyed before returning.</summary>
        public static Diff ComparePng(string a, string b, byte perChannelThreshold)
        {
            Texture2D ta = null;
            Texture2D tb = null;
            try
            {
                ta = LoadPng(a);
                tb = LoadPng(b);
                return Compare(ta, tb, perChannelThreshold);
            }
            finally
            {
                if (ta != null)
                {
                    UnityEngine.Object.DestroyImmediate(ta);
                }

                if (tb != null)
                {
                    UnityEngine.Object.DestroyImmediate(tb);
                }
            }
        }

        private static Texture2D LoadPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("PNG not found: " + path, path);
            }

            byte[] bytes = File.ReadAllBytes(path);

            // Size and format are replaced by LoadImage; the 2x2 is only a valid seed.
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            texture.name = Path.GetFileNameWithoutExtension(path);

            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidDataException("Not a decodable PNG: " + path);
            }

            return texture;
        }
    }
}
