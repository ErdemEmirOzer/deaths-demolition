using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// §12 (C-00 05.10, AP-026-B reload event contract · S-06 pre-check → C-00 decisions): rl_1's rocket_in / rocket_seat are the
    /// tube's Insert / BoltRelease; a tactical reload has three beats and the timeline never invents a bolt release; synonyms of
    /// one beat on one frame reach the listeners once; the shell-by-shell reload's schedule (S-06's start / shell / end loop)
    /// keeps today's total and its rounds land at the end of each shell loop.
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087ReloadOlaySozlesmesiTests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

        [Test]
        public void RoketAdlari_InsertVeBoltRelease()
        {
            Assert.IsTrue(WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvent.RocketIn, out MagazineStep inStep));
            Assert.AreEqual(MagazineStep.Insert, inStep, "rocket_in = Insert (MagIn)");
            Assert.IsTrue(WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvent.RocketSeat, out MagazineStep seat));
            Assert.AreEqual(MagazineStep.BoltRelease, seat, "rocket_seat = BoltRelease (Chamber)");

            var o = new WeaponMagazineOwnership();
            o.Begin(emptyTube: true);
            Assert.AreEqual(3, o.ExpectedSteps, "boş tüp: el · içeri · otur");
            Assert.IsTrue(o.OnEvent(MagazineStep.HandAttach));
            Assert.IsTrue(o.OnEvent(inStep));
            Assert.IsTrue(o.OnEvent(seat));
            Assert.AreEqual(3, o.FromEvent);
            Assert.AreEqual(0, o.FromTimer);
            Assert.IsFalse(o.Active);
        }

        [Test]
        public void Taktik_UcVurus_ZamanlayiciBoltReleaseUydurmaz()
        {
            var o = new WeaponMagazineOwnership();
            var steps = new List<MagazineStep>();
            o.Stepped += (s, fromEvent) => steps.Add(s);
            o.Begin(tactical: true);
            Assert.AreEqual(3, o.ExpectedSteps, "taktik: MagOut · MagHandAttach · MagIn");
            o.Tick(1f, 2f);   // the whole timeline passes, no clip event
            CollectionAssert.AreEqual(new[] { MagazineStep.Detach, MagazineStep.HandAttach, MagazineStep.Insert }, steps, "zamanlayıcı bolt release uydurmadı");
            Assert.AreEqual(MagazineOwner.Weapon, o.Owner);
            o.End();
            Assert.AreEqual(0, o.EndedEarly, "üç vuruşla taktik reload tam");

            // a clip that does send bolt_release after Insert is still heard (the contract is the clip's; the timer just never invents it)
            steps.Clear();
            o.Begin(tactical: true);
            o.OnEvent(MagazineStep.Detach); o.OnEvent(MagazineStep.HandAttach); o.OnEvent(MagazineStep.Insert);
            Assert.IsTrue(o.OnEvent(MagazineStep.BoltRelease));
            Assert.AreEqual(MagazineStep.BoltRelease, steps[steps.Count - 1]);

            // an empty reload keeps its four beats, the bolt release from the timeline included
            steps.Clear();
            o.Begin();
            Assert.AreEqual(4, o.ExpectedSteps);
            o.Tick(1f, 2f);
            Assert.AreEqual(4, steps.Count);
            Assert.AreEqual(MagazineStep.BoltRelease, steps[3]);
        }

        [Test]
        public void AyniKareEsAnlamlilar_TekOlay_SonrakiAyriSayilir()
        {
            var go = new GameObject("Wp087Events");
            try
            {
                var events = go.AddComponent<CharacterAnimationEvents>();
                var heard = new List<CharacterAnimationEvent>();
                System.Action<CharacterAnimationEvents, CharacterAnimationEvent> any = (c, e) => { if (c == events) heard.Add(e); };
                CharacterAnimationEvents.Any += any;
                try
                {
                    int collapsed = CharacterAnimationEvents.CollapsedCount;
                    events.mag_insert(0);
                    events.shell_in(1);   // sg_1 reload_shell: the same beat under its second name, same frame
                    CollectionAssert.AreEqual(new[] { CharacterAnimationEvent.MagIn }, heard, "mag_insert + shell_in = tek olay");
                    Assert.AreEqual(collapsed + 1, CharacterAnimationEvents.CollapsedCount);
                    events.mag_out();      // another beat on the same frame is its own
                    events.chamber();
                    events.rocket_seat(0); // chamber's synonym
                    CollectionAssert.AreEqual(new[] { CharacterAnimationEvent.MagIn, CharacterAnimationEvent.MagOut, CharacterAnimationEvent.Chamber }, heard);
                    Assert.AreEqual(0, CharacterAnimationEvents.BeatFamily(CharacterAnimationEvent.Fire), "olmayan aile");
                }
                finally
                {
                    CharacterAnimationEvents.Any -= any;
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sg1FisekDongusu_ToplamSureAyni_MermiDonguSonunda(bool empty)
        {
            PrototypeConfig config = Config;
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            ClassicWeaponSettings s = config.Weapons.Find(ClassicWeaponId.CombatShotgun);
            var rt = new ClassicWeaponRuntime();
            rt.Equip(s, new WeaponModifierState());
            int full = rt.Ammo, spend = empty ? full : 3;
            for (int i = 0; i < spend; i++) { rt.ConsumeShot(); rt.Tick(2f); }
            Assert.AreEqual(full - spend, rt.Ammo);
            Assert.IsTrue(rt.TryBeginReload());
            Assert.IsTrue(rt.ShellByShellReload);
            WeaponDefinition d = rt.Definition;
            float shell = rt.ShellReloadShellSeconds;
            float mult = shell / Mathf.Max(0.05f, d.ammo.perShellSeconds);
            int n = rt.ShellsQueuedThisReload;
            Assert.AreEqual(spend, n, "eksik fişek kadar döngü");
            // today's total: the tactical overhead + one interval per shell (+ the pump when it began empty), × the reload multiplier
            float expected = (d.ammo.reloadTacSeconds + n * d.ammo.perShellSeconds + (empty ? Mathf.Max(0f, d.ammo.reloadEmptySeconds - d.ammo.reloadTacSeconds) : 0f)) * mult;
            Assert.AreEqual(expected, rt.ReloadDuration, 1e-4f, "toplam reload süresi bugünkü gibi");
            Assert.AreEqual(rt.ReloadDuration, rt.ShellReloadStartSeconds + n * shell + rt.ShellReloadCloseSeconds, 1e-4f, "bölümler toplamı süre");

            var sections = new List<ShellReloadSection>();
            int ammo = rt.Ammo, lastLoop = -1;
            ShellReloadSection lastSection = rt.ShellSection;
            float lastProgress = rt.ShellSectionProgress01;
            for (int i = 0; i < 2000 && rt.IsReloading; i++)
            {
                rt.Tick(0.005f);
                if (rt.Ammo > ammo)
                {
                    // a round lands at the end of its shell loop: the tick before it was the loop near its end
                    Assert.AreEqual(ShellReloadSection.Shell, lastSection, "mermi döngü içinde iner");
                    Assert.GreaterOrEqual(lastProgress, 1f - 0.005f / shell - 1e-3f, "mermi döngünün sonunda");
                    ammo = rt.Ammo;
                }

                if (rt.IsReloading && (sections.Count == 0 || sections[sections.Count - 1] != rt.ShellSection)) sections.Add(rt.ShellSection);
                if (rt.ShellLoopIndex >= 0) { Assert.GreaterOrEqual(rt.ShellLoopIndex, lastLoop); lastLoop = rt.ShellLoopIndex; }
                lastSection = rt.ShellSection;
                lastProgress = rt.ShellSectionProgress01;
            }

            Assert.IsFalse(rt.IsReloading);
            Assert.AreEqual(full, rt.Ammo, "şarjör doldu");
            Assert.AreEqual(n - 1, lastLoop, "döngü sayısı = eksik fişek");
            CollectionAssert.AreEqual(new[] { ShellReloadSection.Start, ShellReloadSection.Shell, ShellReloadSection.Close }, sections);
        }
    }
}
