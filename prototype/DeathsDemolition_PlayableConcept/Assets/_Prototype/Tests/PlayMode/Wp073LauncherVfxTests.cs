using System.Collections;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is=NUnit.Framework.Is;
namespace DD.Prototype.Tests.PlayMode
{
    [TestFixture, Category("WP073")]
    public sealed class Wp073LauncherVfxTests
    {
        private RocketVisualSystem _rocket;
        private ExplosionDirector _blast;
        private float _flash;
        [SetUp] public void Setup()
        {
            _flash=GameFeelSettings.FlashScale;VfxTier.Override=VfxTierLevel.High;
            var vfx=HitFeedbackDirector.Instance.Vfx;Assert.IsTrue(vfx.IsReady);
            _rocket=RocketVisualSystem.Ensure();_blast=ExplosionDirector.Ensure(vfx);
            ProjectileSystem.Clear();_rocket.Clear();_blast.Clear();
        }
        [TearDown] public void Cleanup()
        {ProjectileSystem.Clear();_rocket.Clear();_blast.Clear();GameFeelSettings.FlashScale=_flash;VfxTier.Override=null;}
        private static int Spawn(float range=70f, float life=2.2f, ProjectileKind kind=ProjectileKind.Rocket)
        {
            var def=Arsenal.Get(ClassicWeaponId.Rl1);Assert.IsNotNull(def);
            var l=ProjectileLaunch.From(def,ClassicWeaponId.Rl1,range);
            l.Kind=kind;l.MaxRange=range;l.MaxLifetime=life;l.Gravity=0f;l.GravityCoast=0f;l.WobbleDegrees=0f;l.RocketAcceleration=0f;
            var shot=new ShotRequest {Origin=new Vector3(0f,100f,200f),Direction=Vector3.forward,Damage=1f,Range=range,Power=WeaponPower.Heavy,PierceDamageFalloff=1f};
            return ProjectileSystem.Spawn(shot,l);
        }
        [Test] public void Flight_UsesGeneration_AfterEarlierLiveIndexEnds()
        {
            Spawn(70f,.01f,ProjectileKind.Bullet);Spawn();ProjectileSystem.Step(.025f);Assert.AreEqual(1,ProjectileSystem.LiveCount);
            ref readonly ProjectileState p=ref ProjectileSystem.Get(0);int id=p.Generation;Vector3 position=p.Position;
            _rocket.Present(.05f);Assert.AreEqual(1,_rocket.ActiveFlights);Assert.IsTrue(_rocket.TryVisual(id,out Vector3 at));Assert.AreEqual(position,at);Assert.Greater(_rocket.SmokeEmitted,0);
        }
        [Test] public void Clear_StopsFlight_NoBlast_AndNoFurtherEmission()
        {
            Spawn();_rocket.Present(.05f);int smoke=_rocket.SmokeEmitted,blast=_blast.Presentations;
            ProjectileSystem.Clear();_rocket.Present(.1f);Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(smoke,_rocket.SmokeEmitted);Assert.AreEqual(blast,_blast.Presentations);
        }
        [Test] public void Eviction_EndsOriginalIdentity_WithoutExplosion()
        {
            Spawn();int id=ProjectileSystem.Get(0).Generation;_rocket.Present(.05f);int blast=_blast.Presentations;
            for(int i=0;i<ProjectileSystem.Capacity;i++)Spawn(70f,2f,ProjectileKind.Bullet);
            _rocket.Present(.05f);Assert.IsFalse(_rocket.TryVisual(id,out _));Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blast,_blast.Presentations);
        }
        [Test] public void ActualRangeEnd_ArmedAirburst_ExactlyOnePresentation_NoGroundDecal()
        {
            int blasts=_blast.Presentations,scorch=_blast.Scorches;Spawn(12f);_rocket.Present(.05f);ProjectileSystem.SimulateFor(.6f);_rocket.Present(.05f);
            Assert.AreEqual(0,ProjectileSystem.LiveCount);Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blasts+1,_blast.Presentations);Assert.AreEqual(scorch,_blast.Scorches);
        }
        [Test] public void ActualUnarmedRangeEnd_Dud_WithoutBlast()
        {
            int blasts=_blast.Presentations,duds=_rocket.DudsPlayed;Spawn(1f);_rocket.Present(.05f);ProjectileSystem.SimulateFor(.1f);_rocket.Present(.05f);
            Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blasts,_blast.Presentations);Assert.AreEqual(duds+1,_rocket.DudsPlayed);
        }
        [Test] public void ConcurrentTwoFull_ThirdLight_AndSchedulerCompletes()
        {
            int full=_blast.FullPresentations,light=_blast.LightPresentations,column=_blast.Columns;
            for(int i=0;i<3;i++)Assert.IsTrue(_blast.Play(new Vector3(i*4f,100f,200f),5f,-1));
            Assert.AreEqual(full+2,_blast.FullPresentations);Assert.AreEqual(light+1,_blast.LightPresentations);
            _blast.Tick(Time.time+.3f);Assert.AreEqual(column+3,_blast.Columns);_blast.Tick(Time.time+2f);Assert.AreEqual(0,_blast.Pending);
        }
        [UnityTest] public IEnumerator ReducedFlashes_PreservesColumnAndSoot_RemovesFlash()
        {
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0f,99.5f,200f);ground.transform.localScale=new Vector3(10f,1f,10f);Physics.SyncTransforms();
            int flashes=_blast.FlashCards,columns=_blast.Columns,scorches=_blast.Scorches;
            GameFeelSettings.FlashScale=.4f;Assert.IsTrue(_blast.Play(new Vector3(0f,100.1f,200f),5f,-1));_blast.Tick(Time.time+.3f);
            Assert.AreEqual(flashes,_blast.FlashCards);Assert.AreEqual(columns+1,_blast.Columns);Assert.AreEqual(scorches+1,_blast.Scorches);
            Object.Destroy(ground);yield return null;
        }
        [Test] public void PausedPresentation_DoesNotEmit()
        {
            Spawn();_rocket.Present(.05f);int smoke=_rocket.SmokeEmitted,exhaust=_rocket.ExhaustAlive;
            for(int i=0;i<20;i++)_rocket.Present(0f);
            Assert.AreEqual(smoke,_rocket.SmokeEmitted);Assert.AreEqual(exhaust,_rocket.ExhaustAlive);
        }
        [Test] public void ActualLifetimeEnd_ArmedAirburst_ExactlyOnce()
        {
            int blasts=_blast.Presentations;Spawn(70f,.3f);_rocket.Present(.05f);ProjectileSystem.SimulateFor(.4f);_rocket.Present(.05f);
            Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blasts+1,_blast.Presentations);
        }
        [Test] public void ActualWorldContact_ArmedAndDud_FollowAuthoritativeBlast()
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer=DD.Prototype.Core.PrototypeLayers.Environment;
            try
            {
                wall.transform.position=new Vector3(0f,100f,210f);wall.transform.localScale=new Vector3(4f,4f,.2f);Physics.SyncTransforms();
                int blasts=_blast.Presentations;Spawn();_rocket.Present(.05f);ProjectileSystem.SimulateFor(.6f);_rocket.Present(.05f);
                Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blasts+1,_blast.Presentations);
                _blast.Clear();wall.transform.position=new Vector3(0f,100f,202f);Physics.SyncTransforms();
                blasts=_blast.Presentations;int duds=_rocket.DudsPlayed;Spawn();_rocket.Present(.05f);ProjectileSystem.SimulateFor(.2f);_rocket.Present(.05f);
                Assert.AreEqual(0,_rocket.ActiveFlights);Assert.AreEqual(blasts,_blast.Presentations);Assert.AreEqual(duds+1,_rocket.DudsPlayed);
            }
            finally {Object.DestroyImmediate(wall);}
        }
        [Test, Category("Alloc")] public void WarmPresentation_DoesNotAllocateOrConsumeGameplayRandom()
        {
            Spawn();int generation=ProjectileSystem.Get(0).Generation;
            var rocket=_rocket;var smoke=rocket.Smoke;
            var sparkField=typeof(RocketVisualSystem).GetField("_sparks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(sparkField);var sparks=(ParticleSystem)sparkField.GetValue(rocket);
            Assert.IsNotNull(smoke);Assert.IsNotNull(sparks);
            // Warm both real particle-emission branches. Then .08s crosses both accumulated rates.
            rocket.Present(.17f);rocket.Present(.17f);rocket.Present(.10f);
            Assert.IsTrue(rocket.TryVisual(generation,out Vector3 at));
            Assert.AreEqual(ProjectileSystem.Get(0).Position,at);Assert.AreEqual(1,rocket.ActiveFlights);
            int emittedBefore=rocket.SmokeEmitted,smokeBefore=smoke.particleCount,embersBefore=sparks.particleCount;
            var state=Random.state;byte[] bait=null;
            NUnit.Framework.TestDelegate allocateBait=()=>{bait=new byte[64*1024];};
            NUnit.Framework.TestDelegate presentEmission=()=>rocket.Present(.08f);
            Assert.That(allocateBait,UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "pre-control: calling-thread recorder must detect 64KiB allocation");
            Assert.That(presentEmission,Is.Not.AllocatingGCMemory(),
                "scope: warmed RocketVisualSystem.Present with smoke/ember emission on this calling thread");
            // Observe actual branches and live identity outside the measured allocation boundary.
            Assert.Greater(rocket.SmokeEmitted,emittedBefore);Assert.Greater(smoke.particleCount,smokeBefore);
            Assert.Greater(sparks.particleCount,embersBefore);
            Assert.IsTrue(rocket.TryVisual(generation,out Vector3 after));Assert.AreEqual(at,after);
            Assert.AreEqual(1,rocket.ActiveFlights);Assert.AreEqual(generation,ProjectileSystem.Get(0).Generation);
            Assert.AreEqual(state,Random.state,"VFX must not change gameplay wobble randomness");
            Assert.That(allocateBait,UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "post-control: calling-thread recorder must still detect 64KiB allocation");
            Assert.AreEqual(64*1024,bait.Length,"keep allocation bait live through measured assertion");
        }
        [Test] public void CinematicSteam_IsDelayedAndOnlyRunsOnce()
        {
            VfxTier.Override=VfxTierLevel.Cinematic;int steam=_blast.SteamPuffs;
            _blast.Play(new Vector3(0f,100f,200f),5f,-1);_blast.Tick(Time.time+.19f);Assert.AreEqual(steam,_blast.SteamPuffs);
            _blast.Tick(Time.time+.21f);Assert.AreEqual(steam+6,_blast.SteamPuffs);
            _blast.Tick(Time.time+.21f);Assert.AreEqual(steam+6,_blast.SteamPuffs);
        }
        [Test] public void TierSmokeLife_PreservesFlightIdentity()
        {
            Spawn();int id=ProjectileSystem.Get(0).Generation;
            foreach(VfxTierLevel tier in new[]{VfxTierLevel.Performance,VfxTierLevel.High,VfxTierLevel.Cinematic})
            {VfxTier.Override=tier;_rocket.Present(.05f);Assert.IsTrue(_rocket.TryVisual(id,out _));Assert.AreEqual(tier==VfxTierLevel.Performance?1f:tier==VfxTierLevel.High?2f:3.5f,_rocket.SmokeLife);}
        }
    }
}
