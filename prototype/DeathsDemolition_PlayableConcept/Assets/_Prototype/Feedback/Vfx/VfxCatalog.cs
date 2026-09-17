using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>What an AP-010 asset is, mirrored from the manifest's <c>kind</c>.</summary>
    public enum VfxKind
    {
        Flipbook = 0,
        Decal = 1,
        Card = 2,
        Texture = 3,
        Mesh = 4,
        /// <summary>Review-only copy (the GLB): listed for the count, no Unity asset behind it.</summary>
        Review = 5
    }

    /// <summary>The eight AP-010 families (manifest <c>family</c>).</summary>
    public enum VfxFamily
    {
        Impact = 0,
        Muzzle = 1,
        Explosion = 2,
        ArmorBreak = 3,
        Zombie = 4,
        Gold = 5,
        Beacon = 6,
        Vignette = 7,
        /// <summary>WP-019b · blood set (AP-010B; until it lands the flesh/zombie rows fill the slots, see <see cref="BloodSlots"/>).</summary>
        Blood = 8,
        // AP-010B wave 2 (v003 manifest families)
        Weapon = 9,
        World = 10,
        Enemy = 11,
        Player = 12
    }

    /// <summary>Manifest <c>colour_reserve</c>: which palette band the asset may occupy (DEC-076 / style guide).</summary>
    public enum VfxColourReserve
    {
        None = 0,
        Gold = 1,
        Beacon = 2,
        Vermilion = 3,
        Data = 4
    }

    /// <summary>
    /// WP-019 · ADR-019-1 · One row per AP-010 asset, written by the Editor importer from
    /// <c>AP010-vfx-manifest-v002.json</c> and never edited by hand. The runtime reads the
    /// sheet geometry (grid, frames, fps), the world size, the origin and the readability
    /// limit from here, so the manifest contract is the single source of every number the
    /// flipbook player uses.
    /// </summary>
    [Serializable]
    public sealed class VfxCatalogEntry
    {
        /// <summary>Stable id derived from the file name (e.g. <c>fb_impact_flesh</c>, <c>decal_scorch</c>).</summary>
        public string Id;
        public VfxFamily Family;
        public VfxKind Kind;
        /// <summary>Manifest file path (relative to the AP-010 exports root); provenance.</summary>
        public string SourceFile;
        public string Sha256;
        public Texture2D Texture;
        public Texture2D Emissive;
        public Texture2D Normal;
        public Mesh Mesh;
        /// <summary>Material authored for this row (flipbook / decal / card / mesh). Null for data textures and the review copy.</summary>
        public Material Material;
        public int Columns = 1;
        public int Rows = 1;
        public int FramesUsed = 1;
        public float Fps;
        public float DurationSeconds;
        public bool Loop;
        public float WorldSizeMetres;
        /// <summary>Manifest <c>origin_uv</c>: the point of the frame that sits on the event position (0..1, v up).</summary>
        public Vector2 OriginUv = new Vector2(0.5f, 0.5f);
        public float ReadabilityLimitSeconds;
        public VfxColourReserve ColourReserve;
        public bool Additive;
        public string UsageNote;
        /// <summary>
        /// WP-019b · Alpha-weighted mean LINEAR colour of the texels (alpha &gt; 0.1), written by the importer.
        /// The blood recolour divides its target colour by this so a near-grey sheet lands on
        /// the DEC-077 blood band whatever the source painted (<see cref="BloodPalette"/>).
        /// White when unknown (gain 1).
        /// </summary>
        public Color MeanOpaqueColour = Color.white;
        /// <summary>Manifest this row came from (AP010 main or an AP010B supplement); provenance only.</summary>
        public string ManifestFile;
        // ---- schema v003 (AP-010B)
        /// <summary>Manifest <c>slot</c> (the v002 stem the file fills); several files may share a slot (decal albedo / normal / height).</summary>
        public string Slot;
        /// <summary>Manifest <c>supersedes</c>: the v002 file this row replaced.</summary>
        public string Supersedes;
        /// <summary>Manifest <c>alias_to</c>: a retired row that points at its replacement (v002 flesh impact → blood splash 01).</summary>
        public string AliasTo;
        /// <summary>Second world dimension for non-square effects ([w, h] world sizes); equals <see cref="WorldSizeMetres"/> otherwise.</summary>
        public float WorldHeightMetres;
        /// <summary>Manifest <c>tint_rgb</c> default (LA greyscale sheets are tinted at runtime); white when the sheet is coloured.</summary>
        public Color DefaultTint = Color.white;
        /// <summary>Manifest <c>hdr</c> intensity (additive sheets); 1 when absent.</summary>
        public float HdrIntensity = 1f;
        /// <summary>Manifest <c>blend</c> text (v003); empty for v002 rows.</summary>
        public string BlendNote;

        public int FrameCells => Columns * Rows;
    }

    /// <summary>
    /// The AP-010 catalog asset (<c>Assets/_Art/VFX/Resources/DD_VfxCatalog.asset</c>). Loaded
    /// by name at runtime (<see cref="VfxLibrary"/>); DD.Prototype never references DD.Art.
    /// </summary>
    public sealed class VfxCatalog : ScriptableObject
    {
        public const string ResourceName = "DD_VfxCatalog";

        [SerializeField] private string _manifestSchema;
        [SerializeField] private string _manifestVersion;
        [SerializeField] private List<VfxCatalogEntry> _entries = new List<VfxCatalogEntry>();

        public string ManifestSchema { get => _manifestSchema; set => _manifestSchema = value; }
        public string ManifestVersion { get => _manifestVersion; set => _manifestVersion = value; }
        public List<VfxCatalogEntry> Entries => _entries;

        public VfxCatalogEntry Find(string id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].Id, id, StringComparison.Ordinal))
                {
                    return _entries[i];
                }
            }

            return null;
        }
    }
}
