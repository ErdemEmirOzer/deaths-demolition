using UnityEngine;

namespace DD.Prototype.Rendering
{
    /// <summary>
    /// PROTOTYPE-ONLY: the one door every material factory goes through to get a shader.
    ///
    /// Replaces <c>Shader.Find</c> everywhere. Shader.Find is a string lookup against
    /// whatever happens to be loaded, so in a Player build it answers null for anything the
    /// build did not already contain - silently, with no way for the caller to tell "this
    /// shader does not exist" from "this shader was stripped". Every caller then invented
    /// its own fallback down to Built-in pipeline shaders that URP cannot draw at all.
    ///
    /// This class has no fallbacks. Under URP there is no such thing as a safe substitute
    /// for a missing URP shader: Standard and Sprites/Default do not render, they corrupt.
    /// A missing library is a configuration failure that must be fixed before shipping, so
    /// it is reported once, loudly, and then thrown - never papered over with a material
    /// that will draw garbage.
    /// </summary>
    public static class PrototypeShaders
    {
        private static PrototypeShaderLibrary s_library;
        private static bool s_reported;

        /// <summary>Lit surfaces: world, player rig, zombie bodies.</summary>
        public static Shader Lit => Named(Library.Lit, "Lit");

        /// <summary>Cheaper lit shader.</summary>
        public static Shader SimpleLit => Named(Library.SimpleLit, "SimpleLit");

        /// <summary>Unlit meshes: feedback shapes, tracers, debris.</summary>
        public static Shader Unlit => Named(Library.Unlit, "Unlit");

        /// <summary>Particle sheets: sparks, blood, bursts, motes.</summary>
        public static Shader ParticlesUnlit => Named(Library.ParticlesUnlit, "ParticlesUnlit");

        /// <summary>The procedural sky.</summary>
        public static Shader Skybox => Named(Library.Skybox, "Skybox");

        /// <summary>
        /// True when the library is present and every reference is filled. Lets a caller
        /// ask without triggering the throw - used by the tests and by tooling.
        /// </summary>
        public static bool IsReady
        {
            get
            {
                PrototypeShaderLibrary lib = Load();
                return lib != null && lib.DescribeMissing() == null;
            }
        }

        /// <summary>The library itself, or a thrown failure. Never null on return.</summary>
        public static PrototypeShaderLibrary Library
        {
            get
            {
                PrototypeShaderLibrary lib = Load();
                if (lib == null)
                {
                    throw Fail(
                        "Resources/" + PrototypeShaderLibrary.ResourcePath + " is missing. " +
                        "Rebuild it from Tools > Deaths Demolition > Rebuild Shader Library.");
                }

                string eksik = lib.DescribeMissing();
                if (eksik != null)
                {
                    throw Fail(
                        "Shader library is incomplete. Unassigned: " + eksik + ". " +
                        "Rebuild it from Tools > Deaths Demolition > Rebuild Shader Library.");
                }

                return lib;
            }
        }

        /// <summary>Editor tooling drops the cache after rewriting the asset.</summary>
        public static void Forget()
        {
            s_library = null;
            s_reported = false;
        }

        private static PrototypeShaderLibrary Load()
        {
            if (s_library == null)
            {
                s_library = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);
            }

            return s_library;
        }

        private static Shader Named(Shader shader, string alan)
        {
            if (shader == null)
            {
                throw Fail("Shader library field '" + alan + "' is empty.");
            }

            return shader;
        }

        /// <summary>
        /// Logs once, throws every time. Once, because a broken library would otherwise
        /// print the same line for every material the game builds and bury everything else
        /// in the log. Every time, because the caller must not carry on and make a material
        /// out of nothing.
        /// </summary>
        private static System.InvalidOperationException Fail(string mesaj)
        {
            const string Prefix = "[Rendering] ";
            if (!s_reported)
            {
                s_reported = true;
                Debug.LogError(Prefix + mesaj);
            }

            return new System.InvalidOperationException(Prefix + mesaj);
        }
    }
}
