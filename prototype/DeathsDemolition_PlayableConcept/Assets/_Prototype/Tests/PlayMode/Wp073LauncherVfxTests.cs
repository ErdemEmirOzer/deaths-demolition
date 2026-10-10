using System.Collections;
using DD.Prototype.CameraSystem;
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
        private Camera _cam, _previousCam;
        private CameraMode _previousMode;
        [SetUp] public void Setup()
        {
            _previousMode=CameraModeService.Current;
            _flash=GameFeelSettings.FlashScale;VfxTier.Override=VfxTierLevel.High;
            var vfx=HitFeedbackDirector.Instance.Vfx;Assert.IsTrue(vfx.IsReady);
            _rocket=RocketVisualSystem.Ensure();_blast=ExplosionDirector.Ensure(vfx);
            ProjectileSystem.Clear();_rocket.Clear();_blast.Clear();
            // M-60: a fixed feedback camera 10 m behind the test spawn (billboard distance, edge smear, player guard read it).
            _previousCam=FeedbackCameraRef.Current;_cam=new GameObject("Wp073 launcher test camera").AddComponent<Camera>();_cam.enabled=false;
            _cam.transform.SetPositionAndRotation(new Vector3(0f,101f,190f),Quaternion.identity);FeedbackCameraRef.Set(_cam);
            Kip(CameraMode.TopDown);
        }
        // R3 (user 06.10: the game and its tests are top-down): every test names its camera. Top-down, the WP-093 brush layers
        // (FeelBrushVfxSystem, AP-037; feel contract §4.5) own the blast's ring and light and the director keeps the AP-010 smoke
        // and debris; third person keeps the WP-073 layered presentation. The brush starts each test with its eight reservations
        // free - a synchronous [Test] advances no frame, so an earlier test's would still be held (CI e8fc3f70: 6 order-dependent reds).
        private void Kip(CameraMode mode)
        {
            CameraModeService.Request(mode,instant:true);Assert.AreEqual(mode,CameraModeService.Current,"kamera kipi");
            FeelBrushVfxSystem.Instance?.ResetForTest();
            Assert.AreEqual(mode==CameraMode.TopDown,FeelBrushVfxSystem.OwnsTdLayers,"patlamanın fırça katmanları yalnız Üstten'de sahip");
        }
        [TearDown] public void Cleanup()
        {
            ProjectileSystem.Clear();_rocket.Clear();_blast.Clear();GameFeelSettings.FlashScale=_flash;VfxTier.Override=null;
            ExplosionDirector.GuardChestOverride=null;FeedbackCameraRef.Set(_previousCam);if(_cam!=null)Object.DestroyImmediate(_cam.gameObject);
            CameraModeService.Request(_previousMode,instant:true);FeelBrushVfxSystem.Instance?.ResetForTest();
        }
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
            _rocket.Present(.05f);Assert.AreEqual(1,_rocket.ActiveFlights);Assert.AreEqual(1,_rocket.DrawnRockets);Assert.IsTrue(_rocket.TryVisual(id,out Vector3 at));Assert.Greater(_rocket.SmokeEmitted,0);
            // WP-087 (b, C-00 05.10): the first drawn frame is the spawn point (the mouth); after the catch-up the drawn point is the simulated one
            Assert.Less((p.Origin-at).magnitude,1e-4f,"ilk çizilen kare doğum noktasında");Assert.AreEqual(position,p.Position,"Present simülasyonu değiştirmez");
            ProjectileSystem.Step(RocketVisualSystem.CatchUpSeconds);_rocket.Present(.05f);Assert.IsTrue(_rocket.TryVisual(id,out at));Assert.Less((ProjectileSystem.Get(0).Position-at).magnitude,1e-5f,"yakalama sonrası simülasyon noktasında");
        }
        // WP-087 R2 rocket first frame (b) (C-00 05.10): the visual starts at the spawn point, eases forward onto the simulated position
        // within CatchUpSeconds of the rocket's age (two 60 fps frames), never moves back; Present never writes the flight; a rocket first
        // seen older than NewbornSeconds is drawn where it is.
        [Test] public void Wp087_IlkKare_AgizdanCizilir_SimulasyonaYetisir_UcusDegismez()
        {
            Spawn();int id=ProjectileSystem.Get(0).Generation;Vector3 origin=ProjectileSystem.Get(0).Origin;Vector3 velocity=ProjectileSystem.Get(0).Velocity;
            Vector3 fwd=velocity.normalized;float step=1f/60f;Vector3 prev=origin;
            for(int frame=0;frame<4;frame++)
            {
                ProjectileSystem.Step(step);Vector3 sim=ProjectileSystem.Get(0).Position;float age=ProjectileSystem.Get(0).Age;
                _rocket.Present(step);Assert.AreEqual(sim,ProjectileSystem.Get(0).Position,"Present uçuşu yazmaz");
                Assert.Less((sim-(origin+velocity*age)).magnitude,1e-3f,"uçuş sabit hızda (ivme 0) — görsel yakalama simülasyona dokunmaz");
                Assert.IsTrue(_rocket.TryVisual(id,out Vector3 at));
                if(frame==0)Assert.Less((at-origin).magnitude,1e-4f,"ilk kare ağızda");
                else Assert.GreaterOrEqual(Vector3.Dot(at-prev,fwd),0f,"görsel geri gitmez (kare "+frame+")");
                if(frame>=2)Assert.Less((at-sim).magnitude,1e-4f,"iki karede simülasyona yetişti (kare "+frame+")");
                if(frame==1){Assert.Greater(Vector3.Dot(at-origin,fwd),0f);Assert.Less(Vector3.Dot(at-origin,fwd),Vector3.Dot(sim-origin,fwd));}
                prev=at;
            }
            ProjectileSystem.Clear();_rocket.Present(0f);
            Spawn();int late=ProjectileSystem.Get(0).Generation;ProjectileSystem.Step(.2f);
            _rocket.Present(.05f);Assert.IsTrue(_rocket.TryVisual(late,out Vector3 lateAt));Assert.AreEqual(ProjectileSystem.Get(0).Position,lateAt,"geç görülen roket olduğu yerde");
            Assert.AreEqual(1f,RocketVisualSystem.CatchUpWeight(0f));Assert.AreEqual(0f,RocketVisualSystem.CatchUpWeight(RocketVisualSystem.CatchUpSeconds));
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
        [UnityTest] public IEnumerator ReducedFlashes_PreservesColumnAndSoot_RemovesFlash_Ustten() => ReducedFlashesCore(CameraMode.TopDown);
        internal IEnumerator ReducedFlashesCore(CameraMode mode)
        {
            Kip(mode);bool td=mode==CameraMode.TopDown;int ember=td?0:1;   // top-down: §4.5's brush ring and light, no embers
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0f,99.5f,200f);ground.transform.localScale=new Vector3(10f,1f,10f);Physics.SyncTransforms();
            int flashes=_blast.FlashCards,columns=_blast.Columns,scorches=_blast.Scorches,wet=_blast.WetRings,embers=_blast.EmberBursts,residues=_blast.Residues;
            GameFeelSettings.FlashScale=.4f;Assert.IsTrue(_blast.Play(new Vector3(0f,100.1f,200f),5f,-1));
            if(td)   // top-down the flash is the brush blast light (FeelEvents, as HitFeedbackDirector.ReportExplosion raises it): 30 × FlashScale
            {
                FeelEvents.RaiseExplosion(new Vector3(0f,100.1f,200f),5f,0,ExplosionSource.Rocket);Light lit=null;
                foreach(Light l in FeelBrushVfxSystem.Instance.GetComponentsInChildren<Light>(true))if(l.enabled){Assert.IsNull(lit,"tek fırça ışığı");lit=l;}
                Assert.IsNotNull(lit,"Üstten: fırça patlama ışığı yanar");Assert.AreEqual(30f*.4f,lit.intensity,1e-4f,"ReduceFlashes: fırça ışığı × 0,4");
            }
            _blast.Tick(Time.time+.3f);
            Assert.AreEqual(flashes,_blast.FlashCards);Assert.AreEqual(columns+1,_blast.Columns);Assert.AreEqual(scorches+1,_blast.Scorches);
            Assert.AreEqual(wet+1,_blast.WetRings);Assert.AreEqual(embers+ember,_blast.EmberBursts);Assert.AreEqual(residues+1,_blast.Residues);
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
            var rocket=_rocket;var smoke=rocket.Smoke;var sparks=rocket.Sparks;
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
        [Test] public void FlightMesh_IsAp021PartRocketLod1_NoseOnProjectilePoint()
        {
            var lib=ClassicWeaponVisualLibrary.Current;var entry=lib!=null?lib.Find(ClassicWeaponId.Rl1):null;
            Assert.IsTrue(entry!=null&&entry.HasModel,"AP-021 rl_1 is in DD_ClassicWeapons on main");
            Assert.IsTrue(_rocket.UsesAuthoredRocket,"part_rocket_LOD1 passes the frame checks (else greybox)");
            Assert.LessOrEqual(_rocket.FlightTriangles,600,"direction §1.5 flying rocket ≤ 600 tri");
            Assert.That(_rocket.BodyLength,Is.InRange(.55f,.62f),"part_rocket measured 0.586 m nose-to-tail (Blender, v003)");
            Assert.IsTrue(RocketVisualSystem.TryAuthoredRocket(entry.model,out Mesh mesh,out Material[] mats,out Matrix4x4 local,out float len));
            Assert.IsNotNull(mesh);Assert.Greater(mats.Length,0);
            Bounds b=mesh.bounds;float minZ=float.MaxValue,maxZ=float.MinValue,maxR=0f;
            for(int c=0;c<8;c++)
            {
                Vector3 p=local.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((c&1)==0?-1f:1f,(c&2)==0?-1f:1f,(c&4)==0?-1f:1f)));
                minZ=Mathf.Min(minZ,p.z);maxZ=Mathf.Max(maxZ,p.z);maxR=Mathf.Max(maxR,Mathf.Abs(p.x),Mathf.Abs(p.y));
            }
            Assert.AreEqual(0f,maxZ,1e-4f,"nose on the projectile point");Assert.AreEqual(-len,minZ,1e-4f,"tail one body length behind");
            Assert.Less(maxR,.06f,"centred on the flight axis (Ø ≈ 0.09 m)");
        }
        [Test] public void FlightMesh_WithoutPart_FallsBackToGreybox()
        {
            Assert.IsFalse(RocketVisualSystem.TryAuthoredRocket(null,out _,out _,out _,out _));
            var go=new GameObject("no part_rocket");
            try{Assert.IsFalse(RocketVisualSystem.TryAuthoredRocket(go,out _,out _,out _,out _));}
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void CinematicSteam_IsDelayedAndOnlyRunsOnce_Ustten() => CinematicSteamCore(CameraMode.TopDown);
        internal void CinematicSteamCore(CameraMode mode)
        {
            Kip(mode);bool td=mode==CameraMode.TopDown;int puffs=td?0:6;   // top-down: §4.5's brush ring and light, no steam
            VfxTier.Override=VfxTierLevel.Cinematic;int steam=_blast.SteamPuffs,columns=_blast.Columns;
            _blast.Play(new Vector3(0f,100f,200f),5f,-1);_blast.Tick(Time.time+.19f);Assert.AreEqual(steam,_blast.SteamPuffs);
            _blast.Tick(Time.time+.21f);Assert.AreEqual(steam+puffs,_blast.SteamPuffs);
            _blast.Tick(Time.time+.21f);Assert.AreEqual(steam+puffs,_blast.SteamPuffs);if(td)Assert.AreEqual(columns+1,_blast.Columns,"duman sütunu bir kez");
        }
        [Test] public void TierSmokeLife_PreservesFlightIdentity()
        {
            Spawn();int id=ProjectileSystem.Get(0).Generation;
            foreach(VfxTierLevel tier in new[]{VfxTierLevel.Performance,VfxTierLevel.High,VfxTierLevel.Cinematic})
            {VfxTier.Override=tier;_rocket.Present(.05f);Assert.IsTrue(_rocket.TryVisual(id,out _));Assert.AreEqual(tier==VfxTierLevel.Performance?1f:tier==VfxTierLevel.High?2f:3.5f,_rocket.SmokeLife);}
        }
        // ---- M-56 / M-62 / M-58 (C-00 30.09)
        private static GameObject Ground(float top)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.transform.position=new Vector3(0f,top-.5f,200f);g.transform.localScale=new Vector3(20f,1f,20f);Physics.SyncTransforms();return g;}
        [UnityTest] public IEnumerator M56_ContactAt139m_LaysGroundLayers_ThreeMetreAirburstDoesNot()
        {
            var ground=Ground(100f);
            int scorch=_blast.Scorches,wet=_blast.WetRings,residue=_blast.Residues;
            Assert.IsTrue(_blast.Play(new Vector3(0f,101.39f,200f),5f,-1,ExplosionSource.Rocket));_blast.Tick(Time.time+.3f);
            Assert.AreEqual(scorch+1,_blast.Scorches,"1,39 m temas: scorch");Assert.AreEqual(wet+1,_blast.WetRings,"1,39 m temas: ıslak halka");Assert.AreEqual(residue+1,_blast.Residues,"1,39 m temas: kalıntı");
            _blast.Tick(Time.time+2f);scorch=_blast.Scorches;wet=_blast.WetRings;residue=_blast.Residues;
            Assert.IsTrue(_blast.Play(new Vector3(4f,103f,200f),2.5f,-1,ExplosionSource.Rocket));_blast.Tick(Time.time+.3f);   // reach max(2.5, 2) + 0.15 = 2.65 m
            Assert.AreEqual(scorch,_blast.Scorches);Assert.AreEqual(wet,_blast.WetRings);Assert.AreEqual(residue,_blast.Residues,"3 m hava patlaması: zemin katmanı yok");
            Object.Destroy(ground);yield return null;
        }
        [Test] public void M62_TwoSmallThenRocket_RocketTakesTheFullSlot_SmallPlaysNoEmbersOrColumn_Ustten() => M62TwoSmallThenRocketCore(CameraMode.TopDown);
        internal void M62TwoSmallThenRocketCore(CameraMode mode)
        {
            Kip(mode);bool td=mode==CameraMode.TopDown;int ember=td?0:1;   // top-down: §4.5, no embers at all
            int full=_blast.FullPresentations,light=_blast.LightPresentations,embers=_blast.EmberBursts,columns=_blast.Columns,flashes=_blast.FlashCards;
            var vfx=HitFeedbackDirector.Instance.Vfx;int played=vfx.Played;
            Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),3f,-1,ExplosionSource.Small));Assert.IsTrue(_blast.Play(new Vector3(4f,100f,200f),3f,-1,ExplosionSource.Small));
            Assert.IsTrue(_blast.Play(new Vector3(8f,100f,200f),5f,-1,ExplosionSource.Rocket));
            Assert.AreEqual(full+1,_blast.FullPresentations,"roket Full yuvasını alır");Assert.AreEqual(light+2,_blast.LightPresentations);
            // C-00 10.10 (user note 4): top-down draws the fireball cards with the brush layers again - three fireballs (two at the
            // small preset) and the rocket's flash card (no ground here, so no ring either way)
            if(td){Assert.AreEqual(flashes+1,_blast.FlashCards,"Üstten: roket flaş kartı");Assert.AreEqual(played+4,vfx.Played,"Üstten: 3 ateş topu + 1 flaş kartı");}
            _blast.Tick(Time.time+.3f);Assert.AreEqual(embers+ember,_blast.EmberBursts,"kor yalnız roket");Assert.AreEqual(columns+1,_blast.Columns,"sütun yalnız roket");
        }
        [Test] public void M62_CinematicExplosiveRounds_AtMostOneShadowedLight_Ustten() => M62ShadowCore(CameraMode.TopDown);
        internal void M62ShadowCore(CameraMode mode)
        {
            Kip(mode);bool td=mode==CameraMode.TopDown;
            VfxTier.Override=VfxTierLevel.Cinematic;var lights=MuzzleSystem.Ensure();var brush=FeelBrushVfxSystem.Instance;int peak=0;
            System.Func<int> shadowed=()=>td?brush.ShadowedLightsLit:lights.ShadowedLightsLit;
            for(int i=0;i<25;i++){Blast(td,new Vector3(i%5*3f,100f,200f+i/5*3f),3f,ExplosionSource.Small);peak=Mathf.Max(peak,shadowed());}   // lmg_1 explosive rounds, 5 s × 5/s
            Assert.AreEqual(0,peak,"küçük patlama gölgesiz");
            int full=_blast.FullPresentations;
            Blast(td,new Vector3(20f,100f,200f),5f,ExplosionSource.Rocket);
            if(td)Assert.AreEqual(full+1,_blast.FullPresentations,"roket sunulur (sekiz fırça yuvası küçük patlamalarla dolu olsa da)");
            Assert.AreEqual(ExplosionDirector.LightCastsShadows?1:0,shadowed(),"gölgeli ışık yalnız roket, ≤ 1");
            if(!td)return;
            Blast(td,new Vector3(26f,100f,200f),5f,ExplosionSource.Rocket);
            Assert.AreEqual(ExplosionDirector.LightCastsShadows?1:0,shadowed(),"ikinci roket: gölgeli ışık yine ≤ 1");
        }
        // top-down HitFeedbackDirector.ReportExplosion's order: the director's reservation, then FeelEvents (the brush ring and light)
        private void Blast(bool td,Vector3 at,float radius,ExplosionSource source)
        {
            _blast.Play(at,radius,-1,source);if(td)FeelEvents.RaiseExplosion(at,radius,0,source);
        }
        [Test] public void M58_ExhaustPoolIsEight_NinthRocketFliesWithoutCard()
        {
            Assert.AreEqual(8,_rocket.ExhaustPoolSize);
            for(int i=0;i<10;i++)Spawn();ProjectileSystem.Step(.025f);_rocket.Present(.05f);
            Assert.AreEqual(10,_rocket.ActiveFlights);Assert.AreEqual(8,_rocket.ExhaustAlive);
        }
        // ---- M-60 (C-00 30.09): backblast, far billboard, near-blast edge smear, §4.1 column guard
        [UnityTest] public IEnumerator M60_Backblast_RearCone_DarkensGroundOnlyWithinReach()
        {
            var ground=Ground(100f);
            int blasts=_blast.Backblasts,decals=_blast.BackblastDecals;
            Assert.IsTrue(_blast.PlayBackblast(new Vector3(0f,101.5f,200f),Vector3.back));   // shoulder-high tube rear, ground 1.5 m below
            Assert.AreEqual(blasts+1,_blast.Backblasts);Assert.AreEqual(decals+1,_blast.BackblastDecals,"arka koni ıslak zemini koyulaştırır");
            Assert.IsTrue(_blast.PlayBackblast(new Vector3(0f,105f,200f),Vector3.back));     // 5 m up: no ground within 2.5 m
            Assert.AreEqual(blasts+2,_blast.Backblasts);Assert.AreEqual(decals+1,_blast.BackblastDecals,"zemin menzil dışında: decal yok");
            VfxTier.Override=VfxTierLevel.Performance;Assert.IsTrue(_blast.PlayBackblast(new Vector3(0f,101.5f,200f),Vector3.back));
            Assert.AreEqual(blasts+3,_blast.Backblasts,"Performans: küçük koni, kimlik korunur");
            Object.Destroy(ground);yield return null;
        }
        [Test] public void M60_FarRocket_IsBillboardBeyondTierRange_CinematicKeepsMesh()
        {
            _cam.transform.position=new Vector3(0f,100f,150f);   // ≈ 50 m from the spawn
            Spawn();_rocket.Present(.05f);
            Assert.AreEqual(1,_rocket.BillboardRockets,"Yüksek: 25 m ötesi billboard");Assert.AreEqual(0,_rocket.DrawnRockets,"billboard mesh sayılmaz");
            VfxTier.Override=VfxTierLevel.Cinematic;_rocket.Present(.05f);
            Assert.AreEqual(0,_rocket.BillboardRockets,"Sinematik: her mesafede mesh");Assert.AreEqual(1,_rocket.DrawnRockets);
            _cam.transform.position=new Vector3(0f,100f,180f);   // ≈ 20 m
            VfxTier.Override=VfxTierLevel.Performance;_rocket.Present(.05f);Assert.AreEqual(1,_rocket.BillboardRockets,"Performans: 15 m ötesi");
            VfxTier.Override=VfxTierLevel.High;_rocket.Present(.05f);Assert.AreEqual(0,_rocket.BillboardRockets,"Yüksek: 20 m ≤ 25 m mesh");Assert.AreEqual(1,_rocket.DrawnRockets);
        }
        [Test] public void M60_NearBlast_EdgeSmear_UnderFourMetres_OffWithReducedFlashes_Ustten()
        {
            // top-down: the brush ring and light own the flash (§4.5) - no edge smear at any distance
            _cam.transform.position=new Vector3(0f,100f,198f);int smears=_blast.EdgeSmears;
            Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),5f,-1,ExplosionSource.Rocket));Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),3f,-1,ExplosionSource.Small));
            Assert.AreEqual(smears,_blast.EdgeSmears,"Üstten: kenar lekesi yok");Assert.IsFalse(_blast.EdgeSmearVisible);
        }
        internal void M60NearBlastThirdPersonCore()
        {
            Kip(CameraMode.ThirdPerson);
            _cam.transform.position=new Vector3(0f,100f,198f);int smears=_blast.EdgeSmears;
            Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),5f,-1,ExplosionSource.Rocket));Assert.AreEqual(smears+1,_blast.EdgeSmears,"2 m roket");Assert.IsTrue(_blast.EdgeSmearVisible);
            Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),3f,-1,ExplosionSource.Small));Assert.AreEqual(smears+2,_blast.EdgeSmears,"2 m küçük patlama");
            Assert.IsTrue(_blast.Play(new Vector3(0f,100f,208f),5f,-1,ExplosionSource.Rocket));Assert.AreEqual(smears+2,_blast.EdgeSmears,"10 m: leke yok");
            GameFeelSettings.FlashScale=.4f;Assert.IsTrue(_blast.Play(new Vector3(0f,100f,200f),5f,-1,ExplosionSource.Rocket));Assert.AreEqual(smears+2,_blast.EdgeSmears,"ReduceFlashes: kapalı");
        }
        [Test] public void M60_ColumnBetweenCameraAndPlayerChest_IsGuarded()
        {
            // GelSystem.GuardFactor semantics: covered = on the camera → chest segment (a blast behind the player is drawn behind it).
            _cam.transform.position=new Vector3(0f,101f,190f);ExplosionDirector.GuardChestOverride=new Vector3(0f,101.3f,200f);
            int guarded=_blast.GuardedColumns,columns=_blast.Columns;
            Assert.IsTrue(_blast.Play(new Vector3(0f,101f,196f),5f,-1,ExplosionSource.Rocket));Assert.AreEqual(guarded+1,_blast.GuardedColumns,"kamera–göğüs arasında: α × 0,5");
            Assert.IsTrue(_blast.Play(new Vector3(0f,101f,206f),5f,-1,ExplosionSource.Rocket));Assert.AreEqual(guarded+1,_blast.GuardedColumns,"oyuncunun arkasında: örtmez");
            Assert.IsTrue(_blast.Play(new Vector3(12f,101f,196f),5f,-1,ExplosionSource.Small));Assert.AreEqual(guarded+1,_blast.GuardedColumns,"yanda: örtmez");
            _blast.Tick(Time.time+.3f);Assert.AreEqual(columns+2,_blast.Columns,"sütun yine çizilir (yarı α)");
        }
    }

    /// <summary>
    /// R3 (user 06.10 / 07.10: the tests run top-down only, FP / TP stay as they are): the WP-073 rows whose top-down twin differs,
    /// kept under their original names in third person (the layered presentation; the brush owns the top-down blast). FPTP on the
    /// CLASS - main-ci's PlayMode filter does not exclude a method-level FPTP inside a categorised fixture (07.10).
    /// </summary>
    [TestFixture, Category("WP073"), Category("FPTP")]
    public sealed class Wp073LauncherVfxFptpTests
    {
        private readonly Wp073LauncherVfxTests _t=new Wp073LauncherVfxTests();
        [SetUp] public void Setup()=>_t.Setup();
        [TearDown] public void Cleanup()=>_t.Cleanup();
        [UnityTest] public IEnumerator ReducedFlashes_PreservesColumnAndSoot_RemovesFlash()=>_t.ReducedFlashesCore(CameraMode.ThirdPerson);
        [Test] public void CinematicSteam_IsDelayedAndOnlyRunsOnce()=>_t.CinematicSteamCore(CameraMode.ThirdPerson);
        [Test] public void M62_TwoSmallThenRocket_RocketTakesTheFullSlot_SmallPlaysNoEmbersOrColumn()=>_t.M62TwoSmallThenRocketCore(CameraMode.ThirdPerson);
        [Test] public void M62_CinematicExplosiveRounds_AtMostOneShadowedLight()=>_t.M62ShadowCore(CameraMode.ThirdPerson);
        [Test] public void M60_NearBlast_EdgeSmear_UnderFourMetres_OffWithReducedFlashes()=>_t.M60NearBlastThirdPersonCore();
    }
}
