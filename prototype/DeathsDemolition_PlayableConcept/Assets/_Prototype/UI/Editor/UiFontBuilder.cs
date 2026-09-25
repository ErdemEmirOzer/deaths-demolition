using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// WP-068 slice 3 · the three role faces as static SDF font assets (direction §2.2):
    ///
    ///   Display  Shippori Mincho B1 700 · 1024×512 @ 64 pt, padding 8 · caps, digits, punctuation
    ///   Text     Source Sans 3 400      · 1024×512 @ 44 pt, padding 6 · Latin-1 + typographic marks
    ///   Numeric  Oswald 500             ·  512×512 @ 64 pt, padding 12 · digits + ": / % + − × . , x °"
    ///
    /// Sources are the OFL TTFs under Assets/_Prototype/UI/Art/Fonts/&lt;Face&gt;/ (outside
    /// Resources, licence beside each); the assets land in Art/Resources/UI/Fonts/ as
    /// "Display SDF", "Text SDF", "Numeric SDF" (the names UiFonts loads). Steps per face:
    /// create dynamic → add the charset → Numeric: every digit gets the widest digit's advance,
    /// centred (tabular figures without &lt;mspace&gt;) → kerning from kerning.json beside the TTF
    /// (tools/gpos_pairs.py for GPOS faces; hand-authored caps pairs for Shippori, which has no
    /// pair kerning) → static (Player never rasterises) → atlas + material saved as sub-assets
    /// → an "- Outline" material preset (InkStroke). Text bold is TMP's SDF weight (one atlas;
    /// a Semibold atlas would break the 1.5 MB ceiling).
    ///
    /// Batch: -executeMethod DD.Prototype.UI.EditorTools.UiFontBuilder.BuildAll
    /// </summary>
    public static class UiFontBuilder
    {
        public const string SourceRoot = "Assets/_Prototype/UI/Art/Fonts";
        public const string OutputRoot = "Assets/_Prototype/UI/Art/Resources/UI/Fonts";

        // "_" and "…": TMP asks every font for its underline and ellipsis glyphs when a label takes
        // the font; a static atlas without them logs a warning per label (p3: 4 870 warnings).
        public const string DisplayCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:;!?'\"-–—()&/·×+%#’‘“”…_";
        // 12b (AD R1-A SHOULD-7): "°" - STICK LOOK SPEED "180°/s", the FOV rows and the ACCURACY stat print
        // their unit in the Numeric face; without it the sign fell back to the Text face (Oswald has U+00B0).
        public const string NumericCharset = "0123456789 :/%+−-×.,x_…°";
        public const char DegreeSign = '°';
        public const string Digits = "0123456789";

        public static string TextCharset
        {
            get
            {
                var chars = new System.Text.StringBuilder();
                for (int c = 0x20; c < 0x7F; c++) chars.Append((char)c);
                for (int c = 0xA0; c <= 0xFF; c++) chars.Append((char)c);
                chars.Append("–—‘’“”•…·×→←↑↓−");
                return chars.ToString();
            }
        }

        private struct Face
        {
            public string Role;
            public string Folder;
            public string File;
            public int PointSize;
            public int Padding;
            public int Width;
            public int Height;
            public string Charset;
            public bool TabularDigits;

            /// <summary>&gt; 0: replace the font's line box (em) - the atlas holds no glyph that needs the original one.</summary>
            public float AscentEm;
            public float DescentEm;
        }

        private static readonly Face[] Faces =
        {
            // Shippori's hhea box (1.160 / −0.288 em) is sized for CJK; the caps-only Display atlas uses
            // the font's own Latin typo metrics (0.880 / −0.120), so a caps heading is not pushed down
            // under an empty kanji ascender and its band is 1.0 em (LineHeight reads it).
            new Face { Role = "Display", Folder = "ShipporiMinchoB1", File = "ShipporiMinchoB1-Bold.ttf", PointSize = 64, Padding = 8, Width = 1024, Height = 512, Charset = DisplayCharset, AscentEm = 0.88f, DescentEm = -0.12f },
            new Face { Role = "Text", Folder = "SourceSans3", File = "SourceSans3-Regular.ttf", PointSize = 44, Padding = 6, Width = 1024, Height = 512, Charset = null },
            // Oswald's box (1.193 / −0.289 em) makes room for accented capitals; the Numeric atlas holds
            // digits and a few signs (cap height 0.810): a 0.90 / −0.18 box (1.08 em) keeps the figures
            // clear and fits the HUD's existing numeric bands (NumericL 32 in 36 px) - at 1.16 em the
            // round number was dropped by Truncate (f3).
            new Face { Role = "Numeric", Folder = "Oswald", File = "Oswald-Medium.ttf", PointSize = 64, Padding = 12, Width = 512, Height = 512, Charset = NumericCharset, TabularDigits = true, AscentEm = 0.9f, DescentEm = -0.18f },
        };

        [MenuItem("Tools/Deaths Demolition/UI/Build Font Assets (WP-068)")]
        public static void BuildAll()
        {
            var report = new System.Text.StringBuilder("[UiFontBuilder]");
            foreach (Face face in Faces)
            {
                report.Append('\n').Append(Build(face));
            }

            // Display (caps) and Numeric (digits) fall back to the Text face, not to TMP's global
            // LiberationSans: a word in a numeric label ("LEVEL 1") or lowercase in a heading stays
            // in the product's own sans (f3: "LEVEL" drew in LiberationSans).
            var text = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputRoot + "/Text SDF.asset");
            foreach (string role in new[] { "Display", "Numeric" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputRoot + "/" + role + " SDF.asset");
                if (asset != null && text != null)
                {
                    asset.fallbackFontAssetTable = new List<TMP_FontAsset> { text };
                    EditorUtility.SetDirty(asset);
                    report.Append('\n').Append(asset.name).Append(" → fallback Text SDF");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        private static string Build(Face face)
        {
            string source = SourceRoot + "/" + face.Folder + "/" + face.File;
            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            if (font == null)
            {
                throw new InvalidOperationException("Font source missing: " + source);
            }

            string charset = face.Charset ?? TextCharset;
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, face.PointSize, face.Padding, GlyphRenderMode.SDFAA,
                face.Width, face.Height, AtlasPopulationMode.Dynamic, false);
            asset.name = face.Role + " SDF";
            string missing;
            asset.TryAddCharacters(charset, out missing);

            if (face.TabularDigits)
            {
                EqualiseDigits(asset);
            }

            if (face.AscentEm > 0f)
            {
                FaceInfo info = asset.faceInfo;
                float pt = info.pointSize;
                info.ascentLine = face.AscentEm * pt;
                info.descentLine = face.DescentEm * pt;
                info.lineHeight = (face.AscentEm - face.DescentEm) * pt;
                asset.faceInfo = info;
                asset.ReadFontAssetDefinition();
            }

            int pairs = AddKerning(asset, SourceRoot + "/" + face.Folder + "/kerning.json", face.TabularDigits);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            Directory.CreateDirectory(OutputRoot);
            string path = OutputRoot + "/" + asset.name + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            Texture2D atlas = asset.atlasTexture;
            atlas.name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
            Material material = asset.material;
            material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(material, asset);
            EditorUtility.SetDirty(asset);

            // "- Outline" preset: the InkStroke outline (TMP lists presets whose names start with the asset name).
            string presetPath = OutputRoot + "/" + asset.name + " - Outline.mat";
            AssetDatabase.DeleteAsset(presetPath);
            var outline = new Material(material) { name = asset.name + " - Outline" };
            outline.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            outline.SetColor(ShaderUtilities.ID_OutlineColor, UiTokens.Get(UiColor.InkStroke));
            outline.EnableKeyword("OUTLINE_ON");
            AssetDatabase.CreateAsset(outline, presetPath);

            long bytes = (long)atlas.width * atlas.height; // Alpha8
            return string.Format("{0}: {1} glyphs, missing '{2}', {3} kerning pairs, atlas {4}×{5} {6:0.00} MB", asset.name,
                asset.characterTable.Count, missing, pairs, atlas.width, atlas.height, bytes / (1024f * 1024f));
        }

        /// <summary>Every digit gets the widest digit's advance, its outline centred in the wider cell (tabular figures).</summary>
        private static void EqualiseDigits(TMP_FontAsset asset)
        {
            float widest = 0f;
            var glyphs = new List<Glyph>(10);
            foreach (char c in Digits)
            {
                TMP_Character ch;
                if (asset.characterLookupTable.TryGetValue(c, out ch))
                {
                    glyphs.Add(ch.glyph);
                    widest = Mathf.Max(widest, ch.glyph.metrics.horizontalAdvance);
                }
            }

            foreach (Glyph glyph in glyphs)
            {
                GlyphMetrics m = glyph.metrics;
                float extra = widest - m.horizontalAdvance;
                glyph.metrics = new GlyphMetrics(m.width, m.height, m.horizontalBearingX + extra * 0.5f, m.horizontalBearingY, widest);
            }

            asset.ReadFontAssetDefinition();
        }

        [Serializable]
        private sealed class KerningFile
        {
            public int unitsPerEm = 1000;
            public KerningPair[] pairs;
        }

        [Serializable]
        private sealed class KerningPair
        {
            public string first;
            public string second;
            public int xAdvance;
        }

        /// <summary>Pairs from kerning.json (font units) → TMP glyph pair records (points at the sampling size). Digit pairs are skipped on tabular faces.</summary>
        private static int AddKerning(TMP_FontAsset asset, string jsonPath, bool tabular)
        {
            if (!File.Exists(jsonPath))
            {
                return 0;
            }

            var file = JsonUtility.FromJson<KerningFile>(File.ReadAllText(jsonPath));
            float scale = asset.faceInfo.pointSize / (float)Mathf.Max(1, file.unitsPerEm);
            var records = asset.fontFeatureTable.glyphPairAdjustmentRecords;
            records.Clear();
            int added = 0;
            foreach (KerningPair pair in file.pairs)
            {
                if (string.IsNullOrEmpty(pair.first) || string.IsNullOrEmpty(pair.second) || pair.xAdvance == 0)
                {
                    continue;
                }

                if (tabular && (char.IsDigit(pair.first[0]) || char.IsDigit(pair.second[0])))
                {
                    continue;
                }

                TMP_Character a, b;
                if (!asset.characterLookupTable.TryGetValue(pair.first[0], out a) || !asset.characterLookupTable.TryGetValue(pair.second[0], out b))
                {
                    continue;
                }

                var first = new TMP_GlyphAdjustmentRecord(a.glyphIndex, new TMP_GlyphValueRecord(0f, 0f, pair.xAdvance * scale, 0f));
                var second = new TMP_GlyphAdjustmentRecord(b.glyphIndex, new TMP_GlyphValueRecord(0f, 0f, 0f, 0f));
                records.Add(new TMP_GlyphPairAdjustmentRecord(first, second));
                added++;
            }

            asset.fontFeatureTable.SortGlyphPairAdjustmentRecords();
            asset.ReadFontAssetDefinition();
            return added;
        }
    }
}
