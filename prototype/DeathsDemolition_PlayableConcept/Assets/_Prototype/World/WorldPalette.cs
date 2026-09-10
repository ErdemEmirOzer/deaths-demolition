using DD.Prototype.Rendering;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// Every colour and material in the blockout, and the rule that keeps the world from
    /// competing with the gameplay.
    ///
    /// REQ-105 IS A PALETTE CONSTRAINT, NOT A POST-PROCESS TRICK
    /// ---------------------------------------------------------
    /// Gold, the Special Weapon beacon and the combat warnings have to stay visible
    /// against the environment. You cannot achieve that by making the pickups brighter,
    /// because whatever you do to a pickup you can also accidentally do to a roof. The
    /// only robust way is to RESERVE part of colour space for gameplay and refuse to
    /// spend it on scenery:
    ///
    ///   reserved      hue 35-65 deg (gold) at saturation &gt; 0.30
    ///   reserved      anything at saturation &gt; 0.55 AND value &gt; 0.80 (beacons)
    ///   environment   moss green, stone gray, dark wood, pale cream, muted pink/purple
    ///
    /// The request also asks for vermilion "sparingly", so it is not banned, it is
    /// BUDGETED: <see cref="VermilionBudget"/> pieces may use it and the builder counts
    /// them. <see cref="Validate"/> checks all of this against the materials that were
    /// actually created and names the offender, so the rule cannot quietly rot.
    ///
    /// Materials are created once, shared by every piece that wants them, and are plain
    /// URP/Lit with flat base colours: no textures anywhere in the blockout.
    /// </summary>
    public sealed class WorldPalette
    {
        // ------------------------------------------------------------------ reserved

        /// <summary>Lower bound of the hue band reserved for gold, in degrees.</summary>
        public const float GoldHueMin = 35f;

        /// <summary>Upper bound of the hue band reserved for gold, in degrees.</summary>
        public const float GoldHueMax = 65f;

        /// <summary>Saturation at which a colour starts to read as "a pickup".</summary>
        public const float ReservedSaturation = 0.30f;

        /// <summary>Beacon zone: this saturated AND this bright is gameplay only.</summary>
        public const float BeaconSaturation = 0.55f;

        public const float BeaconValue = 0.80f;

        /// <summary>How many separate pieces may wear vermilion. Request: "limited".</summary>
        public const int VermilionBudget = 6;

        // ------------------------------------------------------------------ colours
        // PROTOTYPE-ONLY, every value invented: the request names the palette in words
        // ("moss green", "stone gray", "dark wood", "pale cream"), never in numbers.

        public static readonly Color MossGreen = new Color(0.235f, 0.322f, 0.196f);
        public static readonly Color GrassGreen = new Color(0.318f, 0.416f, 0.239f);
        public static readonly Color FernGreen = new Color(0.278f, 0.376f, 0.243f);
        public static readonly Color StoneGray = new Color(0.541f, 0.549f, 0.529f);
        public static readonly Color StoneWet = new Color(0.400f, 0.424f, 0.431f);
        public static readonly Color StoneDark = new Color(0.302f, 0.318f, 0.325f);
        public static readonly Color GravelPale = new Color(0.667f, 0.647f, 0.588f);
        public static readonly Color DirtBrown = new Color(0.408f, 0.349f, 0.271f);
        public static readonly Color DarkWood = new Color(0.196f, 0.145f, 0.118f);
        public static readonly Color MidWood = new Color(0.325f, 0.243f, 0.184f);
        public static readonly Color PaleCream = new Color(0.847f, 0.816f, 0.729f);
        public static readonly Color RoofTile = new Color(0.259f, 0.286f, 0.310f);
        public static readonly Color SakuraPink = new Color(0.886f, 0.678f, 0.741f);
        public static readonly Color SakuraPurple = new Color(0.682f, 0.545f, 0.702f);
        public static readonly Color BambooGreen = new Color(0.435f, 0.510f, 0.286f);
        public static readonly Color PineGreen = new Color(0.192f, 0.278f, 0.212f);
        public static readonly Color Vermilion = new Color(0.678f, 0.243f, 0.161f);
        public static readonly Color PondWater = new Color(0.180f, 0.294f, 0.310f, 0.78f);
        public static readonly Color KoiOrange = new Color(0.780f, 0.396f, 0.212f);

        // ------------------------------------------------------------------ materials

        public Material Gravel { get; private set; }
        public Material Stone { get; private set; }
        public Material StoneWetMat { get; private set; }
        public Material StoneDarkMat { get; private set; }
        public Material Dirt { get; private set; }
        public Material Moss { get; private set; }
        public Material Grass { get; private set; }
        public Material Fern { get; private set; }
        public Material Wood { get; private set; }
        public Material WoodMid { get; private set; }
        public Material Plaster { get; private set; }
        public Material Tile { get; private set; }
        public Material Sakura { get; private set; }
        public Material SakuraDeep { get; private set; }
        public Material Bamboo { get; private set; }
        public Material Pine { get; private set; }
        public Material Accent { get; private set; }
        public Material Water { get; private set; }
        public Material Koi { get; private set; }

        private readonly List<Material> _all = new List<Material>(24);
        private readonly List<string> _names = new List<string>(24);

        private int _vermilionUses;

        /// <summary>How many pieces asked for the accent colour so far.</summary>
        public int VermilionUses => _vermilionUses;

        public WorldPalette()
        {
            // Smoothness carries the "after the rain" note (REQ-103) without a single
            // texture: stone and tile are wet and glossy, moss and wood stay matte. On an
            // overcast sky that reads as a damp sheen, not as plastic, because there is no
            // hard sun to blow out.
            Gravel = Make("Gravel", GravelPale, 0.10f);
            Stone = Make("Stone", StoneGray, 0.42f);
            StoneWetMat = Make("StoneWet", StoneWet, 0.55f);
            StoneDarkMat = Make("StoneDark", StoneDark, 0.38f);
            Dirt = Make("Dirt", DirtBrown, 0.08f);
            Moss = Make("Moss", MossGreen, 0.16f);
            Grass = Make("Grass", GrassGreen, 0.14f);
            Fern = Make("Fern", FernGreen, 0.18f);
            Wood = Make("DarkWood", DarkWood, 0.20f);
            WoodMid = Make("MidWood", MidWood, 0.22f);
            Plaster = Make("PaleCream", PaleCream, 0.06f);
            Tile = Make("RoofTile", RoofTile, 0.48f);
            Sakura = Make("Sakura", SakuraPink, 0.08f);
            SakuraDeep = Make("SakuraPurple", SakuraPurple, 0.08f);
            Bamboo = Make("Bamboo", BambooGreen, 0.26f);
            Pine = Make("Pine", PineGreen, 0.12f);
            Accent = Make("Vermilion", Vermilion, 0.30f);
            Koi = Make("Koi", KoiOrange, 0.30f);
            Water = MakeWater();
        }

        /// <summary>
        /// Hands out the accent material and counts it. Callers use this instead of
        /// touching <see cref="Accent"/> so the budget is measured, not hoped for.
        /// </summary>
        public Material UseAccent()
        {
            _vermilionUses++;
            return Accent;
        }

        /// <summary>Ground surface to material. One switch, so paint and physics agree.</summary>
        public Material ForSurface(GroundSurface surface)
        {
            switch (surface)
            {
                case GroundSurface.Gravel: return Gravel;
                case GroundSurface.Stone: return StoneWetMat;
                case GroundSurface.Dirt: return Dirt;
                case GroundSurface.Moss: return Moss;
                default: return Moss;
            }
        }

        public static DD.Prototype.Feedback.SurfaceKind ToSurfaceKind(GroundSurface surface)
        {
            switch (surface)
            {
                case GroundSurface.Stone:
                case GroundSurface.Gravel:
                    return DD.Prototype.Feedback.SurfaceKind.Stone;
                case GroundSurface.Dirt:
                    return DD.Prototype.Feedback.SurfaceKind.Dirt;
                case GroundSurface.Moss:
                    return DD.Prototype.Feedback.SurfaceKind.Foliage;
                default:
                    return DD.Prototype.Feedback.SurfaceKind.Default;
            }
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Walks every material this palette created and reports anything that trespasses
        /// into the colour space reserved for gameplay. Cheap, runs once at build time.
        /// </summary>
        public string Validate(out bool ok)
        {
            var sb = new System.Text.StringBuilder(256);
            int violations = 0;

            for (int i = 0; i < _all.Count; i++)
            {
                Material mat = _all[i];
                if (mat == null)
                {
                    continue;
                }

                Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                float hue = h * 360f;

                if (hue >= GoldHueMin && hue <= GoldHueMax && s > ReservedSaturation)
                {
                    violations++;
                    sb.Append("  '").Append(_names[i])
                        .Append("' sits in the GOLD hue band (h=").Append(hue.ToString("0"))
                        .Append(" s=").Append(s.ToString("0.00")).Append(")\n");
                }

                if (s > BeaconSaturation && v > BeaconValue)
                {
                    violations++;
                    sb.Append("  '").Append(_names[i])
                        .Append("' is as loud as a beacon (s=").Append(s.ToString("0.00"))
                        .Append(" v=").Append(v.ToString("0.00")).Append(")\n");
                }
            }

            if (_vermilionUses > VermilionBudget)
            {
                violations++;
                sb.Append("  vermilion used on ").Append(_vermilionUses)
                    .Append(" pieces, budget is ").Append(VermilionBudget).Append('\n');
            }

            ok = violations == 0;
            sb.Insert(0, "[World] Palette: " + _all.Count + " materials, " +
                         _vermilionUses + "/" + VermilionBudget + " vermilion, " +
                         violations + " readability violations" + (ok ? " -> OK\n" : " -> FAILED\n"));

            return sb.ToString();
        }

        /// <summary>Destroys the generated materials. Called when the world is torn down.</summary>
        public void Dispose()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(_all[i]);
                }
                else
                {
                    Object.DestroyImmediate(_all[i]);
                }
            }

            _all.Clear();
            _names.Clear();
        }

        // ------------------------------------------------------------------ internals

        private Material Make(string name, Color color, float smoothness)
        {
            Material mat = NewLit("World/" + name);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);

            // Specular highlights on a blockout mostly produce sparkle that competes with
            // the beacons. Keep the sheen, drop the highlight.
            if (mat.HasProperty("_SpecularHighlights"))
            {
                mat.SetFloat("_SpecularHighlights", smoothness > 0.35f ? 1f : 0f);
            }

            _all.Add(mat);
            _names.Add(name);
            return mat;
        }

        private Material MakeWater()
        {
            Material mat = NewLit("World/PondWater");
            mat.SetColor("_BaseColor", PondWater);
            mat.SetFloat("_Smoothness", 0.88f);
            mat.SetFloat("_Metallic", 0f);
            DD.Prototype.CameraSystem.OccluderFadeState.MakeTransparent(mat);
            mat.SetColor("_BaseColor", PondWater);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            _all.Add(mat);
            _names.Add("PondWater");
            return mat;
        }

        /// <summary>
        /// A lit world surface.
        ///
        /// The old comment here said "better a grey world than no world" and dropped to
        /// Standard. Under URP that trade does not exist - Standard does not draw grey, it
        /// draws garbage. The shader now comes from the serialized library, which is in the
        /// build by construction.
        /// </summary>
        private static Material NewLit(string name)
        {
            return new Material(PrototypeShaders.Lit) { name = name };
        }
    }
}
