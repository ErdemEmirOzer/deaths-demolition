using DD.Prototype.Core;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-073 B3: passive world presentation. Generation is the key; Get takes a live index.
    /// Greybox is deliberately temporary pending P-07's flying rocket. No colliders or gameplay RNG.</summary>
    public sealed class RocketVisualSystem : MonoBehaviour
    {
        private static RocketVisualSystem s_instance;
        private static readonly int WindId = Shader.PropertyToID("_DD_WindVector");
        public static RocketVisualSystem Instance => s_instance;
        public const float SmokeRate = 20f, RocketLength = 0.55f;
        private struct Flight { public int Id; public float Smoke, Ember; public Vector3 Last; }
        private readonly Flight[] _flights = new Flight[ProjectileSystem.Capacity];
        private readonly Matrix4x4[] _matrices = new Matrix4x4[ProjectileSystem.Capacity];
        private readonly bool[] _seen = new bool[ProjectileSystem.Capacity];
        private struct DudVisual { public Vector3 At, Direction; public float Started, Life; }
        private readonly DudVisual[] _duds = new DudVisual[8];
        private int _dudNext;
        private Mesh _mesh;
        private Material _meshMaterial, _smokeMaterial, _exhaustMaterial, _sparkMaterial;
        private Texture2D _dot;
        private ParticleSystem _smoke, _sparks;
        private readonly MeshRenderer[] _exhaustRenderers = new MeshRenderer[ProjectileSystem.Capacity];
        private readonly Transform[] _exhaustTransforms = new Transform[ProjectileSystem.Capacity];
        private readonly MaterialPropertyBlock[] _exhaustBlocks = new MaterialPropertyBlock[ProjectileSystem.Capacity];
        private static readonly int FrameId=Shader.PropertyToID("_Frame"), AlphaId=Shader.PropertyToID("_Alpha"), AlphaCapId=Shader.PropertyToID("_AlphaCap"), TintId=Shader.PropertyToID("_Tint"), EmissiveId=Shader.PropertyToID("_EmissiveStrength");
        private MuzzleSystem _lights;
        public int ActiveFlights { get; private set; }
        public int SmokeEmitted { get; private set; }
        public int DudsPlayed { get; private set; }
        public int DrawnRockets { get; private set; }
        public ParticleSystem Smoke => _smoke;
        public int ExhaustAlive { get; private set; }
        public float SmokeLife => VfxTier.Pick(1f, 2f, 3.5f);
        public static RocketVisualSystem Ensure()
        {
            if (s_instance == null) s_instance=FindObjectOfType<RocketVisualSystem>();
            if (s_instance == null) { var go = new GameObject("[RocketVisualSystem]"); DontDestroyOnLoad(go); s_instance = go.AddComponent<RocketVisualSystem>(); }
            s_instance.Rearm(); return s_instance;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()=>s_instance=null;
        private void Awake()
        {
            s_instance = this;
            _mesh = Greybox();
            _meshMaterial = new Material(FeedbackMaterials.Opaque(new Color(0.48f, 0.48f, 0.48f))) { name = "B3 rocket greybox", enableInstancing = true };
            _smokeMaterial = B3Particles.Material(VfxIds.CardSmoke);
            _smoke = B3Particles.Build(transform, "RocketSmoke", _smokeMaterial, 1024, 0f, true);
            var size = _smoke.sizeOverLifetime; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.15f / 0.9f, 1f, 1f));
            var colour = _smoke.colorOverLifetime;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(new Color(0.72f,0.69f,0.65f),0f), new GradientColorKey(new Color(0.56f,0.58f,0.60f),0.25f) }, new[] { new GradientAlphaKey(0.45f,0f), new GradientAlphaKey(0f,1f) }); colour.color = gradient;
            if (VfxLibrary.TryGetPlayable(VfxIds.Muzzle, out VfxCatalogEntry row))
            {
                _exhaustMaterial=new Material(row.Material) {name="B3 eight-cell attached exhaust",hideFlags=HideFlags.DontSave};
                for(int i=0;i<_exhaustRenderers.Length;i++)
                {
                    var go=new GameObject("RocketExhaustCard");go.transform.SetParent(transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=VfxLibrary.Quad;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_exhaustMaterial;renderer.enabled=false;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                    _exhaustRenderers[i]=renderer;_exhaustTransforms[i]=go.transform;_exhaustBlocks[i]=new MaterialPropertyBlock();
                    _exhaustBlocks[i].SetFloat(AlphaId,1f);Color tint=new Color(6f,4.5f,2.64f,1f);_exhaustBlocks[i].SetColor(TintId,tint);_exhaustBlocks[i].SetFloat(EmissiveId,1f);renderer.SetPropertyBlock(_exhaustBlocks[i]);
                }
            }
            _dot = B3Particles.Dot(); _sparkMaterial = B3Particles.Material(VfxIds.CardSmoke); _sparkMaterial.mainTexture=_dot; _sparkMaterial.SetTexture("_BaseMap",_dot);
            _sparks=B3Particles.Build(transform,"RocketEmbers",_sparkMaterial,128,1f,false);
            _lights = MuzzleSystem.Ensure();
            SceneManager.activeSceneChanged += OnSceneChanged; Rearm();
        }
        public void Rearm() { Disarm(); ProjectileEvents.Impact += OnImpact; ProjectileEvents.Expired += OnExpired; RocketImpacts.Dud += OnDud; }
        private void Disarm() { ProjectileEvents.Impact -= OnImpact; ProjectileEvents.Expired -= OnExpired; RocketImpacts.Dud -= OnDud; }
        private void OnSceneChanged(Scene a, Scene b) { Clear(); Rearm(); }
        private void OnDestroy()
        {
            Disarm(); SceneManager.activeSceneChanged -= OnSceneChanged; if (s_instance == this) s_instance = null;
            Destroy(_mesh); Destroy(_meshMaterial); Destroy(_smokeMaterial); Destroy(_exhaustMaterial); Destroy(_sparkMaterial); Destroy(_dot);
        }
        private void OnImpact(ProjectileImpactInfo p) { if (p.Stopped) Retire(p.ProjectileId); }
        private void OnExpired(ProjectileExpireInfo p) => Retire(p.ProjectileId);
        private void Retire(int id) { for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id == id) { _flights[i] = default; if(_exhaustRenderers[i]!=null)_exhaustRenderers[i].enabled=false; ActiveFlights--; return; } }
        private void OnDud(RocketDudInfo d)
        {
            _duds[_dudNext] = new DudVisual { At = d.Position, Direction = d.Direction, Started = Time.time, Life = Mathf.Max(0.01f,d.FizzleSeconds) };
            _dudNext = (_dudNext + 1) % _duds.Length; DudsPlayed++;
            for(int i=0;i<3;i++)B3Particles.Emit(_sparks,d.Position,Vector3.up*.3f,.03f,.3f,new Color(1f,.48f,.24f,1f));
        }
        public void Clear()
        {
            for (int i = 0; i < _flights.Length; i++) { _flights[i] = default; if(_exhaustRenderers[i]!=null)_exhaustRenderers[i].enabled=false; }
            for (int i = 0; i < _duds.Length; i++) _duds[i] = default;
            ActiveFlights = 0; ExhaustAlive=0; _smoke?.Clear(); _sparks?.Clear();
        }
        private void LateUpdate() => Present(Time.deltaTime);
        /// <summary>Same live-state reader used by runtime and tests. Does not advance the simulation.</summary>
        public void Present(float dt)
        {
            for (int i = 0; i < _seen.Length; i++) _seen[i] = false;
            int drawn = 0; ExhaustAlive=0;
            Vector3 wind = Shader.GetGlobalVector(WindId);
            for (int live = 0; live < ProjectileSystem.LiveCount; live++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(live);
                if (p.Kind != ProjectileKind.Rocket) continue;
                int slot = -1, free = -1;
                for (int j = 0; j < _flights.Length; j++) { if (_flights[j].Id == p.Generation) { slot = j; break; } if (free < 0 && _flights[j].Id == 0) free = j; }
                if (slot < 0) { slot = free; if (slot < 0) continue; _flights[slot] = new Flight { Id = p.Generation, Last = p.Position }; ActiveFlights++; }
                _seen[slot] = true; ref Flight f = ref _flights[slot];
                Vector3 dir = p.Direction.sqrMagnitude > 0.0001f ? p.Direction.normalized : Vector3.forward;
                Quaternion rotation = Quaternion.LookRotation(dir);
                // World-only after fire in all modes; no CameraView remapping of actual flight coordinates.
                _matrices[drawn++] = Matrix4x4.TRS(p.Position,rotation,Vector3.one);
                // Keep the accepted greybox mesh until a genuine P07 silhouette impostor exists.
                if(_exhaustRenderers[slot]!=null)
                {
                    Vector3 nozzle=p.Position-dir*(RocketLength+.225f);
                    Vector3 to=FeedbackCameraRef.TryGetPosition(out Vector3 viewer)?viewer-nozzle:-FeedbackCameraRef.Forward;
                    if(to.sqrMagnitude<.001f)to=Vector3.back;
                    Vector3 projected=Vector3.ProjectOnPlane(dir,to).normalized;
                    Vector3 up=projected.sqrMagnitude>.001f?Vector3.Cross(-to.normalized,projected):Vector3.up;
                    _exhaustTransforms[slot].SetPositionAndRotation(nozzle,Quaternion.LookRotation(-to.normalized,up));
                    _exhaustTransforms[slot].localScale=new Vector3(.45f,.12f,1f);
                    var block=_exhaustBlocks[slot];block.SetFloat(FrameId,Mathf.FloorToInt(p.Age*60f)%8);block.SetFloat(AlphaCapId,GameFeelSettings.FlashScale);_exhaustRenderers[slot].SetPropertyBlock(block);
                    _exhaustRenderers[slot].enabled=true;ExhaustAlive++;
                }
                if(dt<=0f) { f.Last=p.Position; continue; }
                f.Smoke += Mathf.Max(0f,dt) * SmokeRate;
                int count = Mathf.Min(8, Mathf.FloorToInt(f.Smoke)); f.Smoke -= count;
                for (int n = 0; n < count; n++)
                {
                    Vector3 at = Vector3.Lerp(f.Last,p.Position,(n + 1f)/count) - dir * RocketLength;
                    B3Particles.Emit(_smoke, at, wind + Vector3.up * 0.3f, 0.9f, SmokeLife, Color.white); SmokeEmitted++;
                }
                f.Last = p.Position;
                f.Ember += Mathf.Max(0f,dt) * 6f;
                if (f.Ember >= 1f) { f.Ember -= 1f; B3Particles.Emit(_sparks,p.Position-dir*RocketLength,-dir*.4f,.03f,.3f,new Color(1f,.48f,.24f,1f)); }
                if (VfxTier.Current != VfxTierLevel.Performance) _lights.TrackLight(p.Generation ^ 0x524c0000,p.Position-dir*0.55f,new Color(1f,0.69f,0.44f),8f,5f);
            }
            for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id != 0 && !_seen[i]) { _flights[i] = default; if(_exhaustRenderers[i]!=null)_exhaustRenderers[i].enabled=false; ActiveFlights--; }
            for (int i = 0; i < _duds.Length; i++)
            {
                ref DudVisual d = ref _duds[i]; if (d.Life <= 0f) continue;
                float age = Time.time-d.Started; if (age >= d.Life) { d = default; continue; }
                if (drawn < _matrices.Length) _matrices[drawn++] = Matrix4x4.TRS(d.At + Vector3.down * Mathf.Min(0.3f,0.5f*age*age),Quaternion.LookRotation(d.Direction.sqrMagnitude>0.001f?d.Direction:Vector3.forward),Vector3.one);
            }
            DrawnRockets = drawn;
            if (drawn > 0 && SystemInfo.supportsInstancing) Graphics.DrawMeshInstanced(_mesh,0,_meshMaterial,_matrices,drawn,null,ShadowCastingMode.Off,false,0,null,LightProbeUsage.Off);
            else for(int i=0;i<drawn;i++)Graphics.DrawMesh(_mesh,_matrices[i],_meshMaterial,0,null,0,null,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
        }
        public bool TryVisual(int id, out Vector3 at)
        {
            for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id == id) { at = _flights[i].Last; return true; }
            at = default; return false;
        }
        private static Mesh Greybox()
        {
            // 12-sided tapered body + nose, three triangular fins. 0.55m total, 42 triangles.
            var v = new Vector3[34]; var t = new int[162]; int k = 0;
            for (int i=0;i<12;i++) { float a=i*Mathf.PI/6f; v[i]=new Vector3(Mathf.Cos(a)*0.035f,Mathf.Sin(a)*0.035f,-0.55f); v[12+i]=new Vector3(Mathf.Cos(a)*0.035f,Mathf.Sin(a)*0.035f,-0.10f); }
            v[24]=Vector3.zero;
            for(int i=0;i<12;i++) { int n=(i+1)%12; t[k++]=i;t[k++]=n;t[k++]=12+i;t[k++]=n;t[k++]=12+n;t[k++]=12+i; t[k++]=12+i;t[k++]=12+n;t[k++]=24; }
            for(int i=0;i<3;i++) { float a=i*2*Mathf.PI/3; Vector3 r=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0); int b=25+i*3; v[b]=r*.035f+Vector3.back*.5f;v[b+1]=r*.10f+Vector3.back*.53f;v[b+2]=r*.035f+Vector3.back*.35f;t[k++]=b;t[k++]=b+1;t[k++]=b+2;t[k++]=b+2;t[k++]=b+1;t[k++]=b; }
            System.Array.Resize(ref t,k); var mesh=new Mesh {name="B3 temporary rocket greybox",hideFlags=HideFlags.DontSave}; mesh.vertices=v;mesh.triangles=t;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
    internal static class B3Particles
    {
        internal static Texture2D Dot()
        {
            var t=new Texture2D(32,32,TextureFormat.RGBA32,false);var pixels=new Color[1024];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++){float d=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01(1-d));}
            t.SetPixels(pixels);t.Apply();t.wrapMode=TextureWrapMode.Clamp;return t;
        }
        internal static Material Material(string id)
        {
            var m = new Material(FeedbackMaterials.ParticleAlpha()) {name="B3 "+id,hideFlags=HideFlags.DontSave};
            if(VfxLibrary.TryGetPlayable(id,out VfxCatalogEntry r)) {m.mainTexture=r.Texture;m.SetTexture("_BaseMap",r.Texture);} return m;
        }
        internal static ParticleSystem Build(Transform parent,string name,Material material,int capacity,float gravity,bool grow)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startSpeed=0;main.gravityModifier=gravity;main.maxParticles=capacity;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var colour=ps.colorOverLifetime;colour.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0f),new GradientColorKey(Color.white,1f)},new[]{new GradientAlphaKey(1f,0f),new GradientAlphaKey(0f,1f)});colour.color=g;
            if(grow){var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0f,.5f,1f,1f));}
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.maxParticleSize=.2f;ps.Play();return ps;
        }
        internal static void Emit(ParticleSystem ps,Vector3 at,Vector3 velocity,float size,float life,Color tint)
        {var e=new ParticleSystem.EmitParams {position=at,velocity=velocity,startSize=size,startLifetime=life,startColor=tint,applyShapeToPosition=false};ps.Emit(e,1);}
    }
}
