using System;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · The eighteen AP-001 environment families and the per-family facts every
    /// stage of the pipeline agrees on: which families ship a collider set, which
    /// material role each one renders with, and which ones are allowed to fade for the
    /// camera (REQ-035).
    ///
    /// This is data the AP-001 export manifest already states; it is repeated here as
    /// code so the prefab builder, the layout builder, the spawner and the asset gate
    /// all read the SAME table instead of each re-deriving it from file names. The
    /// order of <see cref="All"/> is the AP-001 asset_id order (alphabetical) and is
    /// load-bearing: the kit shelf slot and the library index of a family follow it.
    /// </summary>
    public static class ArtFamilies
    {
        /// <summary>All families, AP-001 asset_id order (alphabetical).</summary>
        public static readonly string[] All =
        {
            "courtyard_floor",
            "creeping_groundcover",
            "curb",
            "gate_transition",
            "greenery_cluster",
            "low_rise",
            "open_corridor",
            "palace_roof_bay",
            "planter_boundary",
            "plaster_wall",
            "ring_transition",
            "rock_prop",
            "roof_segment",
            "sakura_foliage",
            "sakura_trunk",
            "timber_beam",
            "timber_post",
            "understory_shrub"
        };

        /// <summary>
        /// Families whose prefab root goes on the CameraOccluder layer so the existing
        /// occlusion fader can dissolve them between the camera and the player. Nothing
        /// else is ever considered by that system, so a family missing here never fades.
        /// </summary>
        public static readonly string[] OccluderCandidates =
        {
            "plaster_wall",
            "palace_roof_bay",
            "roof_segment",
            "gate_transition",
            "open_corridor",
            "sakura_foliage"
        };

        /// <summary>
        /// The ten material roles the pipeline authors (<c>M_&lt;role&gt;.mat</c>). Eight are
        /// referenced by a family; <c>earth</c> is the pilot ground and
        /// <c>sakura_purple</c> is applied per instance by the spawner.
        /// </summary>
        public static readonly string[] MaterialRoles =
        {
            "stone",
            "wood",
            "bark",
            "earth",
            "plaster",
            "roof",
            "green",
            "moss",
            "sakura_pink",
            "sakura_purple"
        };

        /// <summary>Roles that ship an AP packed map and therefore get a derived MetallicSmoothness mask.</summary>
        public static readonly string[] PackedTextureRoles =
        {
            "stone",
            "wood",
            "bark",
            "earth"
        };

        /// <summary>The instance material name the spawner swaps in for the purple sakura trees.</summary>
        public const string SakuraPurpleRole = "sakura_purple";

        /// <summary>
        /// Families that export a <c>&lt;family&gt;_COLLIDERS.fbx</c>. Foliage, ground cover,
        /// shrubs and the palace roof bay are deliberately collider-less (AP-001 contract).
        /// </summary>
        private static readonly string[] WithCollider =
        {
            "courtyard_floor",
            "curb",
            "gate_transition",
            "low_rise",
            "open_corridor",
            "planter_boundary",
            "plaster_wall",
            "ring_transition",
            "rock_prop",
            "roof_segment",
            "sakura_trunk",
            "timber_beam",
            "timber_post"
        };

        public static bool IsKnown(string family)
        {
            return IndexOf(All, family) >= 0;
        }

        public static bool HasCollider(string family)
        {
            return IndexOf(WithCollider, family) >= 0;
        }

        public static bool IsOccluderCandidate(string family)
        {
            return IndexOf(OccluderCandidates, family) >= 0;
        }

        /// <summary>
        /// The material role a family renders with, or null for an unknown family. Taken
        /// from the AP-001 material assignment (<c>AP001_M_&lt;role&gt;_v009</c>) recorded in
        /// the import study for every LOD of every family.
        /// </summary>
        public static string MaterialRole(string family)
        {
            switch (family)
            {
                case "courtyard_floor":
                case "curb":
                case "low_rise":
                case "planter_boundary":
                case "ring_transition":
                case "rock_prop":
                    return "stone";

                case "open_corridor":
                case "timber_beam":
                case "timber_post":
                    return "wood";

                case "sakura_trunk":
                    return "bark";

                case "gate_transition":
                case "plaster_wall":
                    return "plaster";

                case "palace_roof_bay":
                case "roof_segment":
                    return "roof";

                case "creeping_groundcover":
                    return "moss";

                case "greenery_cluster":
                case "understory_shrub":
                    return "green";

                case "sakura_foliage":
                    return "sakura_pink";

                default:
                    return null;
            }
        }

        private static int IndexOf(string[] set, string value)
        {
            if (value == null)
            {
                return -1;
            }

            for (int i = 0; i < set.Length; i++)
            {
                if (string.Equals(set[i], value, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
