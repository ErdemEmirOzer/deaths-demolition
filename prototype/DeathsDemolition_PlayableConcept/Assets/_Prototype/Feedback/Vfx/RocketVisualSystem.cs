using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-073 B3: passive world presentation. Generation is the key; Get takes a live index.
    /// B3.2: the flying rocket is AP-021 rl_1's own <c>part_rocket</c> (P-07 v003, LOD1) from the classic weapon library,
    /// nose on the projectile point; the greybox stays as the fallback when the library/model/part is absent or its frame
    /// fails the checks. The seated rocket on the weapon is hidden while the tube is empty and shown again on reload.
    /// No colliders or gameplay RNG.</summary>
    public sealed class RocketVisualSystem : MonoBehaviour
    {
        private static RocketVisualSystem s_instance;
        private static readonly int WindId = Shader.PropertyToID("_DD_WindVector");
        public static RocketVisualSystem Instance => s_instance;
        public const float SmokeRate = 20f, RocketLength = 0.55f;
        /// <summary>AP-021 (P-07) rl_1 moving part and the LOD used in flight (LOD1 466 tris ≤ direction §1.5's 600; LOD0 is 1,750).</summary>
        public const string RocketPart = "part_rocket", FlightLod = "part_rocket_LOD1";
        private struct Flight { public int Id; public float Smoke, Ember; public Vector3 Last, CatchOffset; public float CatchAge0; }
        /// <summary>
        /// WP-087 R2 rocket first frame, option (b) (C-00 05.10, presentation only): the projectile system steps 1/120 s and a newborn
        /// rocket already flies the spawn frame (≈ 0.41 m at 60 fps, 0.83 m at 30), so it was first drawn ≈ 80 px ahead of the FP tube
        /// mouth. Its visual now starts at the spawn point (ProjectileState.Origin, the muzzle) and eases onto the simulated position
        /// over <see cref="CatchUpSeconds"/> of the rocket's own age — two frames at 60 fps, one at 30. The simulation, the hits and
        /// their timing are untouched; only what is drawn (mesh, far card, exhaust, smoke, embers, light) rides the visual point, and
        /// the trail (exhaust card, smoke, embers, light) never starts behind the mouth. A rocket first seen older than <see cref="NewbornSeconds"/> (no free slot at its
        /// birth) is drawn where it is.
        /// </summary>
        public const float CatchUpSeconds = 1f / 30f, NewbornSeconds = 0.05f;

        /// <summary>The share of the spawn-frame lag still drawn <paramref name="sinceFirstSight"/> seconds (rocket age) after the first frame it was drawn: 1 → 0, eased.</summary>
        public static float CatchUpWeight(float sinceFirstSight) =>
            sinceFirstSight >= CatchUpSeconds - 1e-5f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, sinceFirstSight) / CatchUpSeconds);   // ε: ages are sums of steps
        private readonly Flight[] _flights = new Flight[ProjectileSystem.Capacity];
        private readonly Matrix4x4[] _matrices = new Matrix4x4[ProjectileSystem.Capacity];
        private readonly bool[] _seen = new bool[ProjectileSystem.Capacity];
        private struct DudVisual { public Vector3 At, Direction; public float Started, Life; }
        private readonly DudVisual[] _duds = new DudVisual[8];
        private int _dudNext;
        private Mesh _mesh, _greyMesh;
        private Material[] _meshMaterials;
        private Matrix4x4 _meshLocal = Matrix4x4.identity;
        private bool _instanced;
        private Material _greyMaterial, _smokeMaterial, _exhaustMaterial, _sparkMaterial;
        private Transform _seatMuzzle;
        private readonly List<Renderer> _seatRenderers = new List<Renderer>(4);
        private bool _seatHidden, _seatReloaded;
        private Texture2D _dot;
        private ParticleSystem _smoke, _sparks;
        // M-58 (C-00 30.09): eight exhaust cards, not one per projectile slot (256 GameObjects in Awake). Live flights take the
        // lowest free flight slots, so flight slot i < ExhaustCapacity owns card i; a ninth concurrent rocket flies without one.
        public const int ExhaustCapacity = 8;
        private readonly MeshRenderer[] _exhaustRenderers = new MeshRenderer[ExhaustCapacity];
        private readonly Transform[] _exhaustTransforms = new Transform[ExhaustCapacity];
        private readonly MaterialPropertyBlock[] _exhaustBlocks = new MaterialPropertyBlock[ExhaustCapacity];
        // M-60 (C-00 30.09) · direction §5 "Projectile mesh" (l.173) + §1.5 (l.99): Cinematic draws the mesh at every distance;
        // High ≤ 25 m and Performance ≤ 15 m from the feedback camera, beyond that a camera-facing card along the flight line
        // (the ember dot material, tinted between the iron ogive #4A4C50 and the paper body #8C7A66). Flight slot i < 8 owns
        // card i (the exhaust's rule); a ninth far rocket keeps the mesh. No camera → mesh.
        [PrototypeOnly(PendingDecisions.None, "M-60 direction §5 l.173: projectile mesh Cinematic every distance / High ≤ 25 m / Performance ≤ 15 m, then billboard; card width 0.12 m (the exhaust's) and tint are stand-ins.", true)]
        public const float BillboardBeyondHigh = 25f;
        public const float BillboardBeyondPerformance = 15f;
        public const float BillboardWidth = .12f;
        public static float BillboardBeyond => VfxTier.Pick(BillboardBeyondPerformance, BillboardBeyondHigh, float.PositiveInfinity);
        private static readonly Color BillboardTint = new Color(.42f, .40f, .37f, 1f);
        private readonly MeshRenderer[] _billboardRenderers = new MeshRenderer[ExhaustCapacity];
        private readonly Transform[] _billboardTransforms = new Transform[ExhaustCapacity];
        private MaterialPropertyBlock _billboardBlock;
        /// <summary>M-60: rockets drawn as a far billboard card this frame (not counted in <see cref="DrawnRockets"/>).</summary>
        public int BillboardRockets { get; private set; }
        private static readonly int FrameId=Shader.PropertyToID("_Frame"), AlphaId=Shader.PropertyToID("_Alpha"), AlphaCapId=Shader.PropertyToID("_AlphaCap"), TintId=Shader.PropertyToID("_Tint"), EmissiveId=Shader.PropertyToID("_EmissiveStrength");
        private MuzzleSystem _lights;
        public int ActiveFlights { get; private set; }
        public int SmokeEmitted { get; private set; }
        public int DudsPlayed { get; private set; }
        public int DrawnRockets { get; private set; }
        /// <summary>True when the flight mesh is AP-021's part_rocket (false = greybox fallback).</summary>
        public bool UsesAuthoredRocket { get; private set; }
        /// <summary>Nose-to-tail length of the drawn rocket (metres): the smoke, sparks, exhaust and light sit behind it.</summary>
        public float BodyLength { get; private set; } = RocketLength;
        public int FlightTriangles { get; private set; }
        /// <summary>The weapon's seated part_rocket is hidden (tube empty) / how many of its renderers are tracked.</summary>
        public bool SeatedRocketHidden => _seatHidden;
        public int SeatedRocketRenderers => _seatRenderers.Count;
        public ParticleSystem Smoke => _smoke;
        public ParticleSystem Sparks => _sparks;
        public int ExhaustAlive { get; private set; }
        public int ExhaustPoolSize => _exhaustRenderers.Length;
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
            UseFlightMesh();
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
            _billboardBlock=new MaterialPropertyBlock();FeedbackMaterials.ApplyColor(_billboardBlock,BillboardTint);
            for(int i=0;i<_billboardRenderers.Length;i++)
            {
                var go=new GameObject("RocketBillboardCard");go.transform.SetParent(transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=VfxLibrary.Quad;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_sparkMaterial;renderer.enabled=false;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;
                renderer.SetPropertyBlock(_billboardBlock);
                _billboardRenderers[i]=renderer;_billboardTransforms[i]=go.transform;
            }
            _lights = MuzzleSystem.Ensure();
            SceneManager.activeSceneChanged += OnSceneChanged; Rearm();
        }
        public void Rearm()
        {
            Disarm(); ProjectileEvents.Impact += OnImpact; ProjectileEvents.Expired += OnExpired; RocketImpacts.Dud += OnDud;
            ProjectileEvents.Spawned += OnSpawned; WeaponEvents.Mechanism += OnMechanism; WeaponEvents.ReloadInterrupted += OnReloadInterrupted;
        }
        private void Disarm()
        {
            ProjectileEvents.Impact -= OnImpact; ProjectileEvents.Expired -= OnExpired; RocketImpacts.Dud -= OnDud;
            ProjectileEvents.Spawned -= OnSpawned; WeaponEvents.Mechanism -= OnMechanism; WeaponEvents.ReloadInterrupted -= OnReloadInterrupted;
        }
        private void OnSceneChanged(Scene a, Scene b) { Clear(); Rearm(); }
        private void OnDestroy()
        {
            Disarm(); SceneManager.activeSceneChanged -= OnSceneChanged; if (s_instance == this) s_instance = null; ReleaseSeat();
            // The authored mesh / material are library assets (never destroyed); only the greybox pair is ours.
            if (_greyMesh != null) Destroy(_greyMesh); if (_greyMaterial != null) Destroy(_greyMaterial);
            Destroy(_smokeMaterial); Destroy(_exhaustMaterial); Destroy(_sparkMaterial); Destroy(_dot);
        }
        private void OnImpact(ProjectileImpactInfo p) { if (p.Stopped) Retire(p.ProjectileId); }
        private void OnExpired(ProjectileExpireInfo p) => Retire(p.ProjectileId);
        private void Retire(int id) { for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id == id) { _flights[i] = default; HideExhaust(i); ActiveFlights--; return; } }
        private void OnDud(RocketDudInfo d)
        {
            _duds[_dudNext] = new DudVisual { At = d.Position, Direction = d.Direction, Started = Time.time, Life = Mathf.Max(0.01f,d.FizzleSeconds) };
            _dudNext = (_dudNext + 1) % _duds.Length; DudsPlayed++;
            for(int i=0;i<3;i++)B3Particles.Emit(_sparks,d.Position,Vector3.up*.3f,.03f,.3f,new Color(1f,.48f,.24f,1f));
        }
        private void HideExhaust(int slot) { if (slot < _exhaustRenderers.Length && _exhaustRenderers[slot] != null) _exhaustRenderers[slot].enabled = false; HideBillboard(slot); }
        private void HideBillboard(int slot) { if (slot < _billboardRenderers.Length && _billboardRenderers[slot] != null) _billboardRenderers[slot].enabled = false; }
        /// <summary>M-60: the far card — centred on the body, its +x along the flight line projected on the view plane (the exhaust's facing).</summary>
        private void PlaceBillboard(int slot, Vector3 centre, Vector3 dir, Vector3 eye)
        {
            Vector3 to=eye-centre;if(to.sqrMagnitude<.001f)to=Vector3.back;
            Vector3 projected=Vector3.ProjectOnPlane(dir,to).normalized;
            Vector3 up=projected.sqrMagnitude>.001f?Vector3.Cross(-to.normalized,projected):Vector3.up;
            _billboardTransforms[slot].SetPositionAndRotation(centre,Quaternion.LookRotation(-to.normalized,up));
            _billboardTransforms[slot].localScale=new Vector3(BodyLength,BillboardWidth,1f);
            _billboardRenderers[slot].enabled=true;
        }
        public void Clear()
        {
            for (int i = 0; i < _flights.Length; i++) { _flights[i] = default; HideExhaust(i); }
            for (int i = 0; i < _duds.Length; i++) _duds[i] = default;
            ActiveFlights = 0; ExhaustAlive=0; _smoke?.Clear(); _sparks?.Clear(); ReleaseSeat();
        }
        private void LateUpdate() { SyncSeatedRocket(); Present(Time.deltaTime); }
        /// <summary>Same live-state reader used by runtime and tests. Does not advance the simulation.</summary>
        public void Present(float dt)
        {
            for (int i = 0; i < _seen.Length; i++) _seen[i] = false;
            int drawn = 0; ExhaustAlive=0; BillboardRockets=0;
            Vector3 wind = Shader.GetGlobalVector(WindId);
            bool haveEye = FeedbackCameraRef.TryGetPosition(out Vector3 eye);
            float beyond = BillboardBeyond, beyondSq = beyond * beyond;
            for (int live = 0; live < ProjectileSystem.LiveCount; live++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(live);
                if (p.Kind != ProjectileKind.Rocket) continue;
                int slot = -1, free = -1;
                for (int j = 0; j < _flights.Length; j++) { if (_flights[j].Id == p.Generation) { slot = j; break; } if (free < 0 && _flights[j].Id == 0) free = j; }
                if (slot < 0)
                {
                    slot = free; if (slot < 0) continue;
                    Vector3 lag = p.Age <= NewbornSeconds ? p.Origin - p.Position : Vector3.zero;   // WP-087 (b): drawn from the mouth
                    _flights[slot] = new Flight { Id = p.Generation, Last = p.Position + lag, CatchOffset = lag, CatchAge0 = p.Age }; ActiveFlights++;
                }
                _seen[slot] = true; ref Flight f = ref _flights[slot];
                Vector3 dir = p.Direction.sqrMagnitude > 0.0001f ? p.Direction.normalized : Vector3.forward;
                Quaternion rotation = Quaternion.LookRotation(dir);
                // WP-087 (b): the drawn point — the simulated one plus what is left of the spawn-frame lag
                Vector3 at = p.Position;
                if (f.CatchOffset != Vector3.zero)
                {
                    float w = CatchUpWeight(p.Age - f.CatchAge0);
                    if (w <= 0f) f.CatchOffset = Vector3.zero; else at += f.CatchOffset * w;
                }
                // World-only after fire in all modes; no CameraView remapping of actual flight coordinates.
                bool far = haveEye && (at - eye).sqrMagnitude > beyondSq && slot < _billboardRenderers.Length && _billboardRenderers[slot] != null;
                if (far) { PlaceBillboard(slot, at - dir * (BodyLength * .5f), dir, eye); BillboardRockets++; }
                else { HideBillboard(slot); _matrices[drawn++] = Matrix4x4.TRS(at,rotation,Vector3.one) * _meshLocal; }   // nose on the drawn point
                Vector3 tail = OutOfTube(at - dir * BodyLength, p.Origin, dir);
                if(slot<_exhaustRenderers.Length && _exhaustRenderers[slot]!=null)
                {
                    Vector3 nozzle=OutOfTube(at-dir*(BodyLength+.225f), p.Origin, dir);   // WP-087 (b): the flame at the mouth until the nozzle is out
                    Vector3 to=FeedbackCameraRef.TryGetPosition(out Vector3 viewer)?viewer-nozzle:-FeedbackCameraRef.Forward;
                    if(to.sqrMagnitude<.001f)to=Vector3.back;
                    Vector3 projected=Vector3.ProjectOnPlane(dir,to).normalized;
                    Vector3 up=projected.sqrMagnitude>.001f?Vector3.Cross(-to.normalized,projected):Vector3.up;
                    _exhaustTransforms[slot].SetPositionAndRotation(nozzle,Quaternion.LookRotation(-to.normalized,up));
                    _exhaustTransforms[slot].localScale=new Vector3(.45f,.12f,1f);
                    var block=_exhaustBlocks[slot];block.SetFloat(FrameId,Mathf.FloorToInt(p.Age*60f)%8);block.SetFloat(AlphaCapId,GameFeelSettings.FlashScale);_exhaustRenderers[slot].SetPropertyBlock(block);
                    _exhaustRenderers[slot].enabled=true;ExhaustAlive++;
                }
                if(dt<=0f) { f.Last=at; continue; }
                f.Smoke += Mathf.Max(0f,dt) * SmokeRate;
                int count = Mathf.Min(8, Mathf.FloorToInt(f.Smoke)); f.Smoke -= count;
                for (int n = 0; n < count; n++)
                {
                    Vector3 puff = OutOfTube(Vector3.Lerp(f.Last,at,(n + 1f)/count) - dir * BodyLength, p.Origin, dir);
                    B3Particles.Emit(_smoke, puff, wind + Vector3.up * 0.3f, 0.9f, SmokeLife, Color.white); SmokeEmitted++;
                }
                f.Last = at;
                f.Ember += Mathf.Max(0f,dt) * 6f;
                if (f.Ember >= 1f) { f.Ember -= 1f; B3Particles.Emit(_sparks,tail,-dir*.4f,.03f,.3f,new Color(1f,.48f,.24f,1f)); }
                if (VfxTier.Current != VfxTierLevel.Performance) _lights.TrackLight(p.Generation ^ 0x524c0000,tail,new Color(1f,0.69f,0.44f),8f,5f);
            }
            for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id != 0 && !_seen[i]) { _flights[i] = default; HideExhaust(i); ActiveFlights--; }
            for (int i = 0; i < _duds.Length; i++)
            {
                ref DudVisual d = ref _duds[i]; if (d.Life <= 0f) continue;
                float age = Time.time-d.Started; if (age >= d.Life) { d = default; continue; }
                if (drawn < _matrices.Length) _matrices[drawn++] = Matrix4x4.TRS(d.At + Vector3.down * Mathf.Min(0.3f,0.5f*age*age),Quaternion.LookRotation(d.Direction.sqrMagnitude>0.001f?d.Direction:Vector3.forward),Vector3.one) * _meshLocal;
            }
            DrawnRockets = drawn;
            if (drawn > 0)
                for (int s = 0; s < _mesh.subMeshCount; s++)
                {
                    Material m = _meshMaterials[s < _meshMaterials.Length ? s : _meshMaterials.Length - 1];
                    if (_instanced) Graphics.DrawMeshInstanced(_mesh,s,m,_matrices,drawn,null,ShadowCastingMode.Off,false,0,null,LightProbeUsage.Off);
                    else for(int i=0;i<drawn;i++)Graphics.DrawMesh(_mesh,_matrices[i],m,0,null,s,null,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
                }
        }
        /// <summary>WP-087 (b): a trail point behind the launch point (inside the tube, the first frames) sits on the mouth instead.</summary>
        private static Vector3 OutOfTube(Vector3 point, Vector3 origin, Vector3 dir) => Vector3.Dot(point - origin, dir) < 0f ? origin : point;

        /// <summary>The rocket's drawn nose point (the simulated position plus the first-frame catch-up, WP-087), as of the last Present.</summary>
        public bool TryVisual(int id, out Vector3 at)
        {
            for (int i = 0; i < _flights.Length; i++) if (_flights[i].Id == id) { at = _flights[i].Last; return true; }
            at = default; return false;
        }
        // ------------------------------------------------------------------ B3.2 · flight mesh (AP-021 part_rocket, greybox fallback)

        private void UseFlightMesh()
        {
            var entry = ClassicWeaponVisualLibrary.Current != null ? ClassicWeaponVisualLibrary.Current.Find(ClassicWeaponId.Rl1) : null;
            if (entry != null && TryAuthoredRocket(entry.model, out Mesh mesh, out Material[] materials, out Matrix4x4 local, out float length))
            {
                _mesh = mesh; _meshMaterials = materials; _meshLocal = local; BodyLength = length; UsesAuthoredRocket = true;
            }
            else
            {
                _greyMesh = Greybox();
                _greyMaterial = new Material(FeedbackMaterials.Opaque(new Color(0.48f, 0.48f, 0.48f))) { name = "B3 rocket greybox", enableInstancing = true };
                _mesh = _greyMesh; _meshMaterials = new[] { _greyMaterial }; _meshLocal = Matrix4x4.identity; BodyLength = RocketLength; UsesAuthoredRocket = false;
            }

            _instanced = SystemInfo.supportsInstancing;
            for (int i = 0; i < _meshMaterials.Length; i++) _instanced &= _meshMaterials[i].enableInstancing;
            int tris = 0; for (int s = 0; s < _mesh.subMeshCount; s++) tris += (int)(_mesh.GetIndexCount(s) / 3);
            FlightTriangles = tris;
        }

        /// <summary>
        /// Reads <see cref="FlightLod"/> under <paramref name="model"/> (the rl_1 FBX root in the ADR-042-1 frame: +Z barrel, +Y up) and
        /// returns its shared mesh / materials with a matrix that puts the nose (forward-most point) on the origin, centred on the Z axis.
        /// False — caller keeps the greybox — when the part is missing, or the frame check fails: the long axis must be Z, 0.3–1.0 m,
        /// and the nose must be the end toward the Muzzle socket (a model imported backwards or rotated never flies tail-first).
        /// </summary>
        public static bool TryAuthoredRocket(GameObject model, out Mesh mesh, out Material[] materials, out Matrix4x4 local, out float length)
        {
            mesh = null; materials = null; local = Matrix4x4.identity; length = 0f;
            if (model == null) return false;
            Transform lod = WeaponSocketContract.Find(model.transform, FlightLod);
            Transform muzzle = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Muzzle);
            MeshFilter filter = lod != null ? lod.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = lod != null ? lod.GetComponent<MeshRenderer>() : null;
            if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null || muzzle == null) return false;

            Matrix4x4 toRoot = model.transform.worldToLocalMatrix * lod.localToWorldMatrix;
            Bounds b = filter.sharedMesh.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }

            float len = max.z - min.z, width = Mathf.Max(max.x - min.x, max.y - min.y);
            float muzzleZ = model.transform.InverseTransformPoint(muzzle.position).z;
            if (len < 0.3f || len > 1f || width > 0.25f * len || muzzleZ <= (min.z + max.z) * 0.5f) return false;

            mesh = filter.sharedMesh; materials = renderer.sharedMaterials; length = len;
            local = Matrix4x4.Translate(new Vector3(-(min.x + max.x) * 0.5f, -(min.y + max.y) * 0.5f, -max.z)) * toRoot;
            return true;
        }

        // ------------------------------------------------------------------ B3.2 · seated rocket on the weapon (C-00 30.09)

        // The weapon's part_rocket is the round in the tube: hidden once the tube is empty (the flying one is drawn above), shown
        // again when the reload seats a rocket (rl_1 mechanism ShellSeated — clip mag_in / rocket seat or the controller's timer
        // fallback) or the magazine is refilled. Reads WeaponController's public surface only; renderers are found once per model.
        private void SyncSeatedRocket()
        {
            WeaponController c = WeaponRegistry.ActiveController;
            bool rl = c != null && !c.SpecialWeaponActive && c.Classic.Id == ClassicWeaponId.Rl1
                      && c.ClassicModelSource == WeaponController.ClassicModelKind.Authored;
            Transform muzzle = rl ? c.Muzzle : null;
            if (muzzle != _seatMuzzle || (muzzle == null && _seatRenderers.Count > 0))
            {
                ReleaseSeat();
                _seatMuzzle = muzzle;
                if (muzzle != null) FindSeat(muzzle);
            }

            if (muzzle == null || _seatRenderers.Count == 0) return;
            SetSeatHidden(c.Classic.Ammo <= 0 && !_seatReloaded);
        }

        private void FindSeat(Transform muzzle)
        {
            Transform t = muzzle.parent;
            for (int up = 0; up < 3 && t != null; up++, t = t.parent)
            {
                Transform part = WeaponSocketContract.Find(t, RocketPart);
                if (part == null) continue;
                part.GetComponentsInChildren(true, _seatRenderers);
                return;
            }
        }

        private void SetSeatHidden(bool hidden)
        {
            if (hidden == _seatHidden) return;
            _seatHidden = hidden;
            for (int i = 0; i < _seatRenderers.Count; i++) if (_seatRenderers[i] != null) _seatRenderers[i].forceRenderingOff = hidden;
        }

        private void ReleaseSeat()
        {
            SetSeatHidden(false);
            _seatRenderers.Clear(); _seatMuzzle = null; _seatReloaded = false;
        }

        private void OnSpawned(ProjectileSpawnInfo p) { if (p.Kind == ProjectileKind.Rocket && p.WeaponId == ClassicWeaponId.Rl1) _seatReloaded = false; }
        private void OnMechanism(WeaponMechanismEvent e) { if (e.Weapon == ClassicWeaponId.Rl1 && e.Kind == WeaponMechanismKind.ShellSeated) _seatReloaded = true; }
        private void OnReloadInterrupted() => _seatReloaded = false;

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
