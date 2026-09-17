using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019b · ADR-019-2 · The blood colour band and the recolour rule. Directive §10
    /// (user, 2026-09-16): blood is dark crimson, HSV h 355–5°, s 0.70–0.85, v 0.25–0.45,
    /// and must read apart from the skin-1 accent, the beacon amber and the vermilion
    /// warning reserve. Two states (art-director brief): a fresh splash and a decal that
    /// dries over 30 s. The AP-010 v002 flesh/zombie sheets are painted near-grey
    /// (mean v 0.12–0.23), so the runtime recolours them: tint = target ÷ mean texel colour
    /// (<see cref="VfxCatalogEntry.MeanOpaqueColour"/>), which puts the mean texel exactly on
    /// the target and keeps the texture's own light/dark variation.
    /// </summary>
    public static class BloodPalette
    {
        // ---- DEC-077 band (degrees, 0..1, 0..1). Hue wraps through 0.
        public const float HueMinDegrees = 355f;
        public const float HueMaxDegrees = 5f;
        public const float SaturationMin = 0.70f;
        public const float SaturationMax = 0.85f;
        public const float ValueMin = 0.25f;
        public const float ValueMax = 0.45f;

        /// <summary>Fresh splash card and death sheet: HSV(358°, 0.82, 0.42) — the brief's 0.45 is the band ceiling and the sheets' bright texels sit above their mean, so the target is set 0.03 under it (measured: 0.43–0.45).</summary>
        public static readonly Color Fresh = Color.HSVToRGB(358f / 360f, 0.82f, 0.42f);
        /// <summary>HDR intensity on the fresh card (brief 5 asked ×1.6 for bloom). At 1.6 the soft v002 sheet clipped to pink and left the band, at 1.15 it still measured over v 0.45; kept at 1.0 until the AP-010B sheets — the knob stays.</summary>
        public const float FreshHdrGain = 1.0f;
        /// <summary>Droplet particles: HSV(357°, 0.83, 0.42), no HDR (the brief's 0.85 is the band's saturation ceiling; measured 0.851 on screen, so 0.02 under it).</summary>
        public static readonly Color Droplet = Color.HSVToRGB(357f / 360f, 0.83f, 0.42f);
        /// <summary>Decal at birth: HSV(358°, 0.80, 0.40).</summary>
        public static readonly Color DecalWet = Color.HSVToRGB(358f / 360f, 0.80f, 0.40f);
        /// <summary>Decal dried: HSV(2°, 0.72, 0.28) — the floor of the band lifted to 0.28 so dark stone/wood (v 0.20–0.30) does not swallow it.</summary>
        public static readonly Color DecalDry = Color.HSVToRGB(2f / 360f, 0.72f, 0.28f);
        /// <summary>Drying: constant for the first 8 s, then ease-in-out to dry by 30 s.</summary>
        public const float DryHoldSeconds = 8f;
        public const float DrySeconds = 30f;

        /// <summary>The AP-006 skin-1 crimson accent as the directive states it (h 358°, s 0.80, v 0.64): the blood band sits ≥ 0.19 below it in value; reference for the separation readout.</summary>
        public static readonly Color SkinOneAccent = Color.HSVToRGB(358f / 360f, 0.80f, 0.64f);

        /// <summary>
        /// Screen calibration (linear multipliers on the recolour gain). The catalog mean is a texel
        /// statistic; what lands on screen is brighter (filtering, the dense core of a soft sheet
        /// outweighing its fringe), so each slot carries the ratio measured by
        /// <c>VfxBloodTests.KanRengi_RenderdeBantIcinde</c> on the v002 sheets (2026-09-16): marks
        /// 0.75, droplets 0.75, death sheet 0.78, splash sheet 0.40. Re-measure when AP-010B lands.
        /// </summary>
        public const float MarkCalibration = 0.75f;
        public const float DropletCalibration = 0.75f;
        public const float DeathSheetCalibration = 0.50f;   // 0.78 met the mean but p95 0.61, 0.62 → 0.55 (AD M3) — the v002 sheet's bright core
        public const float SplashCalibration = 0.40f;

        /// <summary>
        /// AP-010B v003 sheets are painted in the band per texel (P-05: v ≤ 0.447 on every colour file), but their
        /// coverage-weighted on-screen mean sits 10–12 % brighter (dense core vs fringe, as with the v002 marks):
        /// splash 0.45, heavy 0.49, death 0.50 measured. A uniform 0.55 linear darkening keeps the mean under the 0.45
        /// ceiling and the p95 value (AD M3) under 0.50 — the bright core responds sub-linearly (0.75 → p95 0.52–0.54,
        /// 0.65 → 0.48–0.51); means then sit at ≈ 0.34–0.38, still well inside the band.
        /// </summary>
        public const float AuthoredSheetDarkening = 0.55f;
        public static readonly Color AuthoredSheetTint = new Color(Mathf.LinearToGammaSpace(AuthoredSheetDarkening), Mathf.LinearToGammaSpace(AuthoredSheetDarkening), Mathf.LinearToGammaSpace(AuthoredSheetDarkening), 1f);

        /// <summary>Largest per-channel linear gain the recolour may apply (a sheet darker than 1/40 of the target in linear terms is left dark rather than blown out).</summary>
        public const float MaxGainLinear = 40f;
        /// <summary>The same ceiling as the gamma-encoded Color value the shader receives.</summary>
        public static float MaxGain => Mathf.LinearToGammaSpace(MaxGainLinear);

        /// <summary>
        /// The tint that lands <paramref name="row"/>'s mean opaque texel on <paramref name="target"/>
        /// (sRGB) when the shader multiplies the linear texel by the tint. The catalog mean is the
        /// LINEAR mean of the opaque texels (what the sRGB sampler yields), the gain is computed in
        /// linear space and gamma-encoded here because Unity converts a Color property sRGB → linear
        /// on upload (also above 1). <paramref name="linearGain"/> is an extra HDR multiplier (the
        /// fresh card). White mean (unknown) → the target itself.
        /// </summary>
        public static Color TintFor(VfxCatalogEntry row, Color target, float linearGain = 1f)
        {
            Color mean = row != null ? row.MeanOpaqueColour : Color.white;
            return new Color(
                Gain(target.r, mean.r, linearGain), Gain(target.g, mean.g, linearGain), Gain(target.b, mean.b, linearGain), target.a);
        }

        /// <summary>The same gain in LINEAR terms, for data Unity does not gamma-convert on upload (mesh vertex colours: the merged chunks).</summary>
        public static Color TintForLinear(VfxCatalogEntry row, Color target, float linearGain = 1f)
        {
            Color mean = row != null ? row.MeanOpaqueColour : Color.white;
            return new Color(
                GainLinear(target.r, mean.r, linearGain), GainLinear(target.g, mean.g, linearGain), GainLinear(target.b, mean.b, linearGain), target.a);
        }

        private static float Gain(float targetSrgb, float meanLinear, float linearGain)
        {
            return Mathf.LinearToGammaSpace(GainLinear(targetSrgb, meanLinear, linearGain));
        }

        private static float GainLinear(float targetSrgb, float meanLinear, float linearGain)
        {
            float targetLinear = Mathf.GammaToLinearSpace(targetSrgb) * linearGain;
            return meanLinear <= 0.0005f ? MaxGainLinear : Mathf.Min(MaxGainLinear, targetLinear / meanLinear);
        }

        /// <summary>Wet → dry decal colour at <paramref name="ageSeconds"/> (brief 5).</summary>
        public static Color DecalColourAt(float ageSeconds)
        {
            if (ageSeconds <= DryHoldSeconds)
            {
                return DecalWet;
            }

            float t = Mathf.Clamp01((ageSeconds - DryHoldSeconds) / (DrySeconds - DryHoldSeconds));
            t = t * t * (3f - 2f * t);
            return Color.Lerp(DecalWet, DecalDry, t);
        }

        /// <summary>True when the (sRGB) colour sits inside the DEC-077 blood band.</summary>
        public static bool IsInBand(Color c, out float hueDegrees, out float saturation, out float value)
        {
            Color.RGBToHSV(c, out float h, out saturation, out value);
            hueDegrees = h * 360f;
            bool hueOk = hueDegrees >= HueMinDegrees || hueDegrees <= HueMaxDegrees;
            return hueOk && saturation >= SaturationMin - 1e-3f && saturation <= SaturationMax + 1e-3f
                   && value >= ValueMin - 1e-3f && value <= ValueMax + 1e-3f;
        }

        /// <summary>CIE76 ΔE between two sRGB colours (brief 7c: ≥ 25 against the reserves, ≥ 8 wet ↔ dry).</summary>
        public static float DeltaE(Color a, Color b)
        {
            ToLab(a, out float l1, out float a1, out float b1);
            ToLab(b, out float l2, out float a2, out float b2);
            return Mathf.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
        }

        private static void ToLab(Color c, out float l, out float a, out float b)
        {
            float r = Mathf.GammaToLinearSpace(c.r), g = Mathf.GammaToLinearSpace(c.g), bl = Mathf.GammaToLinearSpace(c.b);
            float x = (r * 0.4124f + g * 0.3576f + bl * 0.1805f) / 0.95047f;
            float y = (r * 0.2126f + g * 0.7152f + bl * 0.0722f) / 1.00000f;
            float z = (r * 0.0193f + g * 0.1192f + bl * 0.9505f) / 1.08883f;
            x = LabF(x); y = LabF(y); z = LabF(z);
            l = 116f * y - 16f;
            a = 500f * (x - y);
            b = 200f * (y - z);
        }

        private static float LabF(float t)
        {
            return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        }
    }

    /// <summary>
    /// WP-019b · Catalog slots of the blood set (AP-010B v003 ids first, the AP-010 v002 rows
    /// as the tail fallback). Resolution is by id order, first playable row wins; once an
    /// AP-010B (family Blood) row is present the v002 tail is not mixed in, so the v003 set
    /// swaps the visuals with no code change. v003: A1 splash 01–06 (1.0 m, origin (0.12, 0.5),
    /// cone travels +u), A6 death 01–03 (3.0 m, origin (0.5, 0.08)), A7 heavy 01–02 (1.4 m),
    /// A2 droplet sprite sheet 4×4, A3 ground marks 01–06 (albedo; normal / height packs are
    /// catalog rows for a lit-decal pass), A4 wall drips 01–04, A5 pool albedo (2.8 m) + growth masks.
    /// </summary>
    public static class BloodSlots
    {
        public static readonly string[] Splash =
        {
            "fb_blood_splash_01", "fb_blood_splash_02", "fb_blood_splash_03",
            "fb_blood_splash_04", "fb_blood_splash_05", "fb_blood_splash_06",
            VfxIds.ImpactFlesh
        };

        public static readonly string[] Heavy = { "fb_blood_heavy_01", "fb_blood_heavy_02", VfxIds.ImpactFlesh };

        public static readonly string[] GroundDecal =
        {
            "decal_blood_ground_01_albedo", "decal_blood_ground_02_albedo", "decal_blood_ground_03_albedo",
            "decal_blood_ground_04_albedo", "decal_blood_ground_05_albedo", "decal_blood_ground_06_albedo",
            VfxIds.DecalImpactFlesh
        };

        public static readonly string[] WallDecal =
        {
            "decal_blood_wall_01_albedo", "decal_blood_wall_02_albedo", "decal_blood_wall_03_albedo", "decal_blood_wall_04_albedo",
            VfxIds.DecalImpactFlesh
        };

        public static readonly string[] Droplet = { VfxIds.BloodDropletSheet, VfxIds.DecalImpactFlesh };

        public static readonly string[] Pool = { VfxIds.BloodPoolAlbedo, VfxIds.DecalZombieSplat01, VfxIds.DecalZombieSplat02 };

        public static readonly string[] DeathSheet = { "fb_blood_death_01", "fb_blood_death_02", "fb_blood_death_03", VfxIds.ZombieDeath };

        /// <summary>Playable rows of a slot, in order, into <paramref name="into"/> (cleared first). Returns the count.</summary>
        public static int Resolve(string[] slot, System.Collections.Generic.List<VfxCatalogEntry> into)
        {
            into.Clear();
            bool ap010b = false;
            for (int i = 0; i < slot.Length; i++)
            {
                if (VfxLibrary.TryGetPlayable(slot[i], out VfxCatalogEntry row))
                {
                    // Once an AP-010B row exists the v002 fallback at the tail is not mixed in.
                    if (row.Family == VfxFamily.Blood) ap010b = true;
                    else if (ap010b) continue;
                    into.Add(row);
                }
            }

            return into.Count;
        }
    }
}
