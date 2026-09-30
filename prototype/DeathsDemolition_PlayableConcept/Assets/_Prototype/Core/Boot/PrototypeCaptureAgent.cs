using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DD.Prototype.Core.Boot
{
    /// <summary>
    /// PROTOTYPE-ONLY: lets the built Player take a picture of itself.
    ///
    /// H12 WRITTEN EXCEPTION (C-00 30.09, Codex-audit M-55): diagnostic/capture agents are normally dev-build only
    /// (<c>#if DEVELOPMENT_BUILD || UNITY_EDITOR</c>). This one deliberately stays in RELEASE builds: it is the only
    /// image-and-exit path of the flagless release smoke (tools/smoke.sh e1/e4 launch the release Player with
    /// <c>-ddCapture</c>; the agent writes the PNG and quits). It is active only with <c>-ddCapture</c>; without the
    /// flag it creates no object and allocates nothing beyond reading the command line (<see cref="TryStart"/>,
    /// tested by CaptureAgentReleaseExceptionTests). Do not add any other behaviour to it.
    ///
    /// A rendering fault that only exists in a build cannot be judged from the Editor, and
    /// the machine running these checks has no screen-recording permission, so nothing
    /// outside the process can photograph the window either. The Player capturing its own
    /// framebuffer is the one view that is both available and honest - it is exactly what
    /// the GPU drew.
    ///
    /// Entirely opt-in. With no <c>-ddCapture</c> argument this class allocates nothing and
    /// starts nothing; a normal launch never knows it is here.
    ///
    ///   DeathsDemolition.app/Contents/MacOS/... -ddCapture /tmp/shot.png -ddSize 1600x900 -ddDelay 8
    ///
    /// KNOWN LIMIT - READ BEFORE TRUSTING AN IMAGE FROM THIS.
    /// On a Retina display the captured PNG is NOT reliable. The window is measured in
    /// points while the Metal drawable behind it is twice that in pixels, and the capture
    /// comes back at the point size: the same build, at the same window size, produces a
    /// clean image on one run and a finely doubled, smeared one on the next, and switching
    /// macRetinaSupport off makes the smear disappear without changing a line of rendering
    /// code. A corrupt-looking capture from this agent is therefore evidence of nothing on
    /// its own - it has to be confirmed by a human looking at the actual screen before any
    /// rendering code is changed on account of it. It is trustworthy for what it was built
    /// for: reading LAYOUT, and proving a scene reaches its first frame at all.
    /// </summary>
    public static class PrototypeCaptureAgent
    {
        private const string YolBayragi = "-ddCapture";
        private const string BoyutBayragi = "-ddSize";
        private const string GecikmeBayragi = "-ddDelay";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Baslat()
        {
            TryStart(Environment.GetCommandLineArgs());
        }

        /// <summary>
        /// Starts the capture only when <paramref name="args"/> carries <c>-ddCapture &lt;path&gt;</c>; returns whether it did.
        /// Without the flag it only scans the array: no GameObject, no component, no allocation (H12 exception, see above).
        /// </summary>
        public static bool TryStart(string[] args)
        {
            string yol = Arg(args, YolBayragi);
            if (string.IsNullOrEmpty(yol))
            {
                return false;
            }

            var tasiyici = new GameObject("PrototypeCaptureAgent");
            UnityEngine.Object.DontDestroyOnLoad(tasiyici);
            tasiyici.AddComponent<Surucu>().Kur(yol, Arg(args, BoyutBayragi), Arg(args, GecikmeBayragi));
            return true;
        }

        private static string Arg(string[] args, string ad)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], ad, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private sealed class Surucu : MonoBehaviour
        {
            private string _yol;
            private string _boyut;
            private float _gecikme = 8f;

            internal void Kur(string yol, string boyut, string gecikme)
            {
                _yol = yol;
                _boyut = boyut;
                float g;
                if (!string.IsNullOrEmpty(gecikme) && float.TryParse(gecikme, out g))
                {
                    _gecikme = g;
                }
            }

            private IEnumerator Start()
            {
                if (!string.IsNullOrEmpty(_boyut))
                {
                    string[] p = _boyut.Split('x');
                    int g, y;
                    if (p.Length == 2 && int.TryParse(p[0], out g) && int.TryParse(p[1], out y))
                    {
                        Screen.SetResolution(g, y, FullScreenMode.Windowed);
                        // SetResolution lands on a later frame; capturing before it does
                        // would photograph the old size and prove nothing.
                        yield return new WaitForSecondsRealtime(2f);
                    }
                }

                yield return new WaitForSecondsRealtime(_gecikme);

                string klasor = Path.GetDirectoryName(_yol);
                if (!string.IsNullOrEmpty(klasor) && !Directory.Exists(klasor))
                {
                    Directory.CreateDirectory(klasor);
                }

                // CaptureScreenshotAsTexture, not CaptureScreenshot(path).
                //
                // The file-writing overload sizes its PNG from Screen.width/height, which
                // on a Retina display is the POINT size while the framebuffer it reads is
                // twice that in pixels. The rows are then reassembled at the wrong stride
                // and the image comes out as the whole screen smeared into a fine grid -
                // a picture of a bug that is not in the game. The texture overload asks the
                // backbuffer for its own dimensions, so what lands in the file is what the
                // GPU drew.
                yield return new WaitForEndOfFrame();
                Texture2D kare = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(_yol, kare.EncodeToPNG());

                Debug.Log(string.Format(
                    "[Capture] {0} · screen {1}x{2} · framebuffer {3}x{4}",
                    _yol, Screen.width, Screen.height, kare.width, kare.height));
                Destroy(kare);

                yield return null;
                Debug.Log("[Capture] done");
                Application.Quit();
            }
        }
    }
}
