using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 0 (UI v3 direction §10): regression guards for the defects the
    /// council confirmed in code and baseline frames - one sound cue per hover and a
    /// Navigate cue on keyboard / pad focus travel (D3), the on-screen camera pick (D5)
    /// and text bands that grow with the reading-text scale (D6). The HUD overlap and
    /// truncation guards (D1, D2) live in the PlayMode HUD test (UiArtSliceTests).
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiDefectSliceTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UiEvent> _raised = new List<UiEvent>();
        private float _textScale;

        [SetUp]
        public void SetUp()
        {
            _raised.Clear();
            _textScale = UiTokens.TextScale;
            UiEvents.Raised += Record;
        }

        [TearDown]
        public void TearDown()
        {
            UiEvents.Raised -= Record;
            UiTokens.TextScale = _textScale;
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
        }

        private void Record(UiEvent e, string detail)
        {
            _raised.Add(e);
        }

        private UiButton NewButton(string name)
        {
            var root = new GameObject("Root", typeof(RectTransform));
            _created.Add(root);
            return UiFactory.Button(name, root.transform, "LABEL", UiButtonVariant.Secondary, null);
        }

        [Test]
        public void Hover_TekIpucu_OdakGezintisiNavigate_ProgramatikSessiz()
        {
            UiButton button = NewButton("Probe");

            // D3: a pointer hover is ONE cue (ButtonHover), never ButtonHover + FocusMoved.
            button.OnPointerEnter(new PointerEventData(null));
            CollectionAssert.AreEqual(new[] { UiEvent.ButtonHover }, _raised, "hover tek ipucu: " + string.Join(",", _raised));

            // Keyboard / pad travel arrives as an AxisEventData select: the Navigate cue.
            _raised.Clear();
            button.OnSelect(new AxisEventData(null) { moveDir = MoveDirection.Down });
            CollectionAssert.AreEqual(new[] { UiEvent.FocusMoved }, _raised, "odak gezintisi Navigate ipucu çalar");

            // Programmatic focus (screen open, focus repair, the hover's own select) is silent.
            _raised.Clear();
            button.OnSelect(new BaseEventData(null));
            CollectionAssert.IsEmpty(_raised, "programatik odak sessiz");
        }

        [Test]
        public void EnsureUiCamera_EkranKamerasiniSecer_RenderTextureKamerasiniAtlar()
        {
            foreach (Camera existing in Object.FindObjectsOfType<Camera>())
            {
                existing.enabled = false; // the EditMode scene's own cameras stay out of the pick
            }

            var texture = new RenderTexture(16, 16, 0);
            var offscreen = new GameObject("TurntableCamera", typeof(Camera)).GetComponent<Camera>();
            offscreen.targetTexture = texture;
            _created.Add(offscreen.gameObject);
            var screen = new GameObject("ScreenCamera", typeof(Camera)).GetComponent<Camera>();
            _created.Add(screen.gameObject);

            try
            {
                Camera picked = UiFactory.EnsureUiCamera();
                Assert.AreSame(screen, picked, "UI ekran kamerasına çizmeli, render-texture kamerasına değil: " + (picked != null ? picked.name : "null"));
            }
            finally
            {
                offscreen.targetTexture = null;
                Object.DestroyImmediate(texture);
                foreach (Camera existing in Object.FindObjectsOfType<Camera>())
                {
                    existing.enabled = true;
                }
            }
        }

        [Test]
        public void MetinBantlari_MetinOlcegiyleBuyur_1_0Degismez()
        {
            UiTokens.TextScale = 1f;
            Assert.AreEqual(24f, UiFactory.ScaledBand(24f), "1,0'da bant aynen kalır");
            UiTokens.TextScale = 1.25f;
            Assert.AreEqual(30f, UiFactory.ScaledBand(24f), "1,25'te bant %25 büyür");
            Assert.AreEqual(115f, UiFactory.ScaledBand(92f));
            UiTokens.TextScale = 3f;
            Assert.AreEqual(30f, UiFactory.ScaledBand(24f), "ölçek 1,25'te kırpılır");
        }
    }
}
