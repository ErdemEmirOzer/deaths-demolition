using System.Collections.Generic;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047 B · The AP-014 Special Weapon use sheets (WP-037B `SpecialWeaponVisualLibrary.FindSheet(usage)`)
    /// as flipbook rows: one runtime `DD/VFX Flipbook` material per usage (cached, shared by every
    /// instance — the ≤ 1 material-per-instance rule holds), the sheet's grid / fps / duration / size copied
    /// into a synthetic <see cref="VfxCatalogEntry"/> so the pooled <see cref="FlipbookEffect"/> plays it
    /// like any catalog row. Absent library / sheet = null row (the WP-002 placeholder stays).
    /// </summary>
    public sealed class SpecialWeaponSheets
    {
        public readonly struct Playable
        {
            public Playable(VfxCatalogEntry row) { Row = row; }
            public VfxCatalogEntry Row { get; }
        }

        private readonly Dictionary<string, VfxCatalogEntry> _rows = new Dictionary<string, VfxCatalogEntry>(8);
        private Shader _shader;
        private bool _shaderTried;

        public int MaterialsCreated { get; private set; }

        public Playable Get(string usage)
        {
            if (_rows.TryGetValue(usage, out VfxCatalogEntry cached))
            {
                return new Playable(cached);
            }

            VfxCatalogEntry row = Build(usage);
            _rows[usage] = row;   // null cached too: the lookup does not repeat per shot
            return new Playable(row);
        }

        private VfxCatalogEntry Build(string usage)
        {
            SpecialWeaponVisualLibrary library = SpecialWeaponVisualLibrary.Current;
            if (library == null)
            {
                return null;
            }

            SpecialWeaponVisualLibrary.Sheet sheet = library.FindSheet(usage);
            if (sheet == null || sheet.texture == null)
            {
                return null;
            }

            if (!_shaderTried)
            {
                _shaderTried = true;
                _shader = PrototypeShaders.VfxFlipbook;
            }

            if (_shader == null)
            {
                return null;
            }

            var m = new Material(_shader) { name = "M_sw_" + usage + " (runtime)" };
            m.SetTexture("_Sheet", sheet.texture);
            m.SetFloat("_Cols", Mathf.Max(1, sheet.columns));
            m.SetFloat("_Rows", Mathf.Max(1, sheet.rows));
            m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", 1f);
            m.SetFloat("_AlphaCap", 1f);
            m.SetFloat("_UseEmissive", 0f);
            m.SetFloat("_ScrollV", 0f);
            // Kagura ring / Ofuda collapse are greyscale + alpha glows → additive; the talisman card and the thread are alpha.
            bool additive = usage == VfxIds.SwKaguraWave || usage == VfxIds.SwOfudaCollapse;
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (additive ? 10 : 0);
            MaterialsCreated++;

            return new VfxCatalogEntry
            {
                Id = usage,
                Family = VfxFamily.Weapon,
                Kind = VfxKind.Flipbook,
                SourceFile = "AP-014 " + usage,
                ManifestFile = "special-weapon-visuals",
                Texture = sheet.texture,
                Material = m,
                Columns = Mathf.Max(1, sheet.columns),
                Rows = Mathf.Max(1, sheet.rows),
                FramesUsed = Mathf.Max(1, Mathf.Min(sheet.columns * sheet.rows, sheet.columns * sheet.rows)),
                Fps = sheet.fps,
                DurationSeconds = sheet.durationSeconds,
                Loop = sheet.loop,
                WorldSizeMetres = sheet.worldSizeMetres,
                WorldHeightMetres = sheet.worldSizeMetres,
                Additive = additive,
                OriginUv = new Vector2(0.5f, 0.5f),
                ColourReserve = VfxColourReserve.Beacon
            };
        }

        /// <summary>Test seam: forget the cached rows (a test-built library).</summary>
        public void Reset()
        {
            _rows.Clear();
        }
    }
}
