using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-073 B3: the single visual owner called by the existing ReportExplosion path.
    /// Never subscribes Detonated or replays damage/audio/feel. Ground probe is presentation only.</summary>
    public sealed class ExplosionDirector : MonoBehaviour
    {
        private static ExplosionDirector s_instance;
        public static ExplosionDirector Instance => s_instance;
        public const int FullCapacity = 2, ScheduleCapacity = 32;
        // Ground marks — direction §1.5 "Patlama sırası" (WP-073-VFX-FEEL-V3-DIRECTION.md l.102): "scorch 2,4 m / 12 s + ıslak koyu
        // halka 3,0 m / 6 s". §1.5 gives no alpha; it reuses the existing AP-010 scorch ("mevcut", WP-069 §7), whose manifest sets it.
        public const float ScorchSize = 2.4f, WetRingSize = 3f, WetRingSeconds = 6f;
        // M-57 (C-00 30.09): the scorch / crater life follows the impact-decal pattern per tier (DIRECTION §5 8 / 12 / 12);
        // the Cinematic value waits on C-00's 12-or-20 s call (VfxDressing.ImpactDecalSeconds is 20 on Cinematic, DEC-100).
        [PrototypeOnly(PendingDecisions.None, "M-57: Cinematic scorch life 12 s per DIRECTION §5 until C-00 decides 12 vs 20 (DEC-100 impact decals 20).", true)]
        public const float ScorchSecondsCinematic = 12f;
        public static float ScorchSeconds => VfxTier.Pick(8f, 12f, ScorchSecondsCinematic);
        [PrototypeOnly(PendingDecisions.None, "AP-010 manifest v004 decal_scorch usage_note: alpha peak 0.40 (direction §1.5 row names no alpha; it reuses this scorch).")]
        public const float ScorchAlpha = 0.40f;
        [PrototypeOnly(PendingDecisions.None, "Wet dark ring alpha: direction §1.5 gives size/time only (3.0 m / 6 s); 0.35 is a stand-in.", true)]
        public const float WetRingAlpha = 0.35f;
        // C-00 30.09 (A): the explosion light is the one shadow-casting VFX light, Cinematic only (URP additional-light shadows: S-05).
        // "-ddVfxExplosionShadowsOff" (development builds / Editor) turns it off for the quiet-window A/B perf run; never in release.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly bool ShadowsOffForAb = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ddVfxExplosionShadowsOff") >= 0;
#else
        private const bool ShadowsOffForAb = false;
#endif
        public static bool LightCastsShadows => VfxTier.Current == VfxTierLevel.Cinematic && !ShadowsOffForAb;

        // ---- presentation numbers (direction §1.5; one per line — M-57)
        [PrototypeOnly(PendingDecisions.None, "B3 blast light: rocket Full 40 / Light 12, range 9 m × scale, hold 0.08 s + fade 0.35 s; a small blast ≤ 12, no shadow (M-62).", true)]
        public const float LightFull = 40f;
        public const float LightSmall = 12f;
        public const float LightRange = 9f;
        public const float LightHold = .08f;
        public const float LightFade = .35f;
        [PrototypeOnly(PendingDecisions.None, "B3 cards: fireball 3.6 m × scale (× 0.65 when not Full) / 1.6 s; flash card 2 m / 1 frame; shock ring 7 m / 0.3 s, α ≤ 0.5 (AD M6); Dirt crater 1.8 m.", true)]
        public const float FireballSize = 3.6f;
        public const float FireballSeconds = 1.6f;
        public const float FireballLightScale = .65f;
        public const float FlashCardSize = 2f;
        public const float RingSize = 7f;
        public const float RingSeconds = .3f;
        public const float RingAlpha = .5f;
        public const float CraterSize = 1.8f;
        // M-56: the ground probe reaches max(radius, 2) + 0.15 m below the blast; a ground layer under a blast h metres up
        // fades in alpha and size from 1 (on the ground) to GroundFadeFloor (at the probe's reach).
        public const float GroundProbeMin = 2f;
        public const float GroundProbeLift = .15f;
        public const float GroundFadeFloor = .35f;
        public static float GroundProbeLength(float radius) => Mathf.Max(radius, GroundProbeMin) + GroundProbeLift;
        private struct Blast { public bool Active, Full, Rocket, Grounded; public Vector3 At, Ground, Normal; public SurfaceKind Surface; public float Start, Scale, GroundFade; public int Stages, Mask; }
        private readonly Blast[] _blasts = new Blast[ScheduleCapacity];
        private Texture2D _dot;
        private VfxDressing _owner;
        private Material _smokeMaterial, _debrisMaterial, _leafMaterial, _emberMaterial;
        private ParticleSystem _column, _debris, _leaves, _embers;
        private MuzzleSystem _lights;
        private int _next;
        public int Presentations { get; private set; }
        public int FullPresentations { get; private set; }
        public int LightPresentations { get; private set; }
        public int FlashCards { get; private set; }
        public int Scorches { get; private set; }
        public int WetRings { get; private set; }
        public int EmberBursts { get; private set; }
        public int Columns { get; private set; }
        public int Residues { get; private set; }
        public int SteamPuffs { get; private set; }
        public int Pending { get { int n=0;for(int i=0;i<_blasts.Length;i++)if(_blasts[i].Active)n++;return n; } }
        public static ExplosionDirector Ensure(VfxDressing owner)
        {
            if(s_instance==null)s_instance=FindObjectOfType<ExplosionDirector>();
            if(s_instance==null){var go=new GameObject("[ExplosionDirector]");DontDestroyOnLoad(go);s_instance=go.AddComponent<ExplosionDirector>();}
            s_instance._owner=owner;return s_instance;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()=>s_instance=null;
        private void Awake()
        {
            s_instance=this;
            _smokeMaterial=B3Particles.Material(VfxIds.CardSmoke);_debrisMaterial=new Material(ImpactSystem.Instance.BlastResidueMaterial);
            _column=B3Particles.Build(transform,"BlastWetColumn",_smokeMaterial,128,0f,true);
            _debris=B3Particles.Build(transform,"BlastResidue",_debrisMaterial,128,1f,false);
            var sheet=_debris.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=4;sheet.numTilesY=4;sheet.startFrame=new ParticleSystem.MinMaxCurve(0f,.999f);sheet.frameOverTime=new ParticleSystem.MinMaxCurve(0f);
            _leafMaterial=B3Particles.Material(VfxIds.LeafBurst);_leaves=B3Particles.Build(transform,"BlastLeaves",_leafMaterial,96,1f,false);
            if(VfxLibrary.TryGetPlayable(VfxIds.LeafBurst,out VfxCatalogEntry leaf)){var ls=_leaves.textureSheetAnimation;ls.enabled=true;ls.numTilesX=leaf.Columns;ls.numTilesY=leaf.Rows;ls.startFrame=new ParticleSystem.MinMaxCurve(0f,.999f);ls.frameOverTime=new ParticleSystem.MinMaxCurve(0f);}
            _dot=B3Particles.Dot();_emberMaterial=B3Particles.Material(VfxIds.CardSmoke);_emberMaterial.mainTexture=_dot;_emberMaterial.SetTexture("_BaseMap",_dot);_embers=B3Particles.Build(transform,"BlastEmbers",_emberMaterial,128,1f,false);
            _lights=MuzzleSystem.Ensure();SceneManager.activeSceneChanged+=OnSceneChanged;
        }
        private void OnDestroy(){SceneManager.activeSceneChanged-=OnSceneChanged;if(s_instance==this)s_instance=null;Destroy(_smokeMaterial);Destroy(_debrisMaterial);Destroy(_leafMaterial);Destroy(_emberMaterial);Destroy(_dot);}
        private void OnSceneChanged(Scene a,Scene b)=>Clear();
        public void Clear(){for(int i=0;i<_blasts.Length;i++)_blasts[i]=default;_column?.Clear();_debris?.Clear();_leaves?.Clear();_embers?.Clear();}
        public bool Play(Vector3 at, float radius, int mask) => Play(at, radius, mask, ExplosionSource.Rocket);

        /// <summary>M-62: a rocket takes a Full slot (≤ FullCapacity) and the Cinematic shadowed light; a small blast plays card +
        /// ring + scorch, light ≤ LightSmall, no shadow, no embers / column / steam / residue / wet ring.</summary>
        public bool Play(Vector3 at, float radius, int mask, ExplosionSource source)
        {
            if (_owner == null || !_owner.IsReady || mask == 0) return false;
            bool rocket = source == ExplosionSource.Rocket;
            if (!rocket) mask &= 1 | 2 | 8;
            int full = 0;
            for (int i = 0; i < _blasts.Length; i++) if (_blasts[i].Active && _blasts[i].Full) full++;
            ref Blast b = ref _blasts[_next];
            _next = (_next + 1) % _blasts.Length;
            b = new Blast { Active = true, Rocket = rocket, Full = rocket && full < FullCapacity, At = at, Start = Time.time, Scale = Mathf.Clamp(radius / 5f, .3f, 1f), Mask = mask };
            int layerMask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            float reach = GroundProbeLength(radius);
            b.Grounded = Physics.Raycast(at + Vector3.up * GroundProbeLift, Vector3.down, out RaycastHit ground, reach + GroundProbeLift, layerMask, QueryTriggerInteraction.Ignore);
            if (b.Grounded)
            {
                b.Ground = ground.point;
                b.Normal = ground.normal;
                b.Surface = SurfaceResolver.Resolve(ground.collider);
                b.GroundFade = Mathf.Lerp(1f, GroundFadeFloor, Mathf.Clamp01((at.y - ground.point.y) / reach));
            }

            Presentations++;
            if (b.Full) FullPresentations++;
            else LightPresentations++;
            float alpha = GameFeelSettings.FlashScale;
            if ((mask & 1) != 0)
            {
                Card(VfxIds.Explosion, at, Quaternion.identity, FlipbookEffect.Facing.Camera, FireballSize * b.Scale * (b.Full ? 1f : FireballLightScale), FireballSeconds, Color.white, alpha);
                if (b.Full && alpha >= .99f && VfxTier.Current != VfxTierLevel.Performance
                    && Card(VfxIds.Explosion, at, Quaternion.identity, FlipbookEffect.Facing.Camera, FlashCardSize * b.Scale, 1f / 60f, new Color(1f, .75f, .45f) * 12f, 1f, true))
                    FlashCards++;
                _lights.FlashAt(at, new Color(1f, .69f, .44f), b.Full ? LightFull : LightSmall, LightRange * b.Scale, LightHold, LightFade, false, b.Full && LightCastsShadows);
            }

            if (b.Grounded && (mask & 2) != 0)
            {
                FlipbookEffect ring = CardEffect(VfxIds.Shockwave, b.Ground + b.Normal * .05f, Quaternion.LookRotation(b.Normal), FlipbookEffect.Facing.Fixed, RingSize * b.Scale * b.GroundFade, RingSeconds, VfxDressing.ShockRingDustTint, RingAlpha * b.GroundFade);   // AD M6 dust tint
                ring?.SetGrow(b.Scale * b.GroundFade, RingSeconds);
            }

            if (b.Grounded && (mask & 8) != 0 && b.Surface != SurfaceKind.Water)
            {
                if (Decal(b.Ground, b.Normal, ScorchSize * b.Scale * b.GroundFade, ScorchSeconds, ScorchAlpha * b.GroundFade, Color.white)) Scorches++;
                if (rocket && Decal(b.Ground, b.Normal, WetRingSize * b.Scale * b.GroundFade, WetRingSeconds, WetRingAlpha * b.GroundFade, new Color(.3f, .32f, .34f))) WetRings++;
            }

            return true;
        }

        private void Update()=>Tick(Time.time);
        public void Tick(float now)
        {
            for(int i=0;i<_blasts.Length;i++)
            {
                ref Blast b=ref _blasts[i];if(!b.Active)continue;float age=now-b.Start;
                if(age>=.016f && (b.Stages&1)==0)
                {b.Stages|=1;if(b.Full && (b.Mask&4)!=0){int count=VfxTier.Pick(10,24,32);for(int n=0;n<count;n++){float a=n*2.399963f;B3Particles.Emit(_embers,b.At,new Vector3(Mathf.Cos(a),1.5f,Mathf.Sin(a))*3f,.035f,.8f,new Color(1f,.48f,.24f,1f));}EmberBursts++;}}
                if(age>=.033f && (b.Stages&2)==0)
                {
                    b.Stages|=2;if(b.Rocket && (b.Mask&4)!=0)
                    {int count=b.Full?VfxTier.Pick(3,8,10):2;for(int n=0;n<count;n++)B3Particles.Emit(_column,b.At+Vector3.up*.2f,Vector3.up*(.4f+n*.05f),3f*b.Scale,1.2f,new Color(.56f,.58f,.60f,.45f));Columns++;}
                }
                if(age>=.05f && (b.Stages&4)==0)
                {
                    b.Stages|=4;
                    if(b.Full && b.Grounded && (b.Mask&4)!=0)
                    {
                        int count=b.Surface==SurfaceKind.Wood?6:b.Surface==SurfaceKind.Dirt?10:b.Surface==SurfaceKind.Foliage?30:12;
                        count=Mathf.Max(1,Mathf.RoundToInt(count*VfxTier.Pick(.4f,1f,1.3f)));
                        bool water=b.Surface==SurfaceKind.Water;
                        int cell=water?10:b.Surface==SurfaceKind.Wood?4:b.Surface==SurfaceKind.Dirt?8:0;
                        var sheet=_debris.textureSheetAnimation;sheet.startFrame=new ParticleSystem.MinMaxCurve(cell/16f,(cell+1.999f)/16f);
                        for(int n=0;n<count;n++){float a=n*2.399963f;Vector3 v=new Vector3(Mathf.Cos(a),1.2f+(n%3)*.3f,Mathf.Sin(a))*2f;B3Particles.Emit(b.Surface==SurfaceKind.Foliage?_leaves:_debris,b.Ground+b.Normal*.1f,v,water?.03f:.05f+(n%4)*.04f,water?1.5f:1.2f,water?new Color(.85f,.88f,.90f,.5f):new Color(.25f,.27f,.29f,1f));}
                        if(water){Card(VfxIds.WaterSplash,b.Ground,Quaternion.identity,FlipbookEffect.Facing.Camera,2.5f*b.Scale,1.5f,Color.white,.5f);Card(VfxIds.Shockwave,b.Ground+b.Normal*.05f,Quaternion.LookRotation(b.Normal),FlipbookEffect.Facing.Fixed,4f*b.Scale,.3f,new Color(.85f,.88f,.90f),.5f);}
                        if(b.Surface==SurfaceKind.Dirt && (b.Mask&8)!=0)Decal(b.Ground,b.Normal,CraterSize*b.Scale*b.GroundFade,ScorchSeconds,ScorchAlpha*b.GroundFade,new Color(.25f,.20f,.16f));   // §1.5 Dirt crater (scorch sheet), α ≤ ScorchAlpha (M-57)
                        Residues++;
                    }
                }
                if(age>=.2f && (b.Stages&8)==0)
                {
                    b.Stages|=8;if(b.Full && VfxTier.Current==VfxTierLevel.Cinematic && (b.Mask&4)!=0)
                    {for(int n=0;n<6;n++){float a=n*Mathf.PI/3f;B3Particles.Emit(_column,b.At+new Vector3(Mathf.Cos(a),.3f,Mathf.Sin(a))*.4f,Vector3.up*.3f,1f,1.2f,new Color(.82f,.84f,.87f,.25f));SteamPuffs++;}}
                }
                if(age>=.8f && (b.Stages&16)==0)
                {
                    b.Stages|=16;if(b.Full && b.Grounded && (b.Mask&4)!=0)
                    {var sheet=_debris.textureSheetAnimation;sheet.startFrame=new ParticleSystem.MinMaxCurve(10f/16f,11.999f/16f);for(int n=0;n<4;n++){float a=n*Mathf.PI*.5f;B3Particles.Emit(_debris,b.Ground+new Vector3(Mathf.Cos(a),.03f,Mathf.Sin(a))*.8f,Vector3.up*.4f,.04f,.15f,new Color(.85f,.88f,.9f,.5f));}}
                }
                if(age>=1.6f)b.Active=false;
            }
        }
        private bool Card(string id,Vector3 at,Quaternion rotation,FlipbookEffect.Facing facing,float size,float seconds,Color tint,float alpha,bool hold=false)
        {var fx=CardEffect(id,at,rotation,facing,size,seconds,tint,alpha);if(hold)fx?.HoldFrame(0);return fx!=null;}
        private FlipbookEffect CardEffect(string id,Vector3 at,Quaternion rotation,FlipbookEffect.Facing facing,float size,float seconds,Color tint,float alpha)
        {
            if(!VfxLibrary.TryGetPlayable(id,out VfxCatalogEntry row))return null;
            var fx=_owner.FlipbookPool?.Get(at,rotation);if(fx==null)return null;fx.Play(row,facing,size,tint,alpha,.25f,seconds);_owner.CountExplosionCard();return fx;
        }
        private bool Decal(Vector3 at,Vector3 normal,float size,float seconds,float alpha,Color tint)
        {
            if(!VfxLibrary.TryGetPlayable(VfxIds.DecalScorch,out VfxCatalogEntry row))return false;
            var d=_owner.DecalPool?.Get(at,Quaternion.LookRotation(normal,Mathf.Abs(Vector3.Dot(normal,Vector3.up))>.99f?Vector3.forward:Vector3.up));
            if(d==null)return false;d.Play(row,size,seconds,alpha,tint,17f);_owner.CountExplosionDecal();return true;
        }
    }
}
