using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The interaction map (WIND-SPEC §3): a world-space window (256² over 40 m at High, 128² over 24 m at
    /// Performance) snapped to the texel grid around the player. Per texel: rg = displacement (fraction of the class cap),
    /// b = flatten (0..2), a = the SIM TIME of the last stamp (age = now − a; ≥ 8 s = empty). Storing the stamp time
    /// instead of an age means only stamped texels change per frame (no full-grid decay loop); the shader evaluates the
    /// per-class spring (ω, ζ by stiffness) and flatten hold / half-life from the age. Addressing is TOROIDAL
    /// (texel = world texel mod size, Repeat wrap): re-centring never copies, it only clears the strips that enter.
    /// CPU-side so it is deterministic and testable without GPU readback; 0 B per frame.
    /// </summary>
    public sealed class VegetationPushMap
    {
        public const float EmptyTime = -10000f;
        public const float MaxAge = 8f;
        public const int MaxBursts = 16;

        private struct Burst
        {
            public Vector2 Position;
            public float Radius;
            public float Strength;
            public float Front;
            public bool Active;
        }

        public readonly int Size;
        public readonly float WorldSize;
        public readonly float Texel;
        public Texture2D Texture { get; private set; }
        public float Time { get; private set; }
        /// <summary>World XZ of the window's minimum corner (texel-snapped).</summary>
        public Vector2 Origin => new Vector2(_ox * Texel, _oz * Texel);
        public int StampedTexelsLastStep { get; private set; }
        public int ActiveBursts { get; private set; }

        /// <summary>Grass-class spring and flatten curves for the stamp blend (shader evaluates per class).</summary>
        public float Omega = 8f, Zeta = 0.7f, FlatHold = 0.4f, FlatHalfLife = 1.0f;
        /// <summary>Explosion front speed (m/s) and ring thickness (m).</summary>
        public float BurstFrontSpeed = 15f, BurstRing = 0.6f;

        private readonly Vector4[] _state;
        private readonly Burst[] _bursts = new Burst[MaxBursts];
        private int _ox, _oz;
        private bool _centred;
        private bool _dirty;

        public VegetationPushMap(int size, float worldSize, bool createTexture)
        {
            Size = Mathf.Clamp(size, 16, 1024);
            WorldSize = Mathf.Max(4f, worldSize);
            Texel = WorldSize / Size;
            _state = new Vector4[Size * Size];
            for (int i = 0; i < _state.Length; i++) _state[i] = new Vector4(0f, 0f, 0f, EmptyTime);
            if (createTexture)
            {
                Texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true)
                {
                    name = "DD_VegPushMap",
                    wrapMode = TextureWrapMode.Repeat,
                    // Point: the shader does its own 4-tap with each texel's own age (bilinear over the stamp-time
                    // channel would leak stale displacement from expired neighbours) — and no float-filtering caps needed.
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _dirty = true;
                Upload();
            }
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                if (Application.isPlaying) Object.Destroy(Texture);
                else Object.DestroyImmediate(Texture);
                Texture = null;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _state.Length; i++) _state[i] = new Vector4(0f, 0f, 0f, EmptyTime);
            for (int i = 0; i < _bursts.Length; i++) _bursts[i].Active = false;
            ActiveBursts = 0;
            _dirty = true;
        }

        public void Advance(float dt)
        {
            Time += Mathf.Max(0f, dt);
            StampedTexelsLastStep = 0;
        }

        /// <summary>Snaps the window around <paramref name="focus"/>; clears only the strips that enter.</summary>
        public void Recenter(Vector2 focus)
        {
            int nx = Mathf.FloorToInt(focus.x / Texel) - Size / 2;
            int nz = Mathf.FloorToInt(focus.y / Texel) - Size / 2;
            if (!_centred)
            {
                _ox = nx;
                _oz = nz;
                _centred = true;
                return;
            }

            int dx = nx - _ox, dz = nz - _oz;
            if (dx == 0 && dz == 0) return;
            if (Mathf.Abs(dx) >= Size || Mathf.Abs(dz) >= Size)
            {
                for (int i = 0; i < _state.Length; i++) _state[i] = new Vector4(0f, 0f, 0f, EmptyTime);
            }
            else
            {
                // Columns entering on x: [old end, new end) when moving +, [new start, old start) when moving −.
                int x0 = dx > 0 ? _ox + Size : nx, x1 = dx > 0 ? nx + Size : _ox;
                for (int gx = x0; gx < x1; gx++) ClearColumn(gx);
                int z0 = dz > 0 ? _oz + Size : nz, z1 = dz > 0 ? nz + Size : _oz;
                for (int gz = z0; gz < z1; gz++) ClearRow(gz);
            }

            _ox = nx;
            _oz = nz;
            _dirty = true;
        }

        private void ClearColumn(int gx)
        {
            int x = Wrap(gx);
            for (int z = 0; z < Size; z++) _state[z * Size + x] = new Vector4(0f, 0f, 0f, EmptyTime);
        }

        private void ClearRow(int gz)
        {
            int row = Wrap(gz) * Size;
            for (int x = 0; x < Size; x++) _state[row + x] = new Vector4(0f, 0f, 0f, EmptyTime);
        }

        private int Wrap(int g)
        {
            int m = g % Size;
            return m < 0 ? m + Size : m;
        }

        private bool InWindow(int gx, int gz) => gx >= _ox && gz >= _oz && gx < _ox + Size && gz < _oz + Size;

        /// <summary>
        /// One pusher (player / zombie) for one step (WIND-SPEC §3 stamp): falloff q = (1 − (r/R)²)², direction radial +
        /// velocity bias, 50 ms attack toward the larger of current and target, flatten builds with dwell up to its cap.
        /// </summary>
        public void Stamp(Vector2 position, Vector2 velocity, float radius, float strength, float flattenRate, float flattenCap, float velocityBias, float dt)
        {
            if (!_centred || radius <= 0f || strength <= 0f) return;
            Vector2 velDir = velocity.sqrMagnitude >= 0.04f ? velocity.normalized * velocityBias : Vector2.zero;
            float attack = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / 0.05f);
            int ax = Mathf.FloorToInt((position.x - radius) / Texel), bx = Mathf.FloorToInt((position.x + radius) / Texel);
            int az = Mathf.FloorToInt((position.y - radius) / Texel), bz = Mathf.FloorToInt((position.y + radius) / Texel);
            float r2max = radius * radius;
            for (int gz = az; gz <= bz; gz++)
            {
                for (int gx = ax; gx <= bx; gx++)
                {
                    if (!InWindow(gx, gz)) continue;
                    float cx = (gx + 0.5f) * Texel - position.x, cz = (gz + 0.5f) * Texel - position.y;
                    float d2 = cx * cx + cz * cz;
                    if (d2 >= r2max) continue;
                    float f = 1f - d2 / r2max;
                    float q = f * f;
                    float d = Mathf.Sqrt(d2);
                    Vector2 radial = d > 1e-4f ? new Vector2(cx / d, cz / d) : Vector2.zero;
                    Vector2 dir = radial + velDir;
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
                    dir.Normalize();
                    Vector2 target = dir * (strength * q);
                    if (target.sqrMagnitude < 1e-4f) continue;
                    ref Vector4 s = ref _state[Wrap(gz) * Size + Wrap(gx)];
                    float age = Time - s.w;
                    if (age >= MaxAge) { s = new Vector4(0f, 0f, 0f, Time - MaxAge); age = MaxAge; }
                    Vector2 cur = new Vector2(s.x, s.y) * Release(age, Omega, Zeta);
                    Vector2 big = cur.sqrMagnitude > target.sqrMagnitude ? cur : target;
                    cur = Vector2.Lerp(cur, big, attack);
                    float flatNow = s.z * Flatten(age, FlatHold, FlatHalfLife);
                    float flat = Mathf.Max(flatNow, Mathf.Min(flattenCap, flatNow + flattenRate * q * dt));
                    s = new Vector4(cur.x, cur.y, flat, Time);
                    StampedTexelsLastStep++;
                    _dirty = true;
                }
            }
        }

        /// <summary>Explosion (WIND-SPEC §3): instant core (r &lt; 0.6 R, flatten 1.8) then a 0.6 m ring front at 15 m/s to 1.6 R.</summary>
        public void AddBurst(Vector2 position, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f) return;
            int slot = -1;
            for (int i = 0; i < _bursts.Length; i++)
            {
                if (!_bursts[i].Active) { slot = i; break; }
            }

            if (slot < 0) slot = 0;   // full: the oldest slot is recycled
            _bursts[slot] = new Burst { Position = position, Radius = radius, Strength = Mathf.Clamp01(strength), Front = 0f, Active = true };
            Ring(ref _bursts[slot], 0f, 0.6f * radius);
            _bursts[slot].Front = 0.6f * radius;
        }

        public void StepBursts(float dt)
        {
            int active = 0;
            for (int i = 0; i < _bursts.Length; i++)
            {
                if (!_bursts[i].Active) continue;
                ref Burst b = ref _bursts[i];
                float next = b.Front + BurstFrontSpeed * Mathf.Max(0f, dt);
                Ring(ref b, Mathf.Max(0f, Mathf.Min(b.Front, next - BurstRing)), next);
                b.Front = next;
                if (b.Front > 1.6f * b.Radius) b.Active = false;
                else active++;
            }

            ActiveBursts = active;
        }

        private void Ring(ref Burst b, float inner, float outer)
        {
            if (!_centred) return;
            float R = b.Radius;
            float reach = Mathf.Min(outer, 1.6f * R);
            int ax = Mathf.FloorToInt((b.Position.x - reach) / Texel), bx = Mathf.FloorToInt((b.Position.x + reach) / Texel);
            int az = Mathf.FloorToInt((b.Position.y - reach) / Texel), bz = Mathf.FloorToInt((b.Position.y + reach) / Texel);
            for (int gz = az; gz <= bz; gz++)
            {
                for (int gx = ax; gx <= bx; gx++)
                {
                    if (!InWindow(gx, gz)) continue;
                    float cx = (gx + 0.5f) * Texel - b.Position.x, cz = (gz + 0.5f) * Texel - b.Position.y;
                    float r = Mathf.Sqrt(cx * cx + cz * cz);
                    if (r < inner || r > reach) continue;
                    float s, flat;
                    if (r < 0.6f * R) { s = 1f; flat = 1.8f; }
                    else if (r < R) { float k = (r - 0.6f * R) / (0.4f * R); s = Mathf.Lerp(1f, 0.3f, k); flat = Mathf.Lerp(1.8f, 0.5f, k); }
                    else { float k = (r - R) / (0.6f * R); s = Mathf.Lerp(0.3f, 0f, k); flat = Mathf.Lerp(0.5f, 0f, k); }
                    s *= b.Strength;
                    flat *= b.Strength;
                    if (s <= 0.01f && flat <= 0.01f) continue;
                    Vector2 dir = r > 1e-4f ? new Vector2(cx / r, cz / r) : Vector2.right;
                    ref Vector4 st = ref _state[Wrap(gz) * Size + Wrap(gx)];
                    float age = Mathf.Min(MaxAge, Time - st.w);
                    Vector2 cur = new Vector2(st.x, st.y) * Release(age, Omega, Zeta);
                    Vector2 target = dir * s;
                    Vector2 big = cur.sqrMagnitude > target.sqrMagnitude ? cur : target;
                    float flatNow = st.z * Flatten(age, FlatHold, FlatHalfLife);
                    st = new Vector4(big.x, big.y, Mathf.Max(flatNow, flat), Time);
                    StampedTexelsLastStep++;
                    _dirty = true;
                }
            }
        }

        public void Upload()
        {
            if (!_dirty || Texture == null) return;
            // Not SetPixelData<T>(T[]): it sizes the element with Marshal.SizeOf(data[0]), which boxes (GC every frame).
            // Write straight into the texture's own CPU copy instead.
            Unity.Collections.NativeArray<Vector4> pixels = Texture.GetPixelData<Vector4>(0);
            pixels.CopyFrom(_state);
            Texture.Apply(false, false);
            _dirty = false;
        }

        // ------------------------------------------------------------------ readouts (tests, probe) and curves

        /// <summary>Displacement magnitude at a world point (grass curve), 0 outside the window.</summary>
        public float DisplacementAt(Vector2 world)
        {
            if (!TryTexel(world, out int idx)) return 0f;
            Vector4 s = _state[idx];
            float age = Time - s.w;
            if (age >= MaxAge) return 0f;
            return new Vector2(s.x, s.y).magnitude * Mathf.Abs(Release(age, Omega, Zeta));
        }

        public float FlattenAt(Vector2 world)
        {
            if (!TryTexel(world, out int idx)) return 0f;
            Vector4 s = _state[idx];
            float age = Time - s.w;
            if (age >= MaxAge) return 0f;
            return s.z * Flatten(age, FlatHold, FlatHalfLife);
        }

        private bool TryTexel(Vector2 world, out int idx)
        {
            int gx = Mathf.FloorToInt(world.x / Texel), gz = Mathf.FloorToInt(world.y / Texel);
            idx = Wrap(gz) * Size + Wrap(gx);
            return _centred && InWindow(gx, gz);
        }

        /// <summary>Spring released from rest (WIND-SPEC §3): underdamped for ζ &lt; 1, critically damped at ζ ≥ 1.</summary>
        public static float Release(float age, float omega, float zeta)
        {
            if (age <= 0f) return 1f;
            if (zeta >= 0.999f) return (1f + omega * age) * Mathf.Exp(-omega * age);
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            return Mathf.Exp(-zeta * omega * age) * (Mathf.Cos(wd * age) + zeta / Mathf.Sqrt(1f - zeta * zeta) * Mathf.Sin(wd * age));
        }

        public static float Flatten(float age, float hold, float halfLife)
        {
            if (age < hold) return 1f;
            return Mathf.Pow(2f, -(age - hold) / Mathf.Max(0.01f, halfLife));
        }
    }
}
