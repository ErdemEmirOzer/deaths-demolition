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
        // M-57 (C-00 30.09): the scorch / crater life follows the impact-decal pattern per tier (VfxDressing.ImpactDecalSeconds):
        // Performance 8 / High 12 / Cinematic 20 s — C-00 decision "no quality cut", held only while tier-ms keeps Cinematic
        // VFX ≤ 5.5 ms with the decal pool (else back to 12 with the number recorded).
        [PrototypeOnly(PendingDecisions.None, "M-57 C-00 30.09: Cinematic scorch life 20 s (ImpactDecalSeconds pattern); conditional on tier-ms Cinematic VFX ≤ 5.5 ms, else 12.", true)]
        public const float ScorchSecondsCinematic = 20f;
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

        // ---- M-60 (C-00 30.09) · rl_1 backblast — direction §1.1 Launcher row (l.49): rear cone 2.5 m, smoke + water spray
        // #B8B0A6 α 0.45, 0.6 s; the cone darkens wet ground (decal 1.2 m / 4 s); rear light #FFA060 / 12 / 6 m.
        [PrototypeOnly(PendingDecisions.None, "M-60 direction §1.1 rl_1 row (l.49): backblast cone 2.5 m / 0.6 s / α 0.45; wet-ground decal 1.2 m / 4 s; rear light 12 / 6 m. Performance: 4 puffs + 3 drops, cone and life × 0.6 (§5 'a lower tier value, identity kept'); rear light off on Performance (2-light cap).", true)]
        public const float BackblastLength = 2.5f;
        public const float BackblastSeconds = .6f;
        public const float BackblastAlpha = .45f;
        public const float BackblastPerformanceScale = .6f;
        public const float BackblastConeSpread = .35f;
        public const float BackblastPuffStart = .5f;
        public const float BackblastPuffGrow = .9f;
        public const float BackblastDropSize = .03f;
        public const float BackblastDropLift = 1.2f;
        public const float BackblastDecalSize = 1.2f;
        public const float BackblastDecalSeconds = 4f;
        public const float BackblastGroundReach = 2.5f;
        public const float RearLightIntensity = 12f;
        public const float RearLightRange = 6f;
        public const float RearLightHold = 4f / 60f;
        public const float RearLightFade = .25f;
        // r4 frames (tp-rl_1-flash, 30.09): the rear light 0.3 m from the shoulder burnt the player's back white (§4.1-1 silhouette
        // rule). It sits RearLightBack metres behind the tube, scales by the player guard squared (TP ≈ 0.25), and is off in FP
        // (behind the eye it only lights the viewmodel).
        public const float RearLightBack = 1f;
        // WP-069 art table l.333: rl_1 tube 1.10 m; the Muzzle socket sits 0.125 m inside it (Wp073WeaponSocketTests finding, 26.09).
        [PrototypeOnly(PendingDecisions.None, "M-60: rl_1 tube 1.10 m (WP-069 l.333) minus the Muzzle socket's 0.125 m inset (Wp073WeaponSocketTests position finding) = the tube's rear behind the socket.", true)]
        public const float TubeRearFromMuzzle = 1.10f - .125f;
        private static readonly Color BackblastTint = new Color(.722f, .690f, .651f);   // #B8B0A6
        private static readonly Color RearLightColour = new Color(1f, .627f, .376f);    // #FFA060
        private static readonly Color WetDarkTint = new Color(.3f, .32f, .34f);         // the wet ring's darkening tint

        // ---- M-60 · near-blast screen-edge smear — direction §1.5 "Kamera / his" (l.105): < 4 m, 0.3 s, α 0.35, off under ReduceFlashes.
        [PrototypeOnly(PendingDecisions.None, "M-60 direction §1.5 l.105: near blast (< 4 m) 0.3 s screen-edge water/mud smear α 0.35, off under reduced flashes; AP-010 vignette_damage edge mask tinted mud-grey (tint is a stand-in).", true)]
        public const float EdgeSmearDistance = 4f;
        public const float EdgeSmearSeconds = .3f;
        public const float EdgeSmearAlpha = .35f;
        public const float EdgeSmearNearOffset = .02f;
        public const float EdgeSmearOverscan = 1.06f;
        private static readonly Color EdgeSmearTint = new Color(.42f, .39f, .35f);   // wet mud grey (stand-in)

        // ---- M-60 · §4.1 player guard (l.149): the column / fireball over the player's upper body on screen draws at α × 0.5 —
        // GelSystem.GuardFactor's camera → chest test (TP / top-down; FP has no body on screen), column Ø 3 m (§1.5).
        [PrototypeOnly(PendingDecisions.None, "M-60 §4.1: guard radius = the §1.5 column's final Ø 3 m / 2 × blast scale; chest 1.3 m (GelSystem's).", true)]
        public const float ColumnGuardRadius = 1.5f;
        public const float GuardChestHeight = 1.3f;
        /// <summary>Test seam (like <see cref="VfxTier.Override"/>): the guard's chest point instead of the primary player's (any camera mode). Null in play.</summary>
        public static Vector3? GuardChestOverride;

        private struct Blast { public bool Active, Full, Rocket, Grounded, BrushOwned; public Vector3 At, Ground, Normal; public SurfaceKind Surface; public float Start, Scale, GroundFade, Guard; public int Stages, Mask; }
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
        /// <summary>M-60: rl_1 backblast cones played / wet-ground darkening decals they laid.</summary>
        public int Backblasts { get; private set; }
        public int BackblastDecals { get; private set; }
        /// <summary>M-60: near-blast screen-edge smears started.</summary>
        public int EdgeSmears { get; private set; }
        public bool EdgeSmearVisible => _smear != null && _smear.enabled;
        /// <summary>M-60 §4.1: presentations whose fireball card / column / steam drew at α × 0.5 (player guard).</summary>
        public int GuardedColumns { get; private set; }
        private MeshRenderer _smear;
        private Camera _smearCamera;
        private Material _smearMaterial;
        private MaterialPropertyBlock _smearBlock;
        private float _smearStart = -1f;
        public int Pending { get { int n=0;for(int i=0;i<_blasts.Length;i++)if(_blasts[i].Active)n++;return n; } }
        public static ExplosionDirector Ensure(VfxDressing owner)
        {
            if(s_instance==null)s_instance=FindObjectOfType<ExplosionDirector>();
            if(s_instance==null){var go=new GameObject("[ExplosionDirector]");DontDestroyOnLoad(go);s_instance=go.AddComponent<ExplosionDirector>();}
            s_instance._owner=owner;return s_instance;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics(){s_instance=null;GuardChestOverride=null;}
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
            _smearBlock=new MaterialPropertyBlock();BuildSmear();   // M-60: warm (no GameObject in the first near blast's frame)
        }
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged-=OnSceneChanged;if(s_instance==this)s_instance=null;Destroy(_smokeMaterial);Destroy(_debrisMaterial);Destroy(_leafMaterial);Destroy(_emberMaterial);Destroy(_dot);
            if (_smear != null) Destroy(_smear.gameObject);
            if (_smearMaterial != null) Destroy(_smearMaterial);
        }
        private void OnSceneChanged(Scene a,Scene b)=>Clear();
        public void Clear(){for(int i=0;i<_blasts.Length;i++)_blasts[i]=default;_column?.Clear();_debris?.Clear();_leaves?.Clear();_embers?.Clear();HideSmear();}
        public bool Play(Vector3 at, float radius, int mask) => Play(at, radius, mask, ExplosionSource.Rocket);

        /// <summary>M-62: a rocket takes a Full slot (≤ FullCapacity) and the Cinematic shadowed light; a small blast plays card +
        /// ring + scorch, light ≤ LightSmall, no shadow, no embers / column / steam / residue / wet ring.</summary>
        public bool Play(Vector3 at, float radius, int mask, ExplosionSource source)
        {
            if (_owner == null || !_owner.IsReady || mask == 0) return false;
            bool rocket = source == ExplosionSource.Rocket;
            bool brushOwned = FeelBrushVfxSystem.OwnsTdLayers;
            // Reserve once before debris and the following FeelEvents ring/light are presented. M-62: a rocket is never the one
            // refused - with every slot held it takes over the oldest small blast's reservation.
            if (brushOwned && !FeelBrushVfxSystem.TryReserveExplosion(at, radius, rocket)) return true;
            if (!rocket) mask &= 1 | 2 | 8;
            int full = 0;
            for (int i = 0; i < _blasts.Length; i++) if (_blasts[i].Active && _blasts[i].Full) full++;
            ref Blast b = ref _blasts[_next];
            _next = (_next + 1) % _blasts.Length;
            b = new Blast { Active = true, Rocket = rocket, BrushOwned = brushOwned, Full = rocket && full < FullCapacity, At = at, Start = Time.time, Scale = Mathf.Clamp(radius / 5f, .3f, 1f), Mask = mask };
            b.Guard = GuardAt(at, ColumnGuardRadius * b.Scale);   // M-60 §4.1: fireball card + column + steam at α × 0.5 over the player
            if (b.Guard < 1f) GuardedColumns++;
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
                // C-00 10.10 (user note 4, "stronger juice"): top-down too - the brush ring and light alone left the rocket almost
                // invisible from the TD camera. The fireball card (small preset for a small blast) and a Full rocket's flash card
                // draw with the brush layers; the light stays the brush's, the near-viewer edge smear the layered path's.
                Card(VfxIds.Explosion, at, Quaternion.identity, FlipbookEffect.Facing.Camera, FireballSize * b.Scale * (b.Full ? 1f : FireballLightScale), FireballSeconds, Color.white, alpha * b.Guard);
                if (b.Full && alpha >= .99f && VfxTier.Current != VfxTierLevel.Performance
                    && Card(VfxIds.Explosion, at, Quaternion.identity, FlipbookEffect.Facing.Camera, FlashCardSize * b.Scale, 1f / 60f, new Color(1f, .75f, .45f) * 12f, 1f, true))
                    FlashCards++;
                if (!brushOwned)
                {
                    _lights.FlashAt(at, new Color(1f, .69f, .44f), b.Full ? LightFull : LightSmall, LightRange * b.Scale, LightHold, LightFade, false, b.Full && LightCastsShadows);
                    // M-60 §1.5 l.105: rocket and small blasts alike; ReduceFlashes (the FlashScale test above) turns it off.
                    if (alpha >= .99f && NearViewer(at) && PlayEdgeSmear()) EdgeSmears++;
                }
            }

            if (!brushOwned && b.Grounded && (mask & 2) != 0)
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

        private void Update(){Tick(Time.time);TickSmear(Time.time);}
        public void Tick(float now)
        {
            for(int i=0;i<_blasts.Length;i++)
            {
                ref Blast b=ref _blasts[i];if(!b.Active)continue;float age=now-b.Start;
                if (b.BrushOwned)
                {
                    // FeelEvents owns the new ring/light. Retain this prewarmed AP010 debris/smoke owner once - under the layered
                    // path's rules: the smoke column is a rocket's and the debris a grounded Full rocket's (M-62: a small blast -
                    // explosive rounds, bomblets, split rockets - keeps its brush ring, light and scorch; M-56: an air burst lays none).
                    if (age >= .033f && (b.Stages & 2) == 0)
                    {
                        b.Stages |= 2;
                        if (b.Rocket && (b.Mask & 4) != 0)
                        {
                            B3Particles.Emit(_column, b.At + Vector3.up * .2f, Vector3.up * .45f,
                                1.8f * b.Scale, 1.0f, new Color(.56f, .58f, .60f, .35f * b.Guard));
                            Columns++;
                        }
                    }
                    if (age >= .05f && (b.Stages & 4) == 0 && b.Full && b.Grounded && (b.Mask & 4) != 0)
                    {
                        b.Stages |= 4;
                        int count = VfxTier.Pick(6, 9, 12);
                        Vector3 origin = b.Grounded ? b.Ground + b.Normal * .1f : b.At;
                        var sheet = _debris.textureSheetAnimation;
                        int cell = b.Surface == SurfaceKind.Wood ? 4 : b.Surface == SurfaceKind.Dirt ? 8 : 0;
                        sheet.startFrame = new ParticleSystem.MinMaxCurve(cell / 16f, (cell + 1.999f) / 16f);
                        for (int n = 0; n < count; n++)
                        {
                            float angle = n * 2.399963f;
                            B3Particles.Emit(_debris, origin, new Vector3(Mathf.Cos(angle), 1.2f + (n % 3) * .3f, Mathf.Sin(angle)) * 2f,
                                .05f + (n % 4) * .04f, .8f + (n % 3) * .2f, new Color(.25f, .27f, .29f, 1f));
                        }
                        Residues++;
                    }
                    if (age >= FeelBrushVfxSystem.ExplosionReservationSeconds) b.Active = false;
                    continue;
                }
                if(age>=.016f && (b.Stages&1)==0)
                {b.Stages|=1;if(b.Full && (b.Mask&4)!=0){int count=VfxTier.Pick(10,24,32);for(int n=0;n<count;n++){float a=n*2.399963f;B3Particles.Emit(_embers,b.At,new Vector3(Mathf.Cos(a),1.5f,Mathf.Sin(a))*3f,.035f,.8f,new Color(1f,.48f,.24f,1f));}EmberBursts++;}}
                if(age>=.033f && (b.Stages&2)==0)
                {
                    b.Stages|=2;if(b.Rocket && (b.Mask&4)!=0)
                    {int count=b.Full?VfxTier.Pick(3,8,10):2;for(int n=0;n<count;n++)B3Particles.Emit(_column,b.At+Vector3.up*.2f,Vector3.up*(.4f+n*.05f),3f*b.Scale,1.2f,new Color(.56f,.58f,.60f,.45f*b.Guard));Columns++;}
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
                    {for(int n=0;n<6;n++){float a=n*Mathf.PI/3f;B3Particles.Emit(_column,b.At+new Vector3(Mathf.Cos(a),.3f,Mathf.Sin(a))*.4f,Vector3.up*.3f,1f,1.2f,new Color(.82f,.84f,.87f,.25f*b.Guard));SteamPuffs++;}}
                }
                if(age>=.8f && (b.Stages&16)==0)
                {
                    b.Stages|=16;if(b.Full && b.Grounded && (b.Mask&4)!=0)
                    {var sheet=_debris.textureSheetAnimation;sheet.startFrame=new ParticleSystem.MinMaxCurve(10f/16f,11.999f/16f);for(int n=0;n<4;n++){float a=n*Mathf.PI*.5f;B3Particles.Emit(_debris,b.Ground+new Vector3(Mathf.Cos(a),.03f,Mathf.Sin(a))*.8f,Vector3.up*.4f,.04f,.15f,new Color(.85f,.88f,.9f,.5f));}}
                }
                if(age>=1.6f)b.Active=false;
            }
        }
        // ------------------------------------------------------------------ M-60 · rl_1 backblast (direction §1.1 l.49)

        /// <summary>
        /// One rl_1 shot's rear cone from <paramref name="rear"/> (the tube's rear, world) along <paramref name="back"/>: smoke puffs
        /// on the column emitter + water-spray drops on the ember emitter (#B8B0A6, α 0.45, 0.6 s, 2.5 m); a 1.2 m / 4 s darkening
        /// decal where the cone meets ground within 2.5 m below its middle (never on water); the rear light #FFA060 / 12 / 6 m
        /// (not on Performance). Performance: 4 puffs + 3 drops, cone and life × 0.6. The §4.1 guard halves the cone's alpha when
        /// it sits between the camera and the player's chest (TP: the cone blows toward the camera). Called by VfxDressing.PlayMuzzle.
        /// </summary>
        public bool PlayBackblast(Vector3 rear, Vector3 back)
        {
            if (_owner == null || !_owner.IsReady || back.sqrMagnitude < 1e-6f) return false;
            back.Normalize();
            bool performance = VfxTier.Current == VfxTierLevel.Performance;
            float scale = performance ? BackblastPerformanceScale : 1f;
            float length = BackblastLength * scale;
            float seconds = BackblastSeconds * scale;
            int puffs = VfxTier.Pick(4, 8, 10);
            int drops = VfxTier.Pick(3, 6, 8);
            Vector3 right = Vector3.Cross(Vector3.up, back);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(back, right);
            float speed = length / seconds;
            float guard = GuardAt(rear + back * (length * .5f), length * .25f);
            Color smoke = new Color(BackblastTint.r, BackblastTint.g, BackblastTint.b, BackblastAlpha * guard);
            for (int n = 0; n < puffs; n++)
            {
                float k = (n + 1f) / puffs;
                float a = n * 2.399963f;
                Vector3 spread = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (BackblastConeSpread * k);
                B3Particles.Emit(_column, rear + back * (length * .1f * k), (back + spread) * speed * (.6f + .4f * k), (BackblastPuffStart + BackblastPuffGrow * k) * scale, seconds, smoke);
            }

            for (int n = 0; n < drops; n++)
            {
                float a = n * 2.399963f + 1f;
                Vector3 spread = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * BackblastConeSpread;
                B3Particles.Emit(_embers, rear, (back + spread) * speed + Vector3.up * BackblastDropLift, BackblastDropSize, seconds, smoke);
            }

            bool firstPerson = DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            if (!performance && !firstPerson)
                _lights.FlashAt(rear + back * RearLightBack, RearLightColour, RearLightIntensity * guard * guard, RearLightRange, RearLightHold, RearLightFade);
            int layerMask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            Vector3 probe = rear + back * (BackblastDecalSize * .5f);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, BackblastGroundReach, layerMask, QueryTriggerInteraction.Ignore)
                && SurfaceResolver.Resolve(ground.collider) != SurfaceKind.Water
                && Decal(ground.point, ground.normal, BackblastDecalSize, BackblastDecalSeconds, WetRingAlpha, WetDarkTint))
                BackblastDecals++;

            Backblasts++;
            return true;
        }

        // ------------------------------------------------------------------ M-60 · §4.1 player guard (l.149)

        /// <summary>GelSystem's guard (0.5 when <paramref name="p"/> sits between the camera and the player's chest, else 1); 1 in FP or without a camera / player.</summary>
        private static float GuardAt(Vector3 p, float radius)
        {
            if (!FeedbackCameraRef.TryGetPosition(out Vector3 cam)) return 1f;
            Vector3 chest;
            if (GuardChestOverride.HasValue) chest = GuardChestOverride.Value;
            else
            {
                if (DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson) return 1f;
                var player = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
                if (player == null) return 1f;
                chest = player.transform.position + Vector3.up * GuardChestHeight;
            }

            return GelSystem.GuardFactor(cam, chest, p, radius);
        }

        // ------------------------------------------------------------------ M-60 · near-blast screen-edge smear (§1.5 l.105)

        /// <summary>True when the blast is under <see cref="EdgeSmearDistance"/> from the camera or the player's chest.</summary>
        private static bool NearViewer(Vector3 at)
        {
            float reach = EdgeSmearDistance * EdgeSmearDistance;
            if (FeedbackCameraRef.TryGetPosition(out Vector3 cam) && (cam - at).sqrMagnitude < reach) return true;
            var player = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            return player != null && (player.transform.position + Vector3.up * GuardChestHeight - at).sqrMagnitude < reach;
        }

        /// <summary>
        /// One camera-parented quad just past the near plane, sized to overscan the frustum, drawing the AP-010 vignette_damage
        /// edge mask (alpha-only edge gradient) tinted mud-grey; α 0.35 → 0 over 0.3 s. VFX-owned (not the HUD canvas, S-09);
        /// a newer near blast restarts it. Rebuilt when its camera went away (it is destroyed with the camera it rides).
        /// </summary>
        private bool PlayEdgeSmear()
        {
            Camera cam = FeedbackCameraRef.Current;
            if (cam == null) return false;
            if (_smear == null && !BuildSmear()) return false;
            Transform t = _smear.transform;
            if (_smearCamera != cam)
            {
                t.SetParent(cam.transform, false);
                _smearCamera = cam;
            }

            float depth = cam.nearClipPlane + EdgeSmearNearOffset;
            float height = 2f * depth * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad) * EdgeSmearOverscan;
            t.localPosition = new Vector3(0f, 0f, depth);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(height * cam.aspect, height, 1f);
            _smearStart = Time.time;
            SetSmearAlpha(EdgeSmearAlpha);
            _smear.enabled = true;
            return true;
        }

        private bool BuildSmear()
        {
            VfxCatalogEntry row = VfxLibrary.Get(VfxIds.VignetteDamage);
            if (row == null || row.Texture == null) return false;
            if (_smearMaterial == null)
            {
                _smearMaterial = new Material(FeedbackMaterials.ParticleAlpha()) { name = "B3 near-blast edge smear", hideFlags = HideFlags.DontSave };
                _smearMaterial.mainTexture = row.Texture;
                _smearMaterial.SetTexture("_BaseMap", row.Texture);
            }

            var go = new GameObject("BlastEdgeSmear");
            go.transform.SetParent(transform, false);   // rides the (DontDestroyOnLoad) director until a near blast hands it to the camera
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            _smear = go.AddComponent<MeshRenderer>();
            _smear.sharedMaterial = _smearMaterial;
            _smear.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _smear.receiveShadows = false;
            _smear.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _smear.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _smear.enabled = false;
            _smearCamera = null;
            return true;
        }

        private void SetSmearAlpha(float alpha)
        {
            FeedbackMaterials.ApplyColor(_smearBlock, new Color(EdgeSmearTint.r, EdgeSmearTint.g, EdgeSmearTint.b, alpha));
            _smear.SetPropertyBlock(_smearBlock);
        }

        private void TickSmear(float now)
        {
            if (_smearStart < 0f || _smear == null) return;
            float k = (now - _smearStart) / EdgeSmearSeconds;
            if (k >= 1f || GameFeelSettings.FlashScale < .99f)
            {
                HideSmear();
                return;
            }

            SetSmearAlpha(EdgeSmearAlpha * (1f - k));
        }

        private void HideSmear()
        {
            _smearStart = -1f;
            if (_smear != null) _smear.enabled = false;
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
