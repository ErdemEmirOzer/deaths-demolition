using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using B = DD.Art.Pipeline.EditorTools.ArtAp040WallBuyBuilder;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-107B (R4) · wall buy art gate - the BUILT state (<c>ArtAp040WallBuyBuilder.BuildForBatch</c> has run and its output is
    /// committed): the AP-040 / AP-042 copies still match their provenance sha256, the three station FBX import without collider /
    /// animation / embedded material and with the slots remapped onto the two project materials, the shared atlas and the decals
    /// carry their colour space, size and Standalone compression, the library holds the three models (sockets, the box proxy, three
    /// LOD levels, the measured front) and one decal per enabled classic weapon, and the AP-042 HUD sprites stay in the UI class
    /// (Sprite, one mip, ≤ 1024) with the delivery's nine-slice borders. Every new file has a .meta whose GUID nobody else uses
    /// (the Assets-wide scan is ArtAssetGateTests.GuidTekligi_ArtIcindeVeAssetsGeneliCakismaYok).
    /// Nothing here writes an asset: importers are read only.
    /// </summary>
    [TestFixture]
    [Category("WP107")]
    public sealed class Wp107bWallBuyArtGateTests
    {
        private const string HudResources = "UI/Hud/";

        /// <summary>AP-042 nine-slice.json (v001 = v002 for these): L, B, R, T px. Written here independently of the intake JSON.</summary>
        private static readonly (string name, Vector4 border)[] HudSprites =
        {
            ("ap042_wallbuy_frame_purchase", new Vector4(96, 48, 96, 48)),
            ("ap042_wallbuy_frame_insufficient", new Vector4(96, 48, 96, 48)),
            ("ap042_wallbuy_price_tag", new Vector4(144, 40, 48, 40)),
            ("ap042_wallbuy_icon_price", Vector4.zero),
            ("ap042_wallbuy_icon_ammo", Vector4.zero),
            ("ap042_wallbuy_icon_insufficient", Vector4.zero),
            ("ap042_wallbuy_seal_discount", Vector4.zero),
            ("ap042_wallbuy_icon_price_64", Vector4.zero),
            ("ap042_wallbuy_icon_ammo_64", Vector4.zero),
            ("ap042_wallbuy_icon_insufficient_64", Vector4.zero),
        };

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, "varlık yok: " + path + " (ArtAp040WallBuyBuilder.BuildForBatch çalıştı mı?)");
            return asset;
        }

        // ------------------------------------------------------------------ provenans

        [Test]
        public void Provenans_HerDosyaninBoyutuVeShaDegeriDisktekiyleAyni()
        {
            B.Provenance provenance = B.ReadProvenance();
            B.HudIntake hud = B.ReadHudIntake();
            Assert.AreEqual("AP-040", provenance.package);
            Assert.AreEqual("AP-042", hud.package);
            Assert.AreEqual(17, provenance.files.Length, "AP-040: 3 FBX + 4 ortak atlas + 10 decal");
            Assert.AreEqual(10, hud.files.Length, "AP-042: 7 teslim dosyası + 3 türetilmiş 64 px ikon");

            var listed = new HashSet<string>(StringComparer.Ordinal);
            var faults = new List<string>();
            foreach (B.ProvenanceFile f in provenance.files) Check(f.repo, f.bytes, f.sha256, listed, faults);
            foreach (B.HudIntakeFile f in hud.files) Check(f.repo, f.bytes, f.sha256, listed, faults);

            // No unlisted art next to the listed copies (the shared atlas folder is S-05's too and is not swept).
            foreach (string folder in new[] { B.Root + "/Models", B.Root + "/Decals" })
            {
                foreach (string file in Directory.GetFiles(folder))
                {
                    string p = file.Replace('\\', '/');
                    if (p.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    if (!listed.Contains(p)) faults.Add("provenansta olmayan dosya: " + p);
                }
            }

            foreach (string file in Directory.GetFiles(B.HudFolder, "ap042_wallbuy_*.png"))
            {
                string p = file.Replace('\\', '/');
                if (!listed.Contains(p)) faults.Add("intake JSON'da olmayan HUD dosyası: " + p);
            }

            Assert.IsEmpty(faults, "provenans uyuşmazlığı (" + faults.Count + "):\n" + string.Join("\n", faults));
        }

        private static void Check(string repo, long bytes, string sha, HashSet<string> listed, List<string> faults)
        {
            if (!listed.Add(repo)) faults.Add("iki kez listelenmiş: " + repo);
            if (!File.Exists(repo))
            {
                faults.Add("dosya yok: " + repo);
                return;
            }

            long size = new FileInfo(repo).Length;
            if (size != bytes) faults.Add(repo + ": " + size + " bayt, provenans " + bytes + " (LFS işaretçisi mi?)");
            else if (!string.Equals(B.Sha256(repo), sha, StringComparison.OrdinalIgnoreCase)) faults.Add(repo + ": sha256 provenansla uyuşmuyor");
        }

        // ------------------------------------------------------------------ FBX

        [TestCase(B.StandardKey)]
        [TestCase(B.PremiumKey)]
        [TestCase(B.WallMountKey)]
        public void Fbx_ImportAyarlari_ColliderAnimasyonYok_SlotlarProjeMalzemelerine(string key)
        {
            string path = B.ModelPath(key);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.IsNotNull(importer, "model importer yok: " + path);
            Assert.AreEqual(1f, importer.globalScale, key + ": globalScale");
            Assert.IsTrue(importer.useFileScale, key + ": dosya ölçeği");
            Assert.IsFalse(importer.bakeAxisConversion, key + ": eksen dönüşümü pişirilmez (AP-021 kuralı)");
            Assert.AreEqual(ModelImporterNormals.Import, importer.importNormals, key + ": normaller içe alınır");
            Assert.AreEqual(ModelImporterTangents.CalculateMikk, importer.importTangents, key + ": Mikk tanjant");
            Assert.IsFalse(importer.addCollider, key + ": otomatik collider olmaz (vekil kutu çalışma anında BoxCollider olur)");
            Assert.IsFalse(importer.importAnimation, key + ": animasyon yok");
            Assert.AreEqual(ModelImporterAnimationType.None, importer.animationType, key + ": rig yok");
            Assert.IsTrue(importer.preserveHierarchy, key + ": hiyerarşi korunur (SOCKET_ boşlukları)");
            Assert.IsFalse(importer.isReadable, key + ": okunabilir değil");
            Assert.IsFalse(importer.importCameras || importer.importLights || importer.importBlendShapes, key + ": kamera / ışık / blend shape yok");
            Assert.IsFalse(importer.generateSecondaryUV, key + ": ikincil UV üretilmez");

            Material shared = Load<Material>(B.SharedMaterialPath);
            Material glow = Load<Material>(B.GlowMaterialPath);
            Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = importer.GetExternalObjectMap();
            foreach ((string slot, Material target) in new[] { (B.SharedSlot, shared), (B.GlowSlot, glow), (B.ProxySlot, shared) })
            {
                Assert.IsTrue(map.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), out UnityEngine.Object mapped),
                    key + ": " + slot + " slotu eşlenmemiş");
                Assert.AreSame(target, mapped, key + ": " + slot + " → " + target.name + " olmalı");
            }

            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                Assert.IsFalse(sub is Material || sub is Texture2D, key + ": FBX içine gömülü alt varlık: " + sub.GetType().Name + " " + sub.name);
            }

            foreach (MeshRenderer r in Load<GameObject>(path).GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    Assert.IsTrue(m == shared || m == glow, key + ": " + r.name + " slotu proje malzemesi değil (" + (m != null ? m.name : "null") + ")");
                }
            }
        }

        // ------------------------------------------------------------------ dokular

        [Test]
        public void Dokular_RenkUzayiBoyutVeStandaloneSikistirma()
        {
            foreach (string map in B.SharedMaps)
            {
                string path = B.SharedTexturePath(map);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, "doku importer yok: " + path);
                bool normal = map == "Normal";
                if (normal)
                {
                    Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, map + ": normal map");
                    Assert.IsFalse(importer.flipGreenChannel, map + ": OpenGL Y+, yeşil çevrilmez");
                }
                else
                {
                    Assert.AreEqual(TextureImporterType.Default, importer.textureType, map);
                    Assert.AreEqual(map != "MaskMap", importer.sRGBTexture, map + (map == "MaskMap" ? ": doğrusal (sRGB kapalı)" : ": sRGB"));
                }

                Assert.IsTrue(importer.mipmapEnabled && importer.streamingMipmaps, map + ": mip + streaming mip");
                Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode, map + ": atlas Clamp");
                Assert.AreEqual(B.SharedMaxSize, importer.maxTextureSize, map + ": en çok 2048");
                Assert.IsFalse(importer.isReadable, map + ": okunabilir değil");
                AssertStandalone(importer, normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7, B.SharedMaxSize, map);
            }

            foreach (string weapon in B.DecalWeapons)
            {
                string path = B.DecalTexturePath(weapon);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, "decal importer yok: " + path);
                Assert.AreEqual(TextureImporterType.Default, importer.textureType, weapon);
                Assert.IsTrue(importer.sRGBTexture, weapon + ": sRGB");
                Assert.IsTrue(importer.alphaIsTransparency, weapon + ": alfa saydamlıktır");
                Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode, weapon + ": Clamp");
                Assert.AreEqual(FilterMode.Bilinear, importer.filterMode, weapon + ": Bilinear");
                Assert.IsTrue(importer.mipmapEnabled, weapon + ": mip açık (dünya dokusu)");
                Assert.AreEqual(B.DecalMaxSize, importer.maxTextureSize, weapon + ": en çok 512");
                AssertStandalone(importer, TextureImporterFormat.BC7, B.DecalMaxSize, weapon);
                Texture2D t = Load<Texture2D>(path);
                Assert.LessOrEqual(Mathf.Max(t.width, t.height), B.DecalMaxSize, weapon + ": içe alınan boyut");
            }
        }

        private static void AssertStandalone(TextureImporter importer, TextureImporterFormat format, int maxSize, string label)
        {
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            Assert.IsTrue(standalone.overridden, label + ": Standalone geçersiz kılma yok");
            Assert.AreEqual(format, standalone.format, label + ": Standalone biçimi");
            Assert.LessOrEqual(standalone.maxTextureSize, maxSize, label + ": Standalone en büyük boyut");
        }

        // ------------------------------------------------------------------ malzemeler

        [Test]
        public void Malzemeler_LitOrtakVeGlow_UnlitSeffafDecal()
        {
            Material shared = Load<Material>(B.SharedMaterialPath);
            Material glow = Load<Material>(B.GlowMaterialPath);
            Texture2D baseMap = Load<Texture2D>(B.SharedTexturePath("BaseColor"));
            Texture2D normal = Load<Texture2D>(B.SharedTexturePath("Normal"));
            Texture2D mask = Load<Texture2D>(B.SharedTexturePath("MaskMap"));
            foreach (Material m in new[] { shared, glow })
            {
                Assert.AreEqual(B.LitShaderName, m.shader.name, m.name + ": URP Lit");
                Assert.AreSame(baseMap, m.GetTexture("_BaseMap"), m.name + ": _BaseMap");
                Assert.AreSame(normal, m.GetTexture("_BumpMap"), m.name + ": _BumpMap");
                Assert.AreSame(mask, m.GetTexture("_MetallicGlossMap"), m.name + ": MaskMap → metalik (R) / pürüzsüzlük (A)");
                Assert.AreSame(mask, m.GetTexture("_OcclusionMap"), m.name + ": MaskMap → AO (G)");
                Assert.AreEqual(0f, m.GetFloat("_SmoothnessTextureChannel"), m.name + ": pürüzsüzlük metalik alfadan");
                Assert.AreEqual(0f, m.GetFloat("_Surface"), m.name + ": opak");
                foreach (string keyword in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" })
                {
                    Assert.IsTrue(m.IsKeywordEnabled(keyword), m.name + ": " + keyword + " kapalı");
                }
            }

            Assert.IsFalse(shared.IsKeywordEnabled("_EMISSION"), "M_AP040_Shared ışımaz");
            Assert.IsTrue(glow.IsKeywordEnabled("_EMISSION"), "M_AP040_Glow ışır");
            Assert.AreSame(Load<Texture2D>(B.SharedTexturePath("Emission")), glow.GetTexture("_EmissionMap"), "glow: ışıma atlası");
            Assert.Greater(glow.GetColor("_EmissionColor").maxColorComponent, 0.5f, "glow: ışıma rengi siyah olmamalı");

            foreach (string weapon in B.DecalWeapons)
            {
                Material m = Load<Material>(B.DecalMaterialPath(weapon));
                Assert.AreEqual(B.UnlitShaderName, m.shader.name, weapon + ": URP Unlit");
                Assert.AreSame(Load<Texture2D>(B.DecalTexturePath(weapon)), m.GetTexture("_BaseMap"), weapon + ": decal dokusu");
                Assert.AreEqual(1f, m.GetFloat("_Surface"), weapon + ": saydam yüzey");
                Assert.AreEqual((int)RenderQueue.Transparent, m.renderQueue, weapon + ": Transparent kuyruğu");
                Assert.AreEqual(0f, m.GetFloat("_ZWrite"), weapon + ": ZWrite kapalı");
                Assert.AreEqual((float)CullMode.Back, m.GetFloat("_Cull"), weapon + ": arka yüz ayıklanır");
                Assert.IsTrue(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), weapon + ": saydam anahtar sözcüğü");
                Assert.IsFalse(m.GetShaderPassEnabled("ShadowCaster"), weapon + ": decal gölge düşürmez");
            }
        }

        // ------------------------------------------------------------------ kütüphane

        [Test]
        public void Kutuphane_UcModelSoketVekilLodOnDerinlikVeOnDecal()
        {
            var library = Resources.Load<WallBuyStationLibrary>(WallBuyStationLibrary.ResourcePath);
            Assert.IsNotNull(library, "Resources/" + WallBuyStationLibrary.ResourcePath + " yok (ArtAp040WallBuyBuilder)");
            Assert.AreEqual(B.LibraryPath, AssetDatabase.GetAssetPath(library));

            AssertStation("standard", library.standard, B.StandardKey, B.BoardWeaponScale, B.BoardDecalSize, true);
            AssertStation("premium", library.premium, B.PremiumKey, B.ShrineWeaponScale, B.ShrineDecalSize, true);
            AssertStation("wallMount", library.wallMount, B.WallMountKey, B.BoardWeaponScale, B.BoardDecalSize, false);
            Assert.AreSame(library.standard, library.ModelFor(false), "silah tahtası = standard");
            Assert.AreSame(library.premium, library.ModelFor(true), "türbe = premium");

            Assert.AreEqual(B.DecalWeapons.Length, library.decals.Length, "on klasik decal");
            var seen = new HashSet<DD.Prototype.Core.Config.ClassicWeaponId>();
            foreach (WallBuyStationLibrary.DecalEntry e in library.decals)
            {
                Assert.IsNotNull(e, "boş decal girdisi");
                Assert.IsTrue(seen.Add(e.weapon), "aynı silaha iki decal: " + e.weapon);
                WeaponDefinition def = Arsenal.Get(e.weapon);
                Assert.IsNotNull(def, e.weapon + ": Arsenal'de yok");
                Assert.IsNotNull(e.material, e.weapon + ": decal malzemesi yok");
                Assert.AreEqual(B.DecalMaterialPath(def.weaponId), AssetDatabase.GetAssetPath(e.material), e.weapon + ": malzeme başka silahın");
                Assert.AreEqual(B.DecalTexturePath(def.weaponId), AssetDatabase.GetAssetPath(e.material.GetTexture("_BaseMap")), e.weapon + ": doku başka silahın");
            }

            foreach (WeaponDefinition def in Arsenal.All)
            {
                if (def == null || def.slot != WeaponSlotKind.Classic || !def.enabled) continue;
                Assert.IsNotNull(library.DecalFor(def.id), def.weaponId + " (" + def.id + "): etkin klasik silahın decal'i yok");
            }
        }

        private static void AssertStation(string role, WallBuyStationLibrary.StationModel s, string key, float weaponScale, float decalSize, bool needsDecal)
        {
            Assert.IsNotNull(s, role + ": girdi yok");
            Assert.IsNotNull(s.model, role + ": model atanmamış");
            Assert.AreEqual(B.ModelPath(key), AssetDatabase.GetAssetPath(s.model), role + ": yanlış FBX");
            Assert.AreEqual(weaponScale, s.weaponScale, 1e-4f, role + ": silah ölçeği (AP-040)");
            Assert.AreEqual(decalSize, s.decalSize, 1e-4f, role + ": decal boyu (AP-040)");
            Assert.AreEqual(3, s.lodScreenHeights.Length, role + ": üç LOD eşiği");
            Assert.IsTrue(s.lodScreenHeights[0] > s.lodScreenHeights[1] && s.lodScreenHeights[1] > s.lodScreenHeights[2], role + ": LOD eşikleri azalır");
            Assert.LessOrEqual(s.lodScreenHeights[2], 0.05f, role + ": son LOD tepeden bakışta kırpılmaz");

            Transform root = s.model.transform;
            Assert.IsNotNull(B.FindChild(root, WallBuyStationLibrary.WeaponSocket), role + ": " + WallBuyStationLibrary.WeaponSocket + " yok");
            if (needsDecal) Assert.IsNotNull(B.FindChild(root, WallBuyStationLibrary.DecalSocket), role + ": " + WallBuyStationLibrary.DecalSocket + " yok");
            Transform price = B.FindChild(root, "SOCKET_Price");
            Assert.IsNotNull(price, role + ": SOCKET_Price yok");
            Assert.Greater(root.InverseTransformPoint(price.position).z, 0f, role + ": istasyonun önü +Z değil (SOCKET_Price ön yüzde olmalı)");

            MeshFilter proxy = B.FindProxy(s.model);
            Assert.IsNotNull(proxy, role + ": *" + WallBuyStationLibrary.ColliderSuffix + " vekil kutusu yok");
            Bounds box = B.RootBounds(root, proxy.transform, proxy.sharedMesh.bounds);
            Assert.Greater(box.size.y, box.size.z, role + ": vekil kutu kök uzayında dik durmalı (düğümde −90° X dönüşü var) — " + box.size.ToString("F3"));
            Assert.Greater(box.min.y, -0.02f, role + ": pivot tabanda");
            Assert.GreaterOrEqual(s.frontDepth, box.max.z - 0.005f, role + ": ön derinlik vekil kutunun önünde başlamalı (" + s.frontDepth.ToString("F2") + ")");
            Assert.LessOrEqual(s.frontDepth, 0.6f, role + ": ön derinlik ölçüsüz büyük");
            Assert.AreEqual(B.MeasureFrontDepth(s.model), s.frontDepth, 1e-4f, role + ": ön derinlik modelden yeniden ölçülenle aynı değil");

            LODGroup group = s.model.GetComponent<LODGroup>();
            Assert.IsNotNull(group, role + ": LODGroup yok (SM_*_LOD0..2)");
            LOD[] lods = group.GetLODs();
            Assert.AreEqual(B.LodCount, lods.Length, role + ": LOD sayısı");
            for (int i = 0; i < lods.Length; i++)
            {
                Assert.IsNotEmpty(lods[i].renderers, role + " LOD" + i + " boş");
                foreach (Renderer r in lods[i].renderers)
                {
                    Assert.IsNotNull(r, role + " LOD" + i + ": boş renderer");
                    Assert.IsTrue(r.name.EndsWith(B.LodSuffix + i, StringComparison.Ordinal), role + " LOD" + i + ": " + r.name);
                }
            }
        }

        // ------------------------------------------------------------------ HUD

        [Test]
        public void HudSpriteleri_TekMip1024AltiVeDokuzDilimKenarlari()
        {
            var fromIntake = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            foreach (B.HudIntakeFile f in B.ReadHudIntake().files)
            {
                fromIntake[f.name] = new Vector4(f.border_lbrt[0], f.border_lbrt[1], f.border_lbrt[2], f.border_lbrt[3]);
            }

            foreach ((string name, Vector4 border) in HudSprites)
            {
                Assert.IsTrue(fromIntake.TryGetValue(name, out Vector4 intake), name + ": intake JSON'da yok");
                Assert.AreEqual(border, intake, name + ": intake kenarı AP-042 nine-slice ile uyuşmuyor");

                string path = B.HudSpritePath(name);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, "HUD importer yok: " + path);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, name + ": Sprite");
                Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode, name + ": tek sprite");
                Assert.IsFalse(importer.mipmapEnabled, name + ": UI dokusunda mip olmaz");
                Assert.LessOrEqual(importer.maxTextureSize, B.HudMaxSize, name + ": UI sınıfı 1024");
                Assert.AreEqual(border, importer.spriteBorder, name + ": 9-slice kenarı (L, B, R, T)");
                AssertStandalone(importer, TextureImporterFormat.BC7, B.HudMaxSize, name);

                Sprite sprite = Resources.Load<Sprite>(HudResources + name);
                Assert.IsNotNull(sprite, name + ": Resources'tan yüklenmedi");
                Assert.AreEqual(border, sprite.border, name + ": sprite kenarı");
                Assert.AreEqual(1, sprite.texture.mipmapCount, name + ": tam bir mip");
                Assert.LessOrEqual(Mathf.Max(sprite.texture.width, sprite.texture.height), B.HudMaxSize, name + ": 1024² tavanı");
                if (name.EndsWith("_64", StringComparison.Ordinal)) Assert.AreEqual(64, sprite.texture.width, name + ": 64 px ikon katmanı");
            }
        }

        // ------------------------------------------------------------------ meta / GUID

        [Test]
        public void YeniVarliklar_MetaVar_GuidTekil()
        {
            var paths = new List<string>
            {
                "Assets/_Prototype/Art", "Assets/_Prototype/Art/AP040", B.SharedRoot, B.SharedRoot + "/Textures", B.SharedRoot + "/Materials",
                "Assets/_Prototype/Weapons/WallBuy/Art", B.Root, B.Root + "/Models", B.Root + "/Decals", B.Root + "/Materials",
                "Assets/_Prototype/UI/Art/AP042",
                B.ProvenancePath, B.Root + "/AP040-wallbuy-LICENSES.csv", B.HudIntakePath, "Assets/_Prototype/UI/Art/AP042/ap042_wallbuy-LICENSES.csv",
                B.SharedMaterialPath, B.GlowMaterialPath, B.LibraryPath,
            };
            foreach (B.ProvenanceFile f in B.ReadProvenance().files) paths.Add(f.repo);
            foreach (B.HudIntakeFile f in B.ReadHudIntake().files) paths.Add(f.repo);
            foreach (string weapon in B.DecalWeapons) paths.Add(B.DecalMaterialPath(weapon));

            var ours = new Dictionary<string, string>(StringComparer.Ordinal);
            var faults = new List<string>();
            foreach (string p in paths)
            {
                string guid = MetaGuid(p + ".meta");
                if (guid == null)
                {
                    faults.Add(".meta yok ya da guid satırı yok: " + p);
                    continue;
                }

                if (ours.TryGetValue(guid, out string other)) faults.Add("GUID çakışması: " + p + " <-> " + other);
                else ours[guid] = p;
                if (AssetDatabase.AssetPathToGUID(p) != guid) faults.Add("AssetDatabase GUID'i .meta ile aynı değil: " + p);
            }

            foreach (string metaRaw in Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories))
            {
                string meta = metaRaw.Replace('\\', '/');
                string guid = MetaGuid(meta);
                if (guid == null || !ours.TryGetValue(guid, out string owner)) continue;
                if (owner + ".meta" != meta) faults.Add("GUID çakışması: " + owner + " <-> " + meta);
            }

            Assert.IsEmpty(faults, "meta / GUID (" + faults.Count + "):\n" + string.Join("\n", faults));
        }

        private static string MetaGuid(string metaPath)
        {
            if (!File.Exists(metaPath)) return null;
            foreach (string line in File.ReadLines(metaPath))
            {
                if (line.StartsWith("guid: ", StringComparison.Ordinal)) return line.Substring(6).Trim();
            }

            return null;
        }
    }
}
