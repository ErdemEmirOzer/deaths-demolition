using System.Collections.Generic;
using System.IO;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-081 ikon v2 B5b · the icon material's reach (C-00 06.10): DD/UI/Fx Icon (8) only on a UiIconButton glyph whose key
    /// is O1 - the G1 manifest's O1 merges (the SVG viewBox family frame [0, 256]: B5a's AP-028-B 50 and, WP-098A, AP-034
    /// v002.1's I6 three), replaced + added. Every other G1 key, the raw <see cref="UiArt.IconImage"/>
    /// path and the HUD's glyphs keep SdfGlyph (IK-M18). At rest the Icon channels are B5b's static values; a disabled
    /// control turns the light off (the flat token) and back on, and nothing else moves.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class UiIconMaterialKeysTests
    {
        private const string G1ManifestPath = "Assets/_Prototype/UI/Art/Resources/UI/G1/AP012-ui-manifest.json";

        private readonly List<GameObject> _created = new List<GameObject>();
        private bool _reduced;
        private RectTransform _root;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
            UiIconButton.ResetMissingForTests();
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedMotion = _reduced;
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiIconButton.ResetMissingForTests();
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [System.Serializable]
        private sealed class G1Manifest
        {
            public G1Merge[] merges;
        }

        [System.Serializable]
        private sealed class G1Merge
        {
            public string slice;
            public string[] replaced;
            public string[] added;
            public float[] familyFrame256;
        }

        /// <summary>
        /// The O1 set in code is the manifest's O1 merges - every merge drawn in the SVG viewBox frame [0, 256] - read from the
        /// file (no hand copy can drift from it): B5a (20 + 30) and WP-098A's AP-034 I6 (Menu.CycleLeft / CycleRight /
        /// Display) = 53.
        /// </summary>
        [Test]
        public void O1Anahtarlari_ManifestinO1BirlesimleriyleAyni()
        {
            G1Manifest manifest = JsonUtility.FromJson<G1Manifest>(File.ReadAllText(G1ManifestPath));
            Assert.IsNotNull(manifest?.merges, "G1 manifest merges");
            Assert.IsNotEmpty(manifest.merges);
            var expected = new SortedSet<string>(System.StringComparer.Ordinal);
            var slices = new List<string>();
            foreach (G1Merge merge in manifest.merges)
            {
                bool o1 = merge.familyFrame256 != null && merge.familyFrame256.Length == 2 && merge.familyFrame256[0] == 0f && merge.familyFrame256[1] == 256f;
                if (!o1)
                {
                    continue;
                }

                slices.Add(merge.slice);
                expected.UnionWith(merge.replaced ?? new string[0]);
                expected.UnionWith(merge.added ?? new string[0]);
            }

            CollectionAssert.AreEqual(new[] { "WP-081 B5a", "WP-098A AP-034 I6" }, slices, "O1 birleşimleri (aile çerçevesi [0, 256])");
            var actual = new SortedSet<string>(UiArt.O1IconKeys, System.StringComparer.Ordinal);

            Assert.AreEqual(UiArt.O1IconKeys.Length, actual.Count, "O1IconKeys tekrar içermez");
            Assert.AreEqual(53, actual.Count, "B5a 50 + AP-034 I6 3");
            foreach (string key in new[] { "Menu.CycleLeft", "Menu.CycleRight", "Menu.Display" })
            {
                Assert.IsTrue(actual.Contains(key), key + ": AP-034 I6 O1 anahtarı");
            }

            CollectionAssert.AreEqual(expected, actual, "UiArt.O1IconKeys = O1 birleşimlerinin replaced + added'ı (" + string.Join(", ", slices) + ")");
            for (int i = 1; i < UiArt.O1IconKeys.Length; i++)
            {
                Assert.Less(string.CompareOrdinal(UiArt.O1IconKeys[i - 1], UiArt.O1IconKeys[i]), 0, "ordinal sıra: " + UiArt.O1IconKeys[i]);
            }

            foreach (string key in UiArt.O1IconKeys)
            {
                Assert.IsTrue(UiArt.IsO1Glyph(key), key);
            }

            // B5a kept these on AP-012 (keptG1, keyOverrides) or deferred them to B5d (Hud.*): not O1, no material.
            foreach (string key in new[] { "Medal.Locked", "Shop.Purchased", "Unlock.Title", "Unlock.Badge", "Hud.Health", "Hud.Gold", "Hud.LowHealth" })
            {
                Assert.IsFalse(UiArt.IsO1Glyph(key), key + ": O1 değil");
            }

            Assert.IsFalse(UiArt.IsO1Glyph(null));
            Assert.IsFalse(UiArt.IsO1Glyph("menu.play"), "anahtar büyük/küçük harf duyarlı (ordinal)");
        }

        /// <summary>
        /// Every G1 glyph through an icon button: Icon exactly for the O1 keys, SdfGlyph for the rest; the raw IconImage path
        /// and a HUD glyph stay SdfGlyph even for an O1 key (the material is the icon control's, IK-M18).
        /// </summary>
        [Test]
        public void IkonModu_YalnizO1_UiIconButtonda_GeriKalaniSdfGlyph()
        {
            UiTheme theme = UiTheme.Active;
            Assert.IsNotNull(theme, "tema");
            int icon = 0, sdf = 0;
            var failures = new List<string>();
            foreach (UiTheme.NamedSprite named in theme.Icons)
            {
                if (named.Sprite == null || !UiArt.IsGlyph(named.Sprite))
                {
                    continue;   // only G1 cells can take either mode
                }

                UiIconButton button = UiIconControls.Button(_root, named.Name, "menu.tab.settings", "SETTINGS", null, UiIconSize.M);
                UiFx fx = button.Glyph.GetComponent<UiFx>();
                bool o1 = UiArt.IsO1Glyph(named.Name);
                UiFxMode expected = o1 ? UiFxMode.Icon : UiFxMode.SdfGlyph;
                if (fx == null || fx.Mode != expected || !fx.enabled || (button.IconMaterial != null) != o1)
                {
                    failures.Add(named.Name + ": " + (fx == null ? "UiFx yok" : fx.Mode + (fx.enabled ? string.Empty : " (kapalı)")) + ", beklenen " + expected);
                }

                if (o1)
                {
                    icon++;
                }
                else
                {
                    sdf++;
                }

                Object.DestroyImmediate(button.gameObject);
            }

            Assert.IsEmpty(failures, "Icon modu yalnız O1 anahtarlarında:\n" + string.Join("\n", failures));
            Assert.AreEqual(UiArt.O1IconKeys.Length, icon, "temadaki her O1 anahtarı Icon");
            Assert.Greater(sdf, 100, "O1 dışı G1 hücreleri SdfGlyph'te kalır");

            Image raw = UiArt.IconImage("Raw", _root, "Menu.Back", UiColor.TextPrimary, 24f);
            Assert.AreEqual(UiFxMode.SdfGlyph, raw.GetComponent<UiFx>().Mode, "UiArt.IconImage ham yolu: SdfGlyph (O1 anahtarında da)");
            Image hud = UiFactory.NewImage("HudGlyph", _root, UiColor.TextPrimary, UiArt.Icon("Hud.Health"));
            Assert.AreEqual(UiFxMode.SdfGlyph, hud.GetComponent<UiFx>().Mode, "HUD glifi SdfGlyph");
            Assert.IsNull(UiArt.ApplyIconMaterial(hud, "Hud.Health", true), "O1 olmayan anahtar malzeme almaz");
            Assert.AreEqual(UiFxMode.SdfGlyph, hud.GetComponent<UiFx>().Mode);
            Assert.AreEqual(UiMotionClass.Flash, UiFx.ClassOf(UiFxMode.Icon), "sheen / glow Flash sınıfı (× FlashScale)");
        }

        /// <summary>
        /// B5b's static values: at rest sheen off (−1), no glow, no spread; drawn, lit, opaque. Disabled = the light off (the
        /// flat token, AP-028 render()'s disabled state) with the tint the kit already sets; enabled again = lit. Choosing a
        /// tab moves no material channel (the indicator is the kit's, IK-M7).
        /// </summary>
        [Test]
        public void IkonModu_DinlenmeKanallari_DisabledIsikKapali()
        {
            UiIconButton button = UiIconControls.Button(_root, "Menu.Settings", "menu.tab.settings", "SETTINGS", null, UiIconSize.L);
            UiFx fx = button.IconMaterial;
            Assert.IsNotNull(fx, "O1 düğmesinin Icon sürücüsü");
            Assert.AreSame(fx, button.Glyph.GetComponent<UiFx>());
            Assert.AreEqual(UiFxMode.Icon, fx.Mode);
            Assert.AreEqual(new Vector3(-1f, 0f, 0f), fx.IconLight, "uv1.yzw: sheen kapalı, glow 0, spread 0");
            Assert.AreEqual(new Vector3(1f, 1f, 1f), fx.Parameters, "uv2.xyz: çizili, ışıklı, opak");

            button.SetInteractable(false);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), fx.Parameters, "disabled: yalnız ışık söner");
            Assert.AreEqual(UiFx.IconRestLight, fx.IconLight);
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), button.Glyph.color, "ton kitin (TextSecondary)");

            button.SetInteractable(true);
            Assert.AreEqual(UiFx.IconRestParams, fx.Parameters, "yeniden etkin: ışık yanar");

            UiIconButton tab = UiIconControls.Tab(_root, "Menu.Profile", "menu.tab.profile", "PROFILE", null);
            Assert.IsNotNull(tab.IconMaterial);
            tab.SetSelected(true);
            // WP-098A (DEC-114 / HAREKET-MENU §3, C-00 08.10): B5b left the choice off the material; the selected rest now lights it -
            // sheen 1 (the band passed, AP-028 selected's last key), glow 0.04 - drawn, lit, opaque as before. The bind frame lands
            // it at once (no track on screen open).
            Assert.AreEqual(new Vector3(UiIconPose.Chosen.Sheen, UiIconPose.Chosen.Glow, 0f), tab.IconMaterial.IconLight, "seçili dinlenme: sheen 1, glow 0,04 (§3)");
            Assert.AreEqual(UiFx.IconRestParams, tab.IconMaterial.Parameters);
        }
    }
}
