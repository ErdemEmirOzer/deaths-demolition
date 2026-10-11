using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107B (R4): the wall buy station art - Codex AP-040 v001 (D1 freestanding board for every classic weapon, D3 shrine for
    /// the rocket launcher) and the ten weapon silhouette decals. Built by <c>ArtAp040WallBuyBuilder</c> (editor) at
    /// <c>Resources/WallBuy/DD_WallBuyStations</c>; <see cref="WallBuyStation"/> falls back to its placeholder rack when the
    /// library or a model is missing. Model contract (AP040-integration-v001.json): pivot at the base centre, front +Z,
    /// children SOCKET_Weapon / SOCKET_Decal (empties), *_LOD0..2 meshes, one *_proxy00_COL box that must not render.
    /// </summary>
    [CreateAssetMenu(fileName = "DD_WallBuyStations", menuName = "Deaths Demolition/Wall Buy Station Library")]
    public sealed class WallBuyStationLibrary : ScriptableObject
    {
        public const string ResourcePath = "WallBuy/DD_WallBuyStations";
        public const string WeaponSocket = "SOCKET_Weapon", DecalSocket = "SOCKET_Decal", ColliderSuffix = "_COL";

        [Serializable]
        public sealed class StationModel
        {
            [Tooltip("The imported FBX (model prefab).")] public GameObject model;
            [Tooltip("Uniform scale of the weapon model at SOCKET_Weapon (AP-040: D1 0.68, D3 0.80).")] public float weaponScale = 0.68f;
            [Tooltip("Side of the square decal quad at SOCKET_Decal, metres (AP-040: D1 0.85, D3 0.82).")] public float decalSize = 0.85f;
            [Tooltip("Metres from the pivot to the front face - where the player stands to use it starts past this.")] public float frontDepth = 0.31f;
            [Tooltip("LOD screen heights (LOD0, LOD1, LOD2); the last stays low so a station never culls at top-down distance.")]
            public float[] lodScreenHeights = { 0.40f, 0.15f, 0.02f };
        }

        [Serializable]
        public sealed class DecalEntry
        {
            public ClassicWeaponId weapon;
            public Material material;
        }

        [Tooltip("D1 V02 freestanding board - every classic weapon but the rocket launcher.")] public StationModel standard = new StationModel();
        [Tooltip("D3 shrine - the rocket launcher (wall-buy-only).")] public StationModel premium = new StationModel { weaponScale = 0.80f, decalSize = 0.82f, frontDepth = 0.48f };
        [Tooltip("D1 V01 wall mount (0.9 m, needs a wall behind it); unused until S-05 picks wall points.")] public StationModel wallMount = new StationModel();
        public DecalEntry[] decals = new DecalEntry[0];

        private static WallBuyStationLibrary s_current;
        private static bool s_looked;

        /// <summary>The library in Resources, loaded once (null when WP-107B's art is not imported - the placeholder rack draws).</summary>
        public static WallBuyStationLibrary Current
        {
            get
            {
                if (!s_looked)
                {
                    s_looked = true;
                    s_current = Resources.Load<WallBuyStationLibrary>(ResourcePath);
                }

                return s_current;
            }
        }

        /// <summary>Test seam: replace the loaded library (null forgets it and reloads from Resources next time).</summary>
        public static void SetForTests(WallBuyStationLibrary library)
        {
            s_current = library;
            s_looked = library != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            s_looked = false;
        }

        /// <summary>The station model for a weapon: the shrine for a wall-buy-only weapon, else the freestanding board (null if missing).</summary>
        public StationModel ModelFor(bool premiumStation)
        {
            StationModel m = premiumStation ? premium : standard;
            return m != null && m.model != null ? m : null;
        }

        /// <summary>The weapon's silhouette decal material, or null.</summary>
        public Material DecalFor(ClassicWeaponId weapon)
        {
            if (decals == null) return null;
            for (int i = 0; i < decals.Length; i++)
            {
                if (decals[i] != null && decals[i].weapon == weapon) return decals[i].material;
            }

            return null;
        }
    }
}
