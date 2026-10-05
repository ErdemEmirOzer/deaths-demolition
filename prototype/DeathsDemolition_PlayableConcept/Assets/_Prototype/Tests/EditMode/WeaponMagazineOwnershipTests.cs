using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Weapons;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-082 reload magazine contract v1 (S-02 with S-06, C-00 05.10) · the pure ownership state: the clip's beats in order,
    /// a missing beat at its timeline mark + grace, forward only (a repeat ignored, a later beat lands the skipped ones), any
    /// end of the reload puts the magazine back in the weapon, the contract names and the AP-009C aliases.
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class WeaponMagazineOwnershipTests
    {
        private const float Reload = 1.8f;   // s, a magazine reload's length (ar_1-like)

        [Test]
        public void Olaylar_Sirayla_SilahElEleSilah()
        {
            var o = new WeaponMagazineOwnership();
            var seen = new List<MagazineOwner>();
            o.Stepped += (s, fromEvent) => seen.Add(o.Owner);
            o.Begin();
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.IsTrue(o.OnEvent(MagazineStep.Detach));
            Assert.IsTrue(o.OnEvent(MagazineStep.HandAttach));
            Assert.IsTrue(o.OnEvent(MagazineStep.Insert));
            Assert.IsTrue(o.OnEvent(MagazineStep.BoltRelease));
            CollectionAssert.AreEqual(new[] { MagazineOwner.HandOld, MagazineOwner.HandNew, MagazineOwner.Weapon, MagazineOwner.Weapon }, seen);
            Assert.IsFalse(o.Active, "son vuruştan sonra reload kapanır");
            Assert.AreEqual(4, o.FromEvent);
            Assert.AreEqual(0, o.FromTimer);
        }

        [Test]
        public void EksikOlay_ZamanCizelgesiIsaretindeVeriliyor()
        {
            var o = new WeaponMagazineOwnership();
            o.Begin();
            float grace = WeaponMagazineOwnership.DeadlineGraceSeconds / Reload;
            o.Tick(WeaponMagazineOwnership.MagazineMarks[0] + grace * 0.5f, Reload);
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner, "pay dolmadan zamanlayıcı devreye girmez");
            o.OnEvent(MagazineStep.Detach);   // the clip carries mag_out …
            // … but not mag_hand_attach (AP-009C has none): it lands at its mark + grace
            o.Tick(WeaponMagazineOwnership.MagazineMarks[1] + grace + 1e-4f, Reload);
            Assert.AreEqual(MagazineOwner.HandNew, o.Owner);
            o.OnEvent(MagazineStep.Insert);
            o.Tick(1f, Reload);
            Assert.IsFalse(o.Active);
            Assert.AreEqual(2, o.FromEvent);
            Assert.AreEqual(2, o.FromTimer);
        }

        [Test]
        public void OlaysizKlip_DortVurusZamanlayicidan()
        {
            var o = new WeaponMagazineOwnership();
            o.Begin();
            for (float p = 0f; p <= 1f; p += 1f / 60f / Reload) o.Tick(p, Reload);
            o.Tick(1f, Reload);
            Assert.IsFalse(o.Active);
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.AreEqual(4, o.FromTimer);
        }

        [Test]
        public void YalnizIleri_TekrarYokSayilir_SonrakiAtlananlariIndirir()
        {
            var o = new WeaponMagazineOwnership();
            o.Begin();
            Assert.IsTrue(o.OnEvent(MagazineStep.Detach));
            Assert.IsFalse(o.OnEvent(MagazineStep.Detach), "aynı vuruş ikinci kez sayılmaz");
            Assert.IsTrue(o.OnEvent(MagazineStep.Insert), "insert, atlanan hand_attach'ı önce indirir");
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.AreEqual(2, o.FromEvent);
            Assert.AreEqual(1, o.FromTimer, "atlanan vuruş olaydan sayılmaz");
            Assert.IsFalse(o.OnEvent(MagazineStep.HandAttach), "geriye dönüş yok");
        }

        [Test]
        public void ReloadErkenBiter_SarjorAninaSilahta()
        {
            foreach (int stop in new[] { 0, 1, 2, 3 })
            {
                var o = new WeaponMagazineOwnership();
                o.Begin();
                for (int s = 0; s < stop; s++) o.OnEvent((MagazineStep)s);
                o.End();   // cancel / switch / Special / death / camera change
                Assert.AreEqual(MagazineOwner.Weapon, o.Owner, "durak " + stop + ": şarjör elde ya da havada kaldı");
                Assert.IsFalse(o.Active);
                Assert.AreEqual(1, o.EndedEarly);
                Assert.IsFalse(o.OnEvent(MagazineStep.Insert), "bitmiş reload'a geç olay");
            }
        }

        [Test]
        public void YeniBaslangic_ReloadOrtasinda_TemizBaslar()
        {
            var o = new WeaponMagazineOwnership();
            o.Begin();
            o.OnEvent(MagazineStep.Detach);
            o.Begin();
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.AreEqual(0, o.NextStep);
            Assert.AreEqual(1, o.EndedEarly);
        }

        [Test]
        public void BosTup_DetachYok_EleGelirTakilirOturur()
        {
            // rl_1 (S-06 05.10): the tube starts empty — the round comes to the hand, goes in, seats
            var o = new WeaponMagazineOwnership();
            o.Begin(emptyTube: true);
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.AreEqual((int)MagazineStep.HandAttach, o.NextStep);
            Assert.IsFalse(o.OnEvent(MagazineStep.Detach), "boş tüpte çıkarılacak bir şey yok");
            float grace = WeaponMagazineOwnership.DeadlineGraceSeconds / Reload;
            o.Tick(WeaponMagazineOwnership.MagazineMarks[1] + grace + 1e-4f, Reload);
            Assert.AreEqual(MagazineOwner.HandNew, o.Owner, "roket elde");
            Assert.IsTrue(o.OnEvent(MagazineStep.Insert));
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            Assert.IsTrue(o.OnEvent(MagazineStep.BoltRelease));
            Assert.IsFalse(o.Active);
            Assert.AreEqual(0, o.EndedEarly);
        }

        [Test]
        public void OlayAdlari_SozlesmeVeTakmaAdlar()
        {
            AssertStep("mag_out", MagazineStep.Detach);
            AssertStep("mag_in", MagazineStep.Insert);
            AssertStep("chamber", MagazineStep.BoltRelease);
            foreach (string n in new[] { "mag_detach", "mag_hand_attach", "mag_insert", "bolt_release" })
            {
                CharacterAnimationEvent e = CharacterAnimationEvents.Parse(n);
                // S-06 adds the contract names; until then Parse returns None and the alias (or the timeline) carries the beat
                if (e != CharacterAnimationEvent.None) Assert.IsTrue(WeaponMagazineOwnership.TryStepFor(e, out _), n);
            }

            Assert.IsFalse(WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvent.Fire, out _));
            Assert.IsFalse(WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvent.None, out _));
            Assert.IsFalse(WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvent.MagIn2, out _), "pompalı fişek oturması şarjör değil");
        }

        private static void AssertStep(string name, MagazineStep expected)
        {
            CharacterAnimationEvent e = CharacterAnimationEvents.Parse(name);
            Assert.AreNotEqual(CharacterAnimationEvent.None, e, name + " enum'da yok");
            Assert.IsTrue(WeaponMagazineOwnership.TryStepFor(e, out MagazineStep step), name);
            Assert.AreEqual(expected, step, name);
        }
    }
}
