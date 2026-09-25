using System;
using DD.Prototype.World;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-075 · DEC-102 · The volumetric light field of the v6 world (on the World v5 dress root, built by WorldV5Atmosphere): a light
    /// height haze plus regional volumes where the shafts should read (the bamboo grove, the ponds, the sakura court, the veranda, the
    /// gates). It publishes the field every frame as shader globals (DD_VolumetricLight.hlsl) for <see cref="VolumetricLightFeature"/>'s
    /// raymarch — and for S-07's atmosphere particles, which read the same density and sun visibility so dust and petals glow in the
    /// shafts through DD_ShaftMask (the runtime contract, C-00 25.09). CPU mirror: <see cref="DensityAt"/>, <see cref="ShaftMaskAt"/>,
    /// <see cref="SunDirection"/>, <see cref="Active"/>.
    /// </summary>
    public sealed class VolumetricLight : MonoBehaviour
    {
        [Serializable]
        public struct Region
        {
            public Vector3 Centre;
            public float RadiusXZ, RadiusY, Density;
        }

        public const int MaxRegions = 12;
        public static readonly int DensityId = Shader.PropertyToID("_DD_VL_Density");
        public static readonly int RegionsId = Shader.PropertyToID("_DD_VL_Regions");
        public static readonly int RegionShapeId = Shader.PropertyToID("_DD_VL_RegionShape");
        public static readonly int RegionCountId = Shader.PropertyToID("_DD_VL_RegionCount");
        public static readonly int ScatterId = Shader.PropertyToID("_DD_VL_Scatter");
        public static readonly int MaskId = Shader.PropertyToID("_DD_VL_Mask");
        public static readonly int BandId = Shader.PropertyToID("_DD_VL_Band");

        [Tooltip("Base haze density (1/m) at BaseHeight.")] public float BaseDensity = 0.0025f;
        [Tooltip("Height falloff (1/m): the haze halves every ln2 / k metres.")] public float HeightFalloff = 0.18f;
        public float BaseHeight = 0f;
        /// <summary>DEC-102 r2 · a lifted haze band (gaussian in height) the steep top-down rays cross: lit through the cloud gaps only, it
        /// reads as the slanted crepuscular columns from above; the eye-level views look under it.</summary>
        [Tooltip("Lifted band centre height (m).")] public float BandHeight = 18f;
        [Tooltip("Lifted band half thickness (m).")] public float BandHalfThickness = 9f;
        [Tooltip("Lifted band peak density (1/m); 0 = none.")] public float BandDensity = 0f;
        [Tooltip("Sun colour × intensity × this = the scattered light.")] public float ScatterScale = 1.0f;
        [Tooltip("Ambient (sky) scatter: RenderSettings' sky colour × this (the cold shadows).")] public float AmbientScatter = 0.35f;
        public Region[] Regions = Array.Empty<Region>();

        /// <summary>The live field (null when no v6 world is loaded).</summary>
        public static VolumetricLight Current { get; private set; }

        public static bool Active => Current != null && Current.isActiveAndEnabled;

        /// <summary>Toward the sun (the main light's reverse), for particles and cameras.</summary>
        public static Vector3 SunDirection
        {
            get
            {
                Light sun = CloudShadowDriver.Active != null ? CloudShadowDriver.Active.Sun : RenderSettings.sun;
                return sun != null ? -sun.transform.forward : Vector3.up;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            Shader.SetGlobalVector(DensityId, Vector4.zero);
            Shader.SetGlobalVector(MaskId, Vector4.zero);
            Shader.SetGlobalVector(BandId, Vector4.zero);
        }

        private readonly Vector4[] _regions = new Vector4[MaxRegions];
        private readonly Vector4[] _shapes = new Vector4[MaxRegions];

        private void OnEnable()
        {
            Current = this;
            Publish();
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            Shader.SetGlobalVector(DensityId, Vector4.zero);   // w 0: the field is off for every reader
            Shader.SetGlobalVector(MaskId, Vector4.zero);
            Shader.SetGlobalVector(BandId, Vector4.zero);
        }

        private void LateUpdate() => Publish();

        /// <summary>The field at a world point (1/m) — the CPU mirror of DD_VL_Density.</summary>
        public float DensityAt(Vector3 p)
        {
            float d = BaseDensity * Mathf.Exp(-HeightFalloff * Mathf.Max(p.y - BaseHeight, 0f));
            float band = (p.y - BandHeight) / Mathf.Max(BandHalfThickness, 1e-3f);
            d += BandDensity * Mathf.Exp(-band * band);
            int n = Mathf.Min(Regions.Length, MaxRegions);
            for (int i = 0; i < n; i++)
            {
                Region r = Regions[i];
                float dx = (p.x - r.Centre.x) / Mathf.Max(r.RadiusXZ, 1e-3f), dz = (p.z - r.Centre.z) / Mathf.Max(r.RadiusXZ, 1e-3f);
                float dy = (p.y - r.Centre.y) / Mathf.Max(r.RadiusY, 1e-3f);
                float q = Mathf.Clamp01(1f - (dx * dx + dz * dz + dy * dy));
                d += r.Density * q * q;
            }

            return d;
        }

        /// <summary>The field's peak density (1/m): the base at its height plus the densest region.</summary>
        public float PeakDensity()
        {
            float peak = 0f;
            int n = Mathf.Min(Regions.Length, MaxRegions);
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Regions[i].Density);
            return BaseDensity + peak;
        }

        /// <summary>CPU mirror of DD_ShaftMask without the cloud cookie: 0..1 of the field's peak at a world point (spawn weighting).</summary>
        public float ShaftMaskAt(Vector3 p)
        {
            float peak = PeakDensity();
            return peak > 0f ? Mathf.Clamp01(DensityAt(p) / peak) : 0f;
        }

        private void Publish()
        {
            int n = Mathf.Min(Regions.Length, MaxRegions);
            for (int i = 0; i < MaxRegions; i++)
            {
                Region r = i < n ? Regions[i] : default;
                _regions[i] = new Vector4(r.Centre.x, r.Centre.y, r.Centre.z, r.RadiusXZ);
                _shapes[i] = new Vector4(r.RadiusY, r.Density, 0f, 0f);
            }

            Shader.SetGlobalVector(DensityId, new Vector4(BaseDensity, HeightFalloff, BaseHeight, 1f));
            Shader.SetGlobalVectorArray(RegionsId, _regions);
            Shader.SetGlobalVectorArray(RegionShapeId, _shapes);
            Shader.SetGlobalFloat(RegionCountId, n);
            float peak = PeakDensity();
            Shader.SetGlobalVector(MaskId, new Vector4(peak > 0f ? 1f / peak : 0f, 0f, 0f, 0f));
            Shader.SetGlobalVector(BandId, new Vector4(BandHeight, BandHalfThickness, BandDensity, 0f));
            Light sun = CloudShadowDriver.Active != null ? CloudShadowDriver.Active.Sun : RenderSettings.sun;
            Color scatter = sun != null ? sun.color.linear * (sun.intensity * ScatterScale) : Color.black;
            Shader.SetGlobalVector(ScatterId, new Vector4(scatter.r, scatter.g, scatter.b, AmbientScatter));
        }
    }
}
