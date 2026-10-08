using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.UI;
using DD.Prototype.UI.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10, S3-25/S3-29) · the baked logo in the project: the A8 time map's contract (inked texel
    /// ≤ 254, edge 255, L == A), the importer (time map linear Alpha8 uncompressed, colour sRGB BC7 full-rect, no mips,
    /// Clamp), the intake record (UI/Art/logo-intake.json: every file's bytes + sha256, no orphan, the bake script's sha256)
    /// and the runtime constants against the motion tables the bake copied from motion-spec v003.
    /// </summary>
    [TestFixture]
    [Category("AP035")]
    public sealed class UiLogoAssetTests
    {
        private const string Art = "Assets/_Prototype/UI/Art/";
        private const string IntakePath = Art + "logo-intake.json";
        private const string LogoDir = Art + "Resources/UI/Logo/";
        private const string MarkDir = Art + "Resources/UI/LogoMark/";
        private const string TimeMapFile = LogoDir + "ap035_logo_wordmark_timemap_2048x768.png";
        private const string WordmarkFile = LogoDir + "ap035_logo_wordmark_2048x768.png";

        [Serializable] private sealed class IntakeFile { public string role, file, folder, @class, sha256, mode; public long bytes; public int[] size; }
        [Serializable] private sealed class Bake { public string script, sha256; }
        [Serializable] private sealed class Intake { public string schema, package; public Bake bake; public IntakeFile[] files; }

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static Intake Load()
        {
            Assert.IsTrue(File.Exists(IntakePath), IntakePath);
            Intake intake = JsonUtility.FromJson<Intake>(File.ReadAllText(IntakePath));
            Assert.AreEqual("dd-logo-intake-v1", intake.schema);
            return intake;
        }

        [Test]
        public void ZamanHaritasi_MurekkepliTexel254VeAlti_Kenar255_LEsitA()
        {
            var map = new Texture2D(2, 2);
            var art = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(map.LoadImage(File.ReadAllBytes(TimeMapFile)), TimeMapFile);
                Assert.IsTrue(art.LoadImage(File.ReadAllBytes(WordmarkFile)), WordmarkFile);
                Assert.AreEqual(2048, map.width);
                Assert.AreEqual(768, map.height);
                Assert.AreEqual(map.width, art.width, "one rect: the map and the colour art share a size");
                Assert.AreEqual(map.height, art.height);
                Color32[] m = map.GetPixels32();
                Color32[] a = art.GetPixels32();
                int inked = 0, never = 0, maxInked = 0, mismatch = 0;
                for (int i = 0; i < m.Length; i++)
                {
                    if (m[i].r != m[i].a)
                    {
                        mismatch++;
                    }

                    if (m[i].a == 255) never++;
                    else maxInked = Math.Max(maxInked, m[i].a);
                    if (a[i].a > 0)
                    {
                        inked++;
                        Assert.LessOrEqual(m[i].a, 254, "an inked texel (" + (i % map.width) + "," + (i / map.width) + ") never reads 'never'");
                    }
                }

                for (int x = 0; x < map.width; x++)
                {
                    Assert.AreEqual(255, m[x].a, "bottom edge never inks");
                    Assert.AreEqual(255, m[(map.height - 1) * map.width + x].a, "top edge never inks");
                }

                Debug.Log("[DD-TEST] ap035.timemap inked=" + inked + " never=" + never + " maxInked=" + maxInked);
                Assert.AreEqual(0, mismatch, "L == A (the shader reads .a)");
                Assert.Greater(inked, 0);
                Assert.LessOrEqual(maxInked, 254);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(map);
                UnityEngine.Object.DestroyImmediate(art);
            }
        }

        [Test]
        public void Ice_Alma_ZamanHaritasiDogrusalAlpha8_RenkSrgbBc7_MipYok()
        {
            var map = (TextureImporter)AssetImporter.GetAtPath(TimeMapFile);
            Assert.IsNotNull(map, "time map imported");
            Assert.IsFalse(map.sRGBTexture, "time map: linear (codes are data)");
            Assert.IsFalse(map.alphaIsTransparency);
            Assert.IsFalse(map.mipmapEnabled);
            Assert.AreEqual(TextureWrapMode.Clamp, map.wrapMode);
            Assert.AreEqual(TextureImporterType.Default, map.textureType);
            Assert.AreEqual(TextureImporterFormat.Alpha8, map.GetPlatformTextureSettings("Standalone").format, "Alpha8");
            Texture2D mapTex = Resources.Load<Texture2D>(UiLogoAssets.TimeMapResource);
            Assert.IsNotNull(mapTex, "time map from Resources (Player)");
            Assert.AreEqual(TextureFormat.Alpha8, mapTex.format);
            Assert.AreEqual(1, mapTex.mipmapCount);
            Assert.AreEqual(FilterMode.Bilinear, mapTex.filterMode);

            foreach (string file in new[] { WordmarkFile, LogoDir + "ap035_logo_wordmark_halo_1024x384.png" })
            {
                var colour = (TextureImporter)AssetImporter.GetAtPath(file);
                Assert.IsNotNull(colour, file);
                Assert.IsTrue(colour.sRGBTexture, file + ": sRGB");
                Assert.AreEqual(TextureImporterType.Sprite, colour.textureType, file);
                Assert.IsFalse(colour.mipmapEnabled, file);
                Assert.AreEqual(TextureImporterFormat.BC7, colour.GetPlatformTextureSettings("Standalone").format, file);
                var settings = new TextureImporterSettings();
                colour.ReadTextureSettings(settings);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, file + ": full rect (the time map's uv = the rect's)");
            }

            foreach (string resource in new[] { UiLogoAssets.WordmarkResource, UiLogoAssets.HaloResource, UiLogoAssets.SealResource, UiLogoAssets.GlowResource,
                         UiLogoAssets.BlotResource, UiLogoAssets.RingResource, UiLogoAssets.BackingResource })
            {
                Sprite sprite = Resources.Load<Sprite>(resource);
                Assert.IsNotNull(sprite, resource + " from Resources (Player)");
                Assert.AreEqual(0, sprite.texture.width % 4, resource + ": a multiple of 4 (BC7 blocks)");
                Assert.AreEqual(0, sprite.texture.height % 4, resource + ": a multiple of 4 (BC7 blocks)");
            }
        }

        [Test]
        public void PisirmeKaydi_DosyalarBaytVeSha256_YetimYok_BetikSha()
        {
            Intake intake = Load();
            Assert.AreEqual("AP-035", intake.package);
            var recorded = new HashSet<string>(StringComparer.Ordinal);
            foreach (IntakeFile f in intake.files)
            {
                string path = Art + "Resources/" + f.folder + "/" + f.file;
                Assert.IsTrue(File.Exists(path), path);
                Assert.AreEqual(f.bytes, new FileInfo(path).Length, path + ": bytes");
                Assert.AreEqual(f.sha256, Sha(path), path + ": sha256");
                recorded.Add(path);
            }

            Assert.AreEqual(8, intake.files.Length, "8 runtime textures");
            foreach (string dir in new[] { LogoDir, MarkDir })
            {
                foreach (string png in Directory.GetFiles(dir, "*.png"))
                {
                    Assert.IsTrue(recorded.Contains(png.Replace('\\', '/')), "orphan, not in the bake record: " + png);
                }
            }

            string script = Path.Combine(RepoRoot, intake.bake.script);
            Assert.IsTrue(File.Exists(script), script);
            Assert.AreEqual(intake.bake.sha256, Sha(script), "the record was written by the bake script in the repo (re-run bake --install after editing it)");
        }

        [Test]
        public void HareketVerisi_CalismaZamaniSabitleriMotionSpecIleAyni()
        {
            string json = File.ReadAllText(IntakePath);
            var motion = JsonUtility.FromJson<MotionHolder>(json).motion;
            Assert.AreEqual(24, motion.fps);
            Assert.AreEqual(UiLogoTimeline.IntroFrames, motion.intro_frames);
            Assert.AreEqual(UiLogoTimeline.LoopFrames, motion.loop_frames);
            CollectionAssert.AreEqual(new[] { 3, 47 }, motion.window);
            CollectionAssert.AreEqual(new[] { 55, 67 }, motion.canonical_hold);
            Assert.AreEqual(UiLogoTimeline.SealFirstFrame, motion.seal_first_frame);
            Assert.AreEqual(UiLogoTimeline.SealRestBottom, motion.seal_rest_bottom_y, 1e-4f);
            Assert.AreEqual(UiLogoTimeline.RingBreathPeakAlpha, motion.ring_loop_breath_alpha_peak, 1e-5f);
            Assert.AreEqual(UiLogoTimeline.LoopGlowPeakAlpha, motion.loop_glow_peak_alpha, 1e-5f);
            Assert.AreEqual(UiLogoTimeline.RingSizeStart.x, motion.ring_size_start[0], 1e-4f);
            Assert.AreEqual(UiLogoTimeline.RingSizeStart.y, motion.ring_size_start[1], 1e-4f);
            Assert.AreEqual(UiLogoTimeline.RingSizeEnd.x, motion.ring_size_end[0], 1e-4f);
            Assert.AreEqual(UiLogoTimeline.RingSizeEnd.y, motion.ring_size_end[1], 1e-4f);
            Assert.AreEqual(UiLogoAnimator.AshCount, motion.ash_sprites);
            Assert.AreEqual(UiLogoAnimator.SparkCount, motion.sparks);
            Assert.AreEqual(-UiLogoAnimator.AshDrift, motion.ash_loop_drift_y, 1e-4f);

            // seal samples: frame, y, sx, sy, glow (flattened by JsonUtility as rows)
            string[] rows = Rows(json, "seal_samples_frame_y_sx_sy_glow");
            Assert.AreEqual(16, rows.Length, "frames 39..54");
            for (int i = 0; i < rows.Length; i++)
            {
                float[] v = Floats(rows[i]);
                Assert.AreEqual(UiLogoTimeline.SealFirstFrame + i, (int)v[0]);
                Assert.AreEqual(UiLogoTimeline.SealY[i], v[1], 1e-4f, "y frame " + v[0]);
                Assert.AreEqual(UiLogoTimeline.SealSx[i], v[2], 1e-4f, "sx frame " + v[0]);
                Assert.AreEqual(UiLogoTimeline.SealSy[i], v[3], 1e-4f, "sy frame " + v[0]);
                Assert.AreEqual(UiLogoTimeline.SealGlow[i], v[4], 1e-4f, "glow frame " + v[0]);
            }

            string[] shake = Rows(json, "camera_shake_frame_dx_dy");
            Assert.AreEqual(UiLogoTimeline.ShakeX.Length, shake.Length);
            for (int i = 0; i < shake.Length; i++)
            {
                float[] v = Floats(shake[i]);
                Assert.AreEqual(UiLogoTimeline.ShakeFirstFrame + i, (int)v[0]);
                Assert.AreEqual(UiLogoTimeline.ShakeX[i], v[1], 1e-4f);
                Assert.AreEqual(UiLogoTimeline.ShakeY[i], v[2], 1e-4f);
            }

            // placement (spec px) - the animator's constants
            var placement = JsonUtility.FromJson<PlacementHolder>(json).placement;
            Assert.AreEqual(placement.seal.sprite_spec_px, UiLogoAnimator.SealSpriteSize, 0.01f);
            Assert.AreEqual(placement.glow.size_spec_px[0], UiLogoAnimator.GlowSize.x, 0.01f);
            Assert.AreEqual(placement.glow.centre_offset_spec_px[0], UiLogoAnimator.GlowOffset.x, 0.01f);
            Assert.AreEqual(placement.glow.centre_offset_spec_px[1], UiLogoAnimator.GlowOffset.y, 0.01f);
            Assert.AreEqual(placement.blot.size_spec_px[0], UiLogoAnimator.BlotSize.x, 0.01f);
            Assert.AreEqual(placement.blot.centre_offset_spec_px[0], UiLogoAnimator.BlotOffset.x, 0.01f);
            Assert.AreEqual(placement.blot.centre_offset_spec_px[1], UiLogoAnimator.BlotOffset.y, 0.01f);
            Assert.AreEqual(placement.ring.sprite_per_art[0], UiLogoAnimator.RingSpriteWidthPerArt, 1e-4f);
        }

        [Serializable] private sealed class Motion
        {
            public int fps, intro_frames, loop_frames, seal_first_frame, ash_sprites, sparks;
            public int[] window, canonical_hold;
            public float seal_rest_bottom_y, ring_loop_breath_alpha_peak, loop_glow_peak_alpha, ash_loop_drift_y;
            public float[] ring_size_start, ring_size_end;
        }

        [Serializable] private sealed class MotionHolder { public Motion motion; }
        [Serializable] private sealed class Box { public float sprite_spec_px; public float[] size_spec_px, centre_offset_spec_px, sprite_per_art; }
        [Serializable] private sealed class Placement { public Box seal, glow, blot, ring; }
        [Serializable] private sealed class PlacementHolder { public Placement placement; }

        /// <summary>The inner arrays of a JSON array of arrays (JsonUtility cannot read nested arrays).</summary>
        private static string[] Rows(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            Assert.GreaterOrEqual(k, 0, key);
            int open = json.IndexOf('[', k);
            var rows = new List<string>();
            int depth = 0, start = -1;
            for (int i = open; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[')
                {
                    depth++;
                    if (depth == 2) start = i + 1;
                }
                else if (c == ']')
                {
                    if (depth == 2) rows.Add(json.Substring(start, i - start));
                    depth--;
                    if (depth == 0) break;
                }
            }

            return rows.ToArray();
        }

        private static float[] Floats(string row)
        {
            string[] parts = row.Split(',');
            var v = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                v[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }

            return v;
        }

        private static string Sha(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream s = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
