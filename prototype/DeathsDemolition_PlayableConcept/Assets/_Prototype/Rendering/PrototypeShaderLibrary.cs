using UnityEngine;

namespace DD.Prototype.Rendering
{
    /// <summary>
    /// PROTOTYPE-ONLY: the build's only guaranteed handle on the URP shaders this
    /// prototype draws with.
    ///
    /// WHY THIS EXISTS. Every material in this prototype is created at runtime - there is
    /// not a single .mat asset in the project. The world, the player, the zombies, the
    /// atmosphere and the feedback effects all used to reach for their shader with
    /// <c>Shader.Find("Universal Render Pipeline/Lit")</c>. In the Editor that works,
    /// because every shader in every imported package is loaded. In a Player build it does
    /// not: the build only contains shaders something in the build actually references, and
    /// nothing referenced URP's. So the whole game shipped without its shaders, every
    /// Shader.Find returned null, and each system quietly fell back to the Built-in
    /// pipeline's Standard / Sprites-Default - which URP cannot draw. That is the "opening
    /// screen is badly corrupted" report, and the single line in Player.log that gave it
    /// away:  [Zombie] URP Lit shader not found. Falling back to Standard.
    ///
    /// This asset lives in a Resources folder, so it is always in the build, and it holds
    /// direct serialized references, so the shaders come with it. Strings cannot rot into
    /// silence any more: a missing reference is a hard, named failure.
    ///
    /// <see cref="_variantPins"/> carries the second half of the problem. Getting a shader
    /// into the build is not the same as getting the VARIANT you need. URP's transparency
    /// and blending keywords are <c>shader_feature</c>s, which the build strips unless some
    /// material asset actually uses them - and our transparent materials are only ever born
    /// at runtime, long after stripping. The pins are ordinary material assets, configured
    /// with exactly the keyword combinations the runtime turns on, that exist for no other
    /// reason than to keep those variants alive.
    /// </summary>
    public sealed class PrototypeShaderLibrary : ScriptableObject
    {
        /// <summary>Resources path. Kept in one place: runtime, editor tool and build check all use it.</summary>
        public const string ResourcePath = "PrototypeShaderLibrary";

        [SerializeField]
        [Tooltip("Universal Render Pipeline/Lit - world, player and zombie surfaces.")]
        private Shader _lit;

        [SerializeField]
        [Tooltip("Universal Render Pipeline/Simple Lit - cheaper stand-in for Lit.")]
        private Shader _simpleLit;

        [SerializeField]
        [Tooltip("Universal Render Pipeline/Unlit - feedback meshes, tracers, debris.")]
        private Shader _unlit;

        [SerializeField]
        [Tooltip("Universal Render Pipeline/Particles/Unlit - hit sparks, bursts, atmosphere motes.")]
        private Shader _particlesUnlit;

        [SerializeField]
        [Tooltip("Skybox/Procedural - the sky. Built-in, but still has to be referenced to ship.")]
        private Shader _skybox;

        [SerializeField]
        [Tooltip("DD/VFX Flipbook - authored VFX sheets and Special Weapon usage cards.")]
        private Shader _vfxFlipbook;

        [SerializeField]
        [Tooltip("DD/VFX HeatHaze - the Special Weapon barrel heat distortion card.")]
        private Shader _vfxHeatHaze;

        [SerializeField]
        [Tooltip("DD/UI/Fx - the UI effect material (WP-068 slice 4).")]
        private Shader _uiFx;

        [SerializeField]
        [Tooltip("Hidden/DD/UI/FrozenFrame - the paused-world blur + ink wash (WP-068 slice 4).")]
        private Shader _uiFrozenFrame;

        [SerializeField]
        [Tooltip("DD/UI/MaskTint - the UI backdrop mask tint (WP-068).")]
        private Shader _uiMaskTint;

        [SerializeField]
        [Tooltip("DD/VFX Ribbon - tracer / glint ribbons (WP-073).")]
        private Shader _vfxRibbon;

        [SerializeField, Tooltip("DD/VFX Atmo - atmosphere grains, petals, leaves, steam, drips (WP-073 B5).")]
        private Shader _vfxAtmo;

        [SerializeField]
        [Tooltip("Materials that exist only to keep runtime keyword variants out of the stripper.")]
        private Material[] _variantPins = new Material[0];

        /// <summary>Lit surfaces: ground, walls, props, the player rig, zombie bodies.</summary>
        public Shader Lit => _lit;

        /// <summary>Cheaper lit shader. Only used if something deliberately asks for it.</summary>
        public Shader SimpleLit => _simpleLit;

        /// <summary>Unlit meshes: feedback shapes, tracers, debris shards.</summary>
        public Shader Unlit => _unlit;

        /// <summary>Particle sheets: impact sparks, blood, zombie bursts, drifting motes.</summary>
        public Shader ParticlesUnlit => _particlesUnlit;

        /// <summary>The procedural sky.</summary>
        public Shader Skybox => _skybox;

        /// <summary>Authored VFX and Special Weapon flipbook sheets.</summary>
        public Shader VfxFlipbook => _vfxFlipbook;

        /// <summary>Barrel heat distortion / shimmer card.</summary>
        public Shader VfxHeatHaze => _vfxHeatHaze;

        /// <summary>DD/UI/Fx: the UI effect material (WP-068 slice 4).</summary>
        public Shader UiFx => _uiFx;

        /// <summary>Hidden/DD/UI/FrozenFrame: the paused-world blur + ink wash (WP-068 slice 4).</summary>
        public Shader UiFrozenFrame => _uiFrozenFrame;

        /// <summary>DD/UI/MaskTint: the UI backdrop mask tint (WP-068).</summary>
        public Shader UiMaskTint => _uiMaskTint;

        /// <summary>DD/VFX Ribbon: tracer / glint ribbons (WP-073).</summary>
        public Shader VfxRibbon => _vfxRibbon;
        public Shader VfxAtmo => _vfxAtmo;

        /// <summary>Read-only view, for the build check and the tests.</summary>
        public Material[] VariantPins => _variantPins;

        /// <summary>
        /// Names every reference that is missing, or null when the library is complete.
        /// One message listing everything, so a broken library is diagnosed in one read
        /// rather than one rebuild per missing field.
        /// </summary>
        public string DescribeMissing()
        {
            var eksik = new System.Collections.Generic.List<string>(7);
            if (_lit == null) { eksik.Add("Lit"); }
            if (_simpleLit == null) { eksik.Add("SimpleLit"); }
            if (_unlit == null) { eksik.Add("Unlit"); }
            if (_particlesUnlit == null) { eksik.Add("ParticlesUnlit"); }
            if (_skybox == null) { eksik.Add("Skybox"); }
            if (_vfxFlipbook == null) { eksik.Add("VfxFlipbook"); }
            if (_vfxHeatHaze == null) { eksik.Add("VfxHeatHaze"); }
            if (_uiFx == null) { eksik.Add("UiFx"); }
            if (_uiFrozenFrame == null) { eksik.Add("UiFrozenFrame"); }
            if (_uiMaskTint == null) { eksik.Add("UiMaskTint"); }
            if (_vfxRibbon == null) { eksik.Add("VfxRibbon"); }
            if (_vfxAtmo == null) { eksik.Add("VfxAtmo"); }

            for (int i = 0; i < _variantPins.Length; i++)
            {
                if (_variantPins[i] == null)
                {
                    eksik.Add("VariantPins[" + i + "]");
                }
            }

            return eksik.Count == 0 ? null : string.Join(", ", eksik);
        }
    }
}
