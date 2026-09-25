using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · The two Special models' emissive states (direction §2.1 / §2.2, WP-069 §8, AP-021 v002 manifest via
    /// S-02 B4c — "S-07 drives the per-state colour / intensity"). Reads the weapon in hand every frame (no event needed
    /// except Fired, which marks the flash) and writes one MaterialPropertyBlock per (renderer, material slot):
    ///
    /// Raiju (M_AP021_sw_arc_glow on part_cell_1..3, body emission on M_AP021_sw_arc_body):
    ///   · cells = ammo: cell i is lit while ammo &gt; i × magazine / 3 — amber #FFC65A × 2 (the HUD Gold400 family, "a full
    ///     Special"); an empty cell is dark (× 0);
    ///   · charge (0.25 s): the coils' flicker ramps 1 → 6 Hz and brightens; the last 80 ms the lit cells turn
    ///     white-cyan #EAF6FF × 3;
    ///   · fire: 0.08 s white-cyan × 4 on the lit cells, the coil × 3 decaying over 0.25 s;
    ///   · reload (cell_out / cell_in): cells dark; the new cassette warms 0 → × 2 amber over the reload's last 0.4 s;
    ///   · idle: coil 1 Hz weak flicker (0.25 ± 0.1 of the emission map).
    /// Namekuji (M_AP021_sw_gel_glow = core specks in part_canister_gel, M_AP021_sw_gel_gel = the liquid / rim, body
    /// emission = nozzle) with the P-07 state table core / rim / nozzle: idle 0.6 / 0.5 / 0.3 · charged (ready to fire)
    /// 1.5 / 1.0 / 1.0 · fire 4.5 / 3.0 / 3.0 (0.2 s, then back to charged over 0.25 s) · empty 0.15 / 0.12 / 0.
    /// The liquid: when the library has DD/VFX Gel, the gel slot is swapped (on this rig instance only, restored on
    /// unbind) for a runtime canister material — a world-height cut plane at the level (ammo / magazine in 8 steps; the
    /// canister change drains it and refills over the last 0.3 s), sloshing at 0.5 Hz (× 2 amplitude for 0.4 s after a
    /// shot or a refill). Without it the Lit gel keeps its look and only its emission is driven.
    ///
    /// Binding walks the rig once per model instance (equip / activation — not a hot path); the per-frame path is
    /// fixed arrays + one block, 0 B. Classification assumptions (see b6-plan.md risk 2): renderers are matched by the
    /// AP-021 material names; Raiju cell index = the nearest ancestor named part_cell_N.
    /// </summary>
    [DefaultExecutionOrder(957)]
    public sealed class SpecialWeaponGlow : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.SpecialGlow");

        public const int MaxBinds = 64;
        public const string ArcGlow = "M_AP021_sw_arc_glow", ArcBody = "M_AP021_sw_arc_body";
        public const string GelGlow = "M_AP021_sw_gel_glow", GelLiquid = "M_AP021_sw_gel_gel", GelBody = "M_AP021_sw_gel_body";
        public const float FireFlashSeconds = 0.08f, CellWarmSeconds = 0.4f, CanisterFillSeconds = 0.3f;
        public const int LevelSteps = 8;

        private static readonly Vector4 Amber = SpecialVfxAssets.Hdr(0xFFC65A, 1f);
        private static readonly Vector4 WhiteCyan = SpecialVfxAssets.Hdr(0xEAF6FF, 1f);
        private static readonly Vector4 GelCore = SpecialVfxAssets.Hdr(0xFF5FD2, 1f);
        private static readonly Vector4 GelRim = SpecialVfxAssets.Hdr(0xB040FF, 1f);

        public enum Part : byte { None, Cell1, Cell2, Cell3, ArcOther, ArcBodyEmission, GelCoreSpecks, GelLiquidRim, GelNozzle }

        public enum GelState { Idle, Charged, Fire, Empty }

        private struct Bind
        {
            public Renderer R;
            public int Slot;
            public Part Part;
        }

        private static SpecialWeaponGlow s_instance;
        private static readonly List<Renderer> s_renderers = new List<Renderer>(32);
        private static readonly List<Material> s_materials = new List<Material>(8);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), GelColorId = Shader.PropertyToID("_GelColor"),
            GelRimId = Shader.PropertyToID("_GelRim"), GelWobbleId = Shader.PropertyToID("_GelWobble"), GelPlaneId = Shader.PropertyToID("_GelPlane");

        private readonly Bind[] _binds = new Bind[MaxBinds];
        private int _count;
        private int _boundModel;
        private SpecialWeaponId _boundId;
        private MaterialPropertyBlock _block;
        private float _firedAt = -10f;
        private float _refilledAt = -10f;
        private bool _wasReloading;

        // Canister swap (Namekuji).
        private Material _canister;
        private Renderer _liquidRenderer;
        private Material[] _liquidOriginal;
        private Material[] _liquidSwapped;

        public static SpecialWeaponGlow Instance => s_instance;
        public int BoundRenderers => _count;
        public bool CanisterSwapped => _liquidRenderer != null;
        public float LastCell0, LastCell1, LastCell2, LastCoil;
        public Vector3 LastGel;           // core, rim, nozzle
        public float LastLevel01;
        public GelState LastGelState;

        // ------------------------------------------------------------------ pure rules (tests)

        /// <summary>The P-07 state table (core, rim, nozzle).</summary>
        public static Vector3 GelIntensities(GelState s)
        {
            switch (s)
            {
                case GelState.Charged: return new Vector3(1.5f, 1.0f, 1.0f);
                case GelState.Fire: return new Vector3(4.5f, 3.0f, 3.0f);
                case GelState.Empty: return new Vector3(0.15f, 0.12f, 0f);
                default: return new Vector3(0.6f, 0.5f, 0.3f);
            }
        }

        /// <summary>
        /// State interpretation (b6-plan.md assumption): Empty = no round in the canister (reloading or out); Fire = 0.2 s
        /// after a shot; Charged = ready to fire (cycle over, a round in); Idle = cycling between shots.
        /// </summary>
        public static GelState GelStateOf(int ammo, bool canFire, bool reloading, float sinceFired)
        {
            if (ammo <= 0 || reloading) return GelState.Empty;
            if (sinceFired >= 0f && sinceFired < 0.2f) return GelState.Fire;
            return canFire ? GelState.Charged : GelState.Idle;
        }

        /// <summary>Raiju cell <paramref name="cell"/> (0..2) amber multiplier: × 2 while it holds its third of the magazine.</summary>
        public static float RaijuCell(int cell, int ammo, int magazine, bool reloading, float reloadRemaining)
        {
            if (reloading)
            {
                return reloadRemaining <= CellWarmSeconds ? 2f * (1f - reloadRemaining / CellWarmSeconds) : 0f;   // the new cassette warms
            }

            float perCell = Mathf.Max(1, magazine) / 3f;
            return ammo > cell * perCell + 1e-3f ? 2f : 0f;
        }

        /// <summary>The canister level in 8 steps (0..8) of ammo / magazine.</summary>
        public static int GelLevelStep(int ammo, int magazine)
        {
            if (magazine <= 0 || ammo <= 0) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(ammo / (float)magazine * LevelSteps), 1, LevelSteps);
        }

        // ------------------------------------------------------------------ life cycle

        public static SpecialWeaponGlow Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[SpecialWeaponGlow]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<SpecialWeaponGlow>();
            }

            s_instance.Rearm();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            _block = new MaterialPropertyBlock();
            if (PrototypeShaders.IsReady)
            {
                _canister = new Material(PrototypeShaders.VfxGel) { name = "DD_GelCanister (runtime)", hideFlags = HideFlags.DontSave };
                _canister.SetFloat("_Additive", 0f);
                _canister.SetFloat("_PlaneOn", 1f);
                _canister.renderQueue = 3001;   // after the glass (Transparent 3000): the liquid reads through the lantern window
            }

            Rearm();
        }

        private void OnDestroy()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            Unbind();
            if (s_instance == this) s_instance = null;
            if (_canister != null) Destroy(_canister);
        }

        public void Rearm()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialWeaponUsageEvents.Usage += OnUsage;
        }

        public void OnUsage(SpecialUsageEvent e)
        {
            if (e.Kind == SpecialUsageKind.Fired) _firedAt = Time.time;
        }

        // ------------------------------------------------------------------ bind

        private void Rebind(Transform body, SpecialWeaponId id, int modelId)
        {
            Unbind();
            _boundModel = modelId;
            _boundId = id;
            body.GetComponentsInChildren(true, s_renderers);
            for (int r = 0; r < s_renderers.Count; r++)
            {
                Renderer ren = s_renderers[r];
                ren.GetSharedMaterials(s_materials);
                for (int m = 0; m < s_materials.Count && _count < MaxBinds; m++)
                {
                    Material mat = s_materials[m];
                    if (mat == null) continue;
                    Part part = Classify(mat.name, ren.transform, body);
                    if (part == Part.None) continue;
                    _binds[_count++] = new Bind { R = ren, Slot = m, Part = part };
                    if (part == Part.GelLiquidRim && _canister != null && _liquidRenderer == null) SwapCanister(ren, m);
                }
            }

            s_renderers.Clear();
            s_materials.Clear();
        }

        private static Part Classify(string material, Transform t, Transform root)
        {
            if (material.StartsWith(ArcGlow, StringComparison.Ordinal))
            {
                for (Transform p = t; p != null && p != root; p = p.parent)
                {
                    string n = p.name;
                    int at = n.IndexOf("part_cell_", StringComparison.Ordinal);
                    if (at < 0 || at + 10 >= n.Length) continue;
                    char c = n[at + 10];
                    if (c == '1') return Part.Cell1;
                    if (c == '2') return Part.Cell2;
                    if (c == '3') return Part.Cell3;
                }

                return Part.ArcOther;
            }

            if (material.StartsWith(ArcBody, StringComparison.Ordinal)) return Part.ArcBodyEmission;
            if (material.StartsWith(GelGlow, StringComparison.Ordinal)) return Part.GelCoreSpecks;
            if (material.StartsWith(GelLiquid, StringComparison.Ordinal)) return Part.GelLiquidRim;
            if (material.StartsWith(GelBody, StringComparison.Ordinal)) return Part.GelNozzle;
            return Part.None;
        }

        private void SwapCanister(Renderer r, int slot)
        {
            _liquidRenderer = r;
            _liquidOriginal = r.sharedMaterials;                 // bind time only (allocates a copy; never per frame)
            _liquidSwapped = (Material[])_liquidOriginal.Clone();
            _liquidSwapped[slot] = _canister;
            r.sharedMaterials = _liquidSwapped;
        }

        private void Unbind()
        {
            for (int i = 0; i < _count; i++)
            {
                if (_binds[i].R != null) _binds[i].R.SetPropertyBlock(null, _binds[i].Slot);
                _binds[i] = default;
            }

            if (_liquidRenderer != null && _liquidOriginal != null) _liquidRenderer.sharedMaterials = _liquidOriginal;
            _liquidRenderer = null;
            _liquidOriginal = null;
            _liquidSwapped = null;
            _count = 0;
            _boundModel = 0;
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();
            WeaponController c = WeaponRegistry.ActiveController;
            Transform body = c != null && c.SpecialWeaponActive ? c.SpecialRigBody : null;
            if (body == null)
            {
                if (_count > 0 || _liquidRenderer != null) Unbind();
                return;
            }

            Transform model = body.childCount > 0 ? body.GetChild(0) : body;
            SpecialWeaponRuntime s = c.Special;
            if (_boundModel != model.GetInstanceID() || _boundId != s.Id) Rebind(body, s.Id, model.GetInstanceID());
            if (_count == 0) return;

            float now = Time.time;
            if (_wasReloading && !s.IsReloading) _refilledAt = now;
            _wasReloading = s.IsReloading;
            if (s.Id == SpecialWeaponId.Raiju) ApplyRaiju(s, now);
            else ApplyNamekuji(s, now);
        }

        private void ApplyRaiju(SpecialWeaponRuntime s, float now)
        {
            float sinceFired = now - _firedAt;
            bool reduce = GameFeelSettings.FlashScale < 0.99f;
            float charge = s.IsCharging ? s.Charge01 : 0f;
            float lastSliver = s.ChargeSeconds > 0f ? 1f - Mathf.Min(1f, 0.08f / s.ChargeSeconds) : 1f;
            bool whiteCyan = s.IsCharging && charge >= lastSliver;
            bool flash = sinceFired >= 0f && sinceFired < FireFlashSeconds;

            // Coil: 1 Hz idle → 6 Hz at full charge; a shot × 3 decaying over 0.25 s. Reduce flashes: no flicker.
            float hz = Mathf.Lerp(1f, 6f, charge);
            float wave = reduce ? 0.5f : 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f * hz);
            float coil = Mathf.Lerp(0.15f, 0.35f, wave) + charge * 1.2f;
            if (sinceFired >= 0f && sinceFired < 0.25f) coil = Mathf.Max(coil, 3f * (1f - sinceFired / 0.25f));
            LastCoil = coil;

            for (int i = 0; i < _count; i++)
            {
                ref Bind b = ref _binds[i];
                if (b.R == null) continue;
                Vector4 colour;
                switch (b.Part)
                {
                    case Part.Cell1:
                    case Part.Cell2:
                    case Part.Cell3:
                    {
                        int cell = (int)b.Part - (int)Part.Cell1;
                        float k = RaijuCell(cell, s.Ammo, s.MagazineSize, s.IsReloading, s.ReloadRemaining);
                        colour = k <= 0f ? Vector4.zero : flash ? WhiteCyan * 4f : whiteCyan ? WhiteCyan * 3f : Amber * k;
                        if (cell == 0) LastCell0 = k; else if (cell == 1) LastCell1 = k; else LastCell2 = k;
                        break;
                    }
                    case Part.ArcOther:
                        colour = Amber * 2f;
                        break;
                    case Part.ArcBodyEmission:
                        colour = new Vector4(coil, coil, coil, 1f);   // × the AP-021 emission map (its own colours)
                        break;
                    default:
                        continue;
                }

                Write(b.R, b.Slot, colour);
            }
        }

        private void ApplyNamekuji(SpecialWeaponRuntime s, float now)
        {
            float sinceFired = now - _firedAt;
            GelState state = GelStateOf(s.Ammo, s.CanFire, s.IsReloading, sinceFired);
            Vector3 k = GelIntensities(state);
            if (state == GelState.Charged && sinceFired >= 0.2f && sinceFired < 0.45f)
            {
                k = Vector3.Lerp(GelIntensities(GelState.Fire), k, (sinceFired - 0.2f) / 0.25f);   // back to charged over 0.25 s
            }

            LastGel = k;
            LastGelState = state;

            // Level: 8 steps of the magazine; the canister change drains it, the new one fills over the last 0.3 s.
            float level = GelLevelStep(s.Ammo, s.MagazineSize) / (float)LevelSteps;
            if (s.IsReloading) level = s.ReloadRemaining <= CanisterFillSeconds ? 1f - s.ReloadRemaining / CanisterFillSeconds : 0f;
            LastLevel01 = level;
            float sinceSlosh = Mathf.Min(now - _firedAt, now - _refilledAt);
            float slosh = (sinceSlosh >= 0f && sinceSlosh < 0.4f ? 2f : 1f) * 0.06f * Mathf.Sin(now * Mathf.PI * 2f * 0.5f);

            for (int i = 0; i < _count; i++)
            {
                ref Bind b = ref _binds[i];
                if (b.R == null) continue;
                switch (b.Part)
                {
                    case Part.GelCoreSpecks:
                        Write(b.R, b.Slot, GelCore * k.x);
                        break;
                    case Part.GelNozzle:
                        Write(b.R, b.Slot, new Vector4(k.z, k.z, k.z, 1f));
                        break;
                    case Part.GelLiquidRim:
                        if (b.R == _liquidRenderer) WriteCanister(b.R, b.Slot, k, level, slosh, now);
                        else Write(b.R, b.Slot, GelRim * k.y);
                        break;
                }
            }
        }

        private void Write(Renderer r, int slot, Vector4 emission)
        {
            _block.Clear();
            _block.SetVector(EmissionId, emission);
            r.SetPropertyBlock(_block, slot);
        }

        private void WriteCanister(Renderer r, int slot, Vector3 k, float level01, float slosh, float now)
        {
            Bounds wb = r.bounds;
            float surfaceY = Mathf.Lerp(wb.min.y, wb.max.y, level01) + (level01 >= 1f ? 0.05f : 0f);   // full: the lantern is full
            Transform t = r.transform;
            Vector3 n = (Vector3.up + t.right * slosh + t.forward * (slosh * 0.5f)).normalized;
            Vector3 point = new Vector3(wb.center.x, surfaceY, wb.center.z);
            _block.Clear();
            _block.SetVector(GelColorId, new Vector4(GelCore.x * k.x * 0.5f, GelCore.y * k.x * 0.5f, GelCore.z * k.x * 0.5f, 0.85f));
            _block.SetVector(GelRimId, new Vector4(GelRim.x * k.y, GelRim.y * k.y, GelRim.z * k.y, 3f));
            _block.SetVector(GelWobbleId, new Vector4(0.002f, 0.5f, 0f, 1f));
            _block.SetVector(GelPlaneId, new Vector4(n.x, n.y, n.z, Vector3.Dot(n, point)));
            r.SetPropertyBlock(_block, slot);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
