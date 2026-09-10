using DD.Prototype.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Rendering.EditorTools
{
    /// <summary>
    /// PROTOTYPE-ONLY: refuses to build a Player that would ship without its shaders.
    ///
    /// The corruption this guards against is invisible at build time - the build succeeds,
    /// the log is clean, and the fault only appears when someone launches the game and sees
    /// a broken screen. So the check runs before the build starts and stops it with a
    /// message that says what is missing and how to fix it.
    /// </summary>
    public sealed class ShaderLibraryBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var kitaplik = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);

            if (kitaplik == null)
            {
                throw new BuildFailedException(
                    "[Rendering] Build stopped: Resources/" + PrototypeShaderLibrary.ResourcePath +
                    " does not exist. Without it the Player ships with no URP shaders and every " +
                    "surface draws through a Built-in fallback URP cannot render. " +
                    "Fix: Tools > Deaths Demolition > Rebuild Shader Library.");
            }

            string eksik = kitaplik.DescribeMissing();
            if (eksik != null)
            {
                throw new BuildFailedException(
                    "[Rendering] Build stopped: the shader library is incomplete. " +
                    "Unassigned: " + eksik + ". " +
                    "Fix: Tools > Deaths Demolition > Rebuild Shader Library.");
            }

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                throw new BuildFailedException(
                    "[Rendering] Build stopped: no Render Pipeline Asset is assigned in " +
                    "Graphics settings. The prototype is authored for URP; building without it " +
                    "produces the same corrupted screen this check exists to prevent.");
            }
        }
    }
}
