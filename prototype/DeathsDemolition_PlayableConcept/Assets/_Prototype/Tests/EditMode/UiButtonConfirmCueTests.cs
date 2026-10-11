using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-105 (SHOULD-8) · UiButton's activation cue. A press the control acts on raises ButtonPress + Confirm (one pair);
    /// <see cref="UiButton.ConfirmCue"/> off leaves the voice to the handler (Selection's locked weapon row: Denied alone,
    /// never Confirm + Denied in one frame); a control with no handler confirms nothing (an achievement row); a dead
    /// control still says Denied. UiSound raises its UiEvent before the audio path, so EditMode counts the cues without a
    /// sound playing.
    /// </summary>
    [TestFixture]
    [Category("WP105")]
    public sealed class UiButtonConfirmCueTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly Dictionary<UiEvent, int> _seen = new Dictionary<UiEvent, int>();
        private RectTransform _root;
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            // The press pulse runs on the motion core: a clean core on a test clock, as UiComponentsV3Tests does.
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
            _seen.Clear();
            UiEvents.Raised += Record;
        }

        [TearDown]
        public void TearDown()
        {
            UiEvents.Raised -= Record;
            UiEvents.ResetCounters();
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiMotion.ReducedMotion = _reduced;
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
            int n;
            _seen.TryGetValue(e, out n);
            _seen[e] = n + 1;
        }

        private int Seen(UiEvent e)
        {
            int n;
            return _seen.TryGetValue(e, out n) ? n : 0;
        }

        [Test]
        public void UiButton_Varsayilan_BasisOnaySesi_BirKez_EylemCalisir()
        {
            int fired = 0;
            UiButton button = UiFactory.Button("Act", _root, "ACT", UiButtonVariant.Secondary, () => fired++);
            Assert.IsTrue(button.ConfirmCue, "varsayılan açık: mevcut kontrollerin sesi değişmez");

            button.OnSubmit(null);
            Assert.AreEqual(1, fired, "eylem bir kez");
            Assert.AreEqual(1, Seen(UiEvent.ButtonPress), "ButtonPress bir kez");
            Assert.AreEqual(1, Seen(UiEvent.Confirm), "Confirm bir kez");
            Assert.AreEqual(0, Seen(UiEvent.Denied), "Denied yok");
        }

        [Test]
        public void UiButton_OnaySesiKapali_EylemReddederse_YalnizRedSesi()
        {
            // Selection's locked weapon row in miniature: interactable (its stage explains the lock), the handler refuses.
            UiButton button = UiFactory.Button("Locked", _root, "LOCKED", UiButtonVariant.Secondary, () => UiSound.Play(UiSoundKind.Denied));
            button.ConfirmCue = false;
            Assert.IsTrue(button.IsInteractable(), "kilitli satır odaklanabilir kalır");

            button.OnSubmit(null);
            Assert.AreEqual(1, Seen(UiEvent.Denied), "tek ses: Denied");
            Assert.AreEqual(0, Seen(UiEvent.Confirm), "aynı karede Confirm yok (SHOULD-8)");
            Assert.AreEqual(0, Seen(UiEvent.ButtonPress), "ButtonPress de yok: Confirm'in sahibi olduğu satır (AudioEventMap)");

            button.OnPointerClick(null);
            Assert.AreEqual(2, Seen(UiEvent.Denied), "tıklama da yalnız Denied");
            Assert.AreEqual(0, Seen(UiEvent.Confirm));
        }

        [Test]
        public void UiButton_EylemiYok_BasisHicbirSeyOnaylamaz()
        {
            // An achievement row: a list row with no handler - focusable for reading, the press does nothing.
            UiButton row = UiFactory.Button("Achievement.row", _root, "BLOODED", UiButtonVariant.Secondary, null);
            row.OnSubmit(null);
            Assert.AreEqual(0, Seen(UiEvent.Confirm), "eylemsiz satır Confirm çalmaz");
            Assert.AreEqual(0, Seen(UiEvent.ButtonPress), "eylemsiz satır ButtonPress yaymaz");
            Assert.AreEqual(0, Seen(UiEvent.Denied), "reddedilmedi de: sessiz");

            // A handler set later (the kit's steppers / switches / choice groups wire theirs after the build) confirms.
            int fired = 0;
            row.SetOnClick(() => fired++);
            row.OnSubmit(null);
            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, Seen(UiEvent.Confirm), "sonradan bağlanan eylem onay sesini alır");
        }

        [Test]
        public void UiButton_EtkisizKontrol_YineRedSesi_OnayBayragindanBagimsiz()
        {
            int fired = 0;
            UiButton dead = UiFactory.Button("Dead", _root, "DEAD", UiButtonVariant.Secondary, () => fired++);
            dead.SetInteractable(false);
            dead.ConfirmCue = false;
            dead.OnSubmit(null);
            Assert.AreEqual(0, fired, "etkisiz kontrol eylem çalıştırmaz");
            Assert.AreEqual(1, Seen(UiEvent.Denied), "etkisiz kontrolün Denied'ı korunur (M-34)");
            Assert.AreEqual(0, Seen(UiEvent.Confirm));
        }
    }
}
