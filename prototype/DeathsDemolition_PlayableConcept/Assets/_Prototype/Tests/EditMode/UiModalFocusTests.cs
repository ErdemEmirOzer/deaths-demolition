using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// R4 WP-105 (KOD SHOULD-13) · focus ownership under a modal: an open UiModal registers with UiInput, and while it stands a
    /// screen's ReclaimFocus fallback behind it is turned into the modal's own safe action (UiInput.ReclaimTarget) - the
    /// screen asks, the modal answers, focus never goes behind a dialog. The modal's own controls stay its own; closing,
    /// hiding or destroying the modal gives the focus back to the screens; the last one opened is on top.
    /// </summary>
    [TestFixture]
    [Category("WP105")]
    public sealed class UiModalFocusTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private RectTransform _root;
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiMotion.ReducedMotion = _reduced;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private UiModal Confirm()
        {
            UiModal modal = UiBrushKit.Confirm(PrototypeUiFlow.QuitConfirmName, _root, PrototypeUiFlow.QuitSpec, null, null);
            _created.Add(modal.gameObject);
            return modal;
        }

        [Test]
        public void Modal_AcikkenOdakSahibi_AlttakiEkraninGeriAlimiModalaGider_KapaninceEkranaDoner()
        {
            UiButton behind = UiFactory.Button("Behind", _root, "BEHIND", UiButtonVariant.Secondary, null);
            UiModal modal = Confirm();
            Assert.IsNull(UiInput.TopModal, "kapalı kurulan modal odak sahibi değil");
            Assert.AreSame(behind, UiInput.ReclaimTarget(behind), "modal yokken ekranın geri alımı kendi yedeğine");

            modal.Open();
            Assert.AreSame(modal, UiInput.TopModal, "açılan modal kayıtlı");
            Assert.AreSame(modal.DefaultFocus, UiInput.ReclaimTarget(behind), "alttaki ekranın geri alımı modalın güvenli eylemine gider");
            Assert.AreSame(modal.Secondary, modal.DefaultFocus, "yıkıcı onayda güvenli eylem varsayılan");
            Assert.AreSame(modal.Primary, UiInput.ReclaimTarget(modal.Primary), "modalın kendi kontrolü kendisinde kalır");

            modal.Close();
            Assert.IsNull(UiInput.TopModal, "kapanınca kayıttan çıkar");
            Assert.AreSame(behind, UiInput.ReclaimTarget(behind), "odak yeniden ekranın");

            modal.Open();
            Assert.IsTrue(modal.HandleCancel(), "iptal = güvenli eylem, modal kapanır");
            Assert.IsNull(UiInput.TopModal, "iptalle kapanan modal da kayıttan çıkar");
        }

        [Test]
        public void IkiModal_SonAcilanUstte_GizlenenAtlanir_YokEdilenKayittanDuser()
        {
            UiButton behind = UiFactory.Button("Behind", _root, "BEHIND", UiButtonVariant.Secondary, null);
            UiModal first = Confirm();
            UiModal second = Confirm();
            first.Open();
            second.Open();
            Assert.AreSame(second, UiInput.TopModal, "son açılan üstte");
            Assert.AreSame(second.DefaultFocus, UiInput.ReclaimTarget(first.Secondary), "alttaki modalın geri alımı da üsttekine gider");

            second.gameObject.SetActive(false);   // hidden without Close (its screen went away): skipped, not the owner
            Assert.AreSame(first, UiInput.TopModal, "gizlenen atlanır");
            second.gameObject.SetActive(true);
            Assert.AreSame(second, UiInput.TopModal, "yeniden görünen yine üstte (kaydı duruyor)");

            _created.Remove(second.gameObject);
            Object.DestroyImmediate(second.gameObject);
            Assert.AreSame(first, UiInput.TopModal, "yok edilen kayıttan düşer");
            first.Close();
            Assert.IsNull(UiInput.TopModal);
            Assert.AreSame(behind, UiInput.ReclaimTarget(behind));
        }

        [Test]
        public void YedekYokken_ModalAcik_GeriAlimModalinVarsayilanina()
        {
            UiModal modal = Confirm();
            Assert.IsNull(UiInput.ReclaimTarget(null), "modal yok, yedek yok: geri alım yok");
            modal.Open();
            Assert.AreSame(modal.DefaultFocus, UiInput.ReclaimTarget(null));
            modal.Close();
        }
    }
}
