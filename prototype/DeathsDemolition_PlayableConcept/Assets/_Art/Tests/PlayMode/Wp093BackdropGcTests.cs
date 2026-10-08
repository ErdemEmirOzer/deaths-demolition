using System;
using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = NUnit.Framework.Is;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-093 Faz 1b (S-07 GC triage, Kalabalik40 IK test) · the WP-093 per-frame paths on REAL scene data: Boot → MainMenu (the V6
    /// backdrop: AP-023 lanterns, roofs with their RoofOccluder boxes, ponds, the plaza; vegetation as the run configures it). After the
    /// settle scans, 100 frames of LanternFlames.Tick (flicker, light pass + rendering-layer writes, glow blocks, halos, sparks),
    /// AtmosphereSystem.Step (incl. the drip landing log) and AmbientVfxSystem.Step (smoke, rings, drip drain, leaf sampling) on the
    /// calling thread must allocate 0 B, positive control first. Systems a build does not create are skipped (null-safe), but at
    /// least one must be live or the test is inconclusive.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093BackdropGcTests
    {
        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator MenuArkaPlani_FenerAtmosferOrtam_YuzKare0B()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 90f, "MainMenu");
            // The lantern settle scan (SettleFrames) and the ambient settle rescan (30 frames) allocate by design — let both pass.
            for (int i = 0; i <= LanternFlames.SettleFrames + 10; i++) yield return null;

            HitFeedbackDirector director = HitFeedbackDirector.Instance;
            LanternFlames lanterns = director != null && director.Vfx != null && director.Vfx.IsReady ? director.Vfx.Lanterns : null;
            AtmosphereSystem atmo = AtmosphereSystem.Active != null && AtmosphereSystem.Active.Ready ? AtmosphereSystem.Active : null;
            AmbientVfxSystem amb = AmbientVfxSystem.Active != null && AmbientVfxSystem.Active.Ready ? AmbientVfxSystem.Active : null;
            Debug.Log("[DD-TEST] wp093.backdrop lanterns=" + (lanterns != null ? lanterns.Placed + " lights=" + lanterns.LightsLit + " glow=" + lanterns.GlowSlots + " halos=" + lanterns.HalosLive : "none") +
                      " atmo=" + (atmo != null ? atmo.TotalLive.ToString() : "none") +
                      " ambient=" + (amb != null ? "sources " + amb.SmokeSourceCount + " ponds " + amb.Ponds + " puddles " + amb.PuddlePoints + " underEaves " + amb.PuddlesUnderEaves : "none"));
            Assume.That(lanterns != null || atmo != null || amb != null, "menu arka planında WP-093 sistemi yok: ölçüm anlamsız");

            VfxTier.Override = VfxTierLevel.Cinematic;   // every path on: sparks, drip / leaf rings, soft smoke
            float t = Time.time;
            const float dt = 1f / 60f;

            void Frames(int n)
            {
                for (int f = 0; f < n; f++)
                {
                    t += dt;
                    if (lanterns != null) lanterns.Tick(t);
                    if (atmo != null) atmo.Step(dt);
                    if (amb != null) amb.Step(dt);
                }
            }

            Frames(90);   // warm: tier change, light pass + mask writes, first range refreshes, prefill
            yield return null;
            Frames(30);

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() => Frames(100), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "WP-093 kare yolu (fener + atmosfer + ortam) gerçek menü sahnesinde ayırdı");
            Assert.IsNotNull(bait);
            GC.KeepAlive(bait);
        }
    }
}
