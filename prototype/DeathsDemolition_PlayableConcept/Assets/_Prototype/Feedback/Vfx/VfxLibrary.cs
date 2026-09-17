using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · Runtime door to the AP-010 catalog. Loaded once by name from Resources
    /// (DD.Prototype must not reference DD.Art), absent in a checkout where the art
    /// pipeline has not run — every consumer treats a null row as "keep the WP-002
    /// procedural effect", so the game never goes bare.
    /// </summary>
    public static class VfxLibrary
    {
        private static VfxCatalog s_catalog;
        private static bool s_tried;
        private static Sprite s_hudMarker;
        private static Mesh s_quad;

        /// <summary>DEC-053 Special Weapon beacon colour, applied at runtime to the greyscale beacon textures.</summary>
        /// <remarks>WP-019b AD M7: ≥ 10° warmer than the gold sparkle (h ≈ 41–48°) so beacon amber and the gold band read apart on screen — h ≈ 33°.</remarks>
        public static readonly Color BeaconTint = new Color(1.00f, 0.68f, 0.30f, 1f);

        /// <summary>Vermilion600 (#C4362B): the damage vignette tint (manifest palette rule).</summary>
        public static readonly Color VermilionTint = new Color(0.769f, 0.212f, 0.169f, 1f);

        /// <summary>Gold band core (#FFC74D) for the sparkle / trail cards.</summary>
        public static readonly Color GoldTint = new Color(1f, 0.78f, 0.30f, 1f);

        /// <summary>Ofuda talisman cream (the director's seal colour) for the AP-014 web card.</summary>
        public static readonly Color OfudaCreamTint = new Color(0.96f, 0.93f, 0.84f, 1f);

        public static VfxCatalog Catalog
        {
            get
            {
                if (!s_tried)
                {
                    s_tried = true;
                    s_catalog = Resources.Load<VfxCatalog>(VfxCatalog.ResourceName);
                }

                return s_catalog;
            }
        }

        public static bool IsReady => Catalog != null && Catalog.Entries.Count > 0;

        /// <summary>The row, or null when the catalog or the row is absent.</summary>
        public static VfxCatalogEntry Get(string id)
        {
            VfxCatalog c = Catalog;
            return c != null ? c.Find(id) : null;
        }

        /// <summary>True when the row exists AND has a playable material.</summary>
        public static bool TryGetPlayable(string id, out VfxCatalogEntry entry)
        {
            entry = Get(id);
            return entry != null && entry.Material != null && (entry.Texture != null || entry.Mesh != null);
        }

        /// <summary>
        /// The AP-010 HUD diamond as a Sprite (S-09 interface: grey outline, tint with
        /// <see cref="BeaconTint"/> at runtime). Null without the catalog.
        /// </summary>
        public static Sprite HudMarkerSprite
        {
            get
            {
                if (s_hudMarker == null)
                {
                    VfxCatalogEntry row = Get(VfxIds.HudMarker);
                    if (row != null && row.Texture != null)
                    {
                        var tex = row.Texture;
                        s_hudMarker = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        s_hudMarker.name = "AP010_hud_marker";
                    }
                }

                return s_hudMarker;
            }
        }

        /// <summary>A unit quad in the XY plane (centre pivot), shared by every flipbook/decal instance.</summary>
        public static Mesh Quad
        {
            get
            {
                if (s_quad == null)
                {
                    s_quad = new Mesh { name = "VFX Quad" };
                    s_quad.vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
                    };
                    s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                    s_quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    s_quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                    s_quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    s_quad.RecalculateBounds();
                    s_quad.UploadMeshData(false);
                }

                return s_quad;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_catalog = null;
            s_tried = false;
            s_hudMarker = null;
            s_quad = null;
        }
    }
}
