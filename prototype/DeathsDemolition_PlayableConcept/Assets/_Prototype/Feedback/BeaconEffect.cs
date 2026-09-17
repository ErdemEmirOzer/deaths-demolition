using DD.Prototype.Feedback.Vfx;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The light column that marks a Special Weapon on the ground (REQ-053).
    ///
    /// Unlike every other effect here it is not fire-and-forget: the pickup owns it for
    /// as long as the weapon is on the ground and releases it explicitly. So its
    /// lifetime is effectively infinite and <see cref="SetUrgency"/> is what turns it
    /// from a calm marker into a warning as the ground timer runs out.
    ///
    /// G-21: IT NO LONGER OWNS ITS OWN RHYTHM.
    /// It used to pulse on <c>Time.time * rate</c>, with the rate derived from urgency.
    /// That was correct while the beacon was the only surface saying "hurry". It is not
    /// any more: the HUD marker now blinks too, and the numeric ground timer it used to
    /// sit next to is gone. Two surfaces running two clocks at the same NOMINAL rate
    /// still drift apart in phase, and a player reads the drift as two different events.
    /// So the owner integrates one phase (<see cref="GroundBlinkClock"/>) and pushes it
    /// here through <see cref="SetPulse"/>. Urgency now only chooses the COLOUR.
    /// </summary>
    public sealed class BeaconEffect : PooledEffect
    {
        private Transform _column;
        private Renderer _columnRenderer;
        private Light _light;
        private MaterialPropertyBlock _block;

        private Color _calmColor = new Color(1f, 0.82f, 0.35f, 0.35f);
        private Color _urgentColor = new Color(1f, 0.35f, 0.2f, 0.55f);
        private float _urgency;
        private float _pulse = 1f;
        private float _baseHeight = 12f;
        private float _baseRadius = 0.55f;
        private bool _cached;

        // WP-019 · AP-010 beacon: two crossed column cards (UV-scrolling, additive), a ground
        // ring and the 16-frame ring loop, all greyscale textures tinted at runtime with the
        // reserved beacon colour (DEC-053). Built once per instance when the catalog is there;
        // the WP-007 cylinder stays the fallback and is hidden otherwise.
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private Transform _ap010Root;
        private MeshRenderer[] _ap010Columns;
        private MeshRenderer _ap010Ring;
        private MeshRenderer _ap010RingLoop;
        private VfxCatalogEntry _ringLoopRow;
        private MaterialPropertyBlock _ap010Block;
        private float _ringClock;
        private bool _ap010Tried;

        /// <summary>True when the AP-010 column/ring cards are drawn (test readout).</summary>
        public bool UsesAp010 => _ap010Root != null;

        private void Cache()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _column = transform.childCount > 0 ? transform.GetChild(0) : null;
            _columnRenderer = _column != null ? _column.GetComponent<Renderer>() : null;
            _light = GetComponent<Light>();
            _block = new MaterialPropertyBlock();
            BuildAp010();
        }

        private void BuildAp010()
        {
            if (_ap010Tried)
            {
                return;
            }

            _ap010Tried = true;
            if (!VfxLibrary.TryGetPlayable(VfxIds.BeaconColumn, out VfxCatalogEntry column) ||
                !VfxLibrary.TryGetPlayable(VfxIds.BeaconRing, out VfxCatalogEntry ring))
            {
                return;
            }

            _ap010Block = new MaterialPropertyBlock();
            var root = new GameObject("AP010");
            root.transform.SetParent(transform, false);
            _ap010Root = root.transform;

            // Manifest: column 1.2 m dia × 6 m, bright at the base fading up, UV scroll 0.2/s (material).
            _ap010Columns = new MeshRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                _ap010Columns[i] = Card(root.transform, "Column" + i, column.Material, new Vector3(0f, 3f, 0f), Quaternion.Euler(0f, i * 90f, 0f), new Vector3(1.2f, 6f, 1f));
            }

            // Ground ring 2.4 m, flat (quad normal -Z → up).
            _ap010Ring = Card(root.transform, "Ring", ring.Material, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(ring.WorldSizeMetres, ring.WorldSizeMetres, 1f));

            if (VfxLibrary.TryGetPlayable(VfxIds.BeaconRingAnim, out VfxCatalogEntry loop))
            {
                _ringLoopRow = loop;
                _ap010RingLoop = Card(root.transform, "RingLoop", loop.Material, new Vector3(0f, 0.03f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(loop.WorldSizeMetres, loop.WorldSizeMetres, 1f));
            }

            if (_column != null)
            {
                _column.gameObject.SetActive(false);   // the WP-007 cylinder is the fallback only
            }
        }

        private static MeshRenderer Card(Transform parent, string name, Material material, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        private void TickAp010(Color tint, float pulse)
        {
            if (_ap010Root == null)
            {
                return;
            }

            // Manifest: additive at alpha 0.3 for the COLUMN as a whole; two crossed cards add up, so each
            // card carries half (art-director fix 1: the stacked pair read like a gold-band core).
            var c = new Color(tint.r, tint.g, tint.b, 1f);
            float alpha = Mathf.Lerp(0.11f, 0.19f, pulse);
            _ap010Block.SetColor(TintId, c);
            _ap010Block.SetFloat(AlphaId, alpha);
            _ap010Block.SetFloat(FrameId, 0f);
            for (int i = 0; i < _ap010Columns.Length; i++)
            {
                _ap010Columns[i].SetPropertyBlock(_ap010Block);
            }

            _ap010Block.SetFloat(AlphaId, 0.3f);
            _ap010Ring.SetPropertyBlock(_ap010Block);

            if (_ap010RingLoop != null && _ringLoopRow != null)
            {
                _ringClock += Time.deltaTime;
                float fps = Mathf.Max(1f, _ringLoopRow.Fps);
                int frame = Mathf.FloorToInt(_ringClock * fps) % Mathf.Max(1, _ringLoopRow.FramesUsed);
                _ap010Block.SetFloat(FrameId, frame);
                _ap010Block.SetFloat(AlphaId, 0.3f);
                _ap010RingLoop.SetPropertyBlock(_ap010Block);
            }
        }

        /// <summary>Start the marker. It stays until <see cref="PooledEffect.Despawn"/> is called by the owner.</summary>
        public void Begin(float height, float radius, Color calm, Color urgent)
        {
            Cache();

            _baseHeight = Mathf.Max(1f, height);
            _baseRadius = Mathf.Max(0.05f, radius);
            _calmColor = calm;
            _urgentColor = urgent;
            _urgency = 0f;
            _pulse = 1f;

            // Effectively "never expires on its own".
            Lifetime = float.MaxValue;

            if (_column != null)
            {
                _column.localScale = new Vector3(_baseRadius * 2f, _baseHeight * 0.5f, _baseRadius * 2f);
                _column.localPosition = new Vector3(0f, _baseHeight * 0.5f, 0f);
            }

            if (_light != null)
            {
                _light.enabled = true;
                _light.range = _baseHeight;
                _light.color = calm;
            }

            Tick(0f);
        }

        /// <summary>0 = calm marker, 1 = about to disappear. Drives COLOUR only (G-21).</summary>
        public void SetUrgency(float urgency01)
        {
            _urgency = Mathf.Clamp01(urgency01);
        }

        /// <summary>
        /// The blink envelope for this frame, from the owner's integrated phase.
        /// Feed it <see cref="GroundBlinkClock.Intensity01"/>; anything else will read as
        /// a different beat from the HUD marker, which is the failure this exists to stop.
        /// </summary>
        public void SetPulse(float intensity01)
        {
            _pulse = Mathf.Clamp01(intensity01);
        }

        protected override void Tick(float t)
        {
            Cache();

            // The rhythm arrives from outside (see the class remarks). What is left here
            // is only the mapping from "how bright right now" to what the column does.
            float pulse = _pulse;

            Color c = Color.Lerp(_calmColor, _urgentColor, _urgency);
            c.a *= pulse;

            if (_columnRenderer != null)
            {
                _columnRenderer.GetPropertyBlock(_block);
                FeedbackMaterials.ApplyColor(_block, c);
                _columnRenderer.SetPropertyBlock(_block);
            }

            if (_column != null && _ap010Root == null)
            {
                float r = _baseRadius * (0.85f + 0.35f * pulse) * 2f;
                _column.localScale = new Vector3(r, _baseHeight * 0.5f, r);
            }

            TickAp010(c, pulse);

            if (_light != null)
            {
                _light.color = Color.Lerp(_calmColor, _urgentColor, _urgency);
                _light.intensity = Mathf.Lerp(1.6f, 3.6f, _urgency) * pulse;
            }
        }

        protected override void OnDespawned()
        {
            if (_light != null)
            {
                _light.enabled = false;
            }

            _urgency = 0f;
            _pulse = 1f;
        }
    }
}
