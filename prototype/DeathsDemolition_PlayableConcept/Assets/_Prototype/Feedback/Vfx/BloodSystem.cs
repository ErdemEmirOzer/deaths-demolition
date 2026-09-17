using System.Collections.Generic;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019b · The blood layer (directive §10, gore HIGH, no dismemberment). Owned by
    /// <see cref="VfxDressing"/>, called after the director's budget check like every other
    /// AP-010 family. Numbers are the art-director brief (Fable, 2026-09-16,
    /// <c>wp-019/evidence/blood-design-brief-fable.md</c>) and PROTOTYPE-ONLY.
    ///
    /// Per flesh hit: one directional splash card (60 % shot direction + 40 % of its
    /// reflection about the hit normal, card rolled to that direction, 0.26 s), a droplet
    /// burst (<see cref="BloodDropletEffect"/>, GPU-instanced quads) and a mark where the
    /// splash ray meets ground or wall (≤ 2.5 m) plus a 40 % drip mark under the hit.
    /// Critical / heavy hits and armour breaks spread 2 cards; a death adds two cards, 36
    /// droplets and a pool that grows 0.4 → 1.1 m (1.4 m heavy) over 3 s.
    ///
    /// Marks persist for the round in their own 128-slot pool: impact marks may hold at
    /// most 40 % of it, death pools are evicted last, eviction (from 24 slots short of full) is a 1 s fade of the oldest
    /// (LRU), a new impact mark within 0.25 m of a live one grows that one ×1.15 instead of
    /// spawning, marks never land within 0.6 m of a live gold packet and cap alpha at 0.85
    /// (DEC-077). Settled marks farther than 30 m from the camera (≈ 10 m beyond the player's
    /// focus at the 20 m gameplay camera) are copied into the merged chunk mesh
    /// (<see cref="BloodDecalMerger"/>) and their slot freed. Event LOD: ≥ 32 m no droplets, no
    /// impact mark, one card ×1.2; ≥ 45 m no card (the pool only).
    /// </summary>
    public sealed class BloodSystem
    {
        public enum HitClass
        {
            Normal = 0,
            Critical = 1,
            ArmorBreak = 2
        }

        public const int DecalCapacity = 128;
        public const int DropletCapacity = 12;
        public const int ImpactShareMax = 52;                 // 40 % of 128
        /// <summary>Slots short of full at which the LRU fade starts: ~20 marks/s in the dense scenario × the 1 s fade.</summary>
        public const int EvictHeadroom = 24;
        public const float GoldExclusionMetres = 0.6f;
        public const float DecalAlphaCap = 0.85f;
        public const float SplashSeconds = 0.26f;
        public const float SplashFadeFraction = 4f / 12f;      // last 4 of 12 frames
        public const float MergeDistanceMetres = 0.25f;
        public const float MergeGrowth = 1.15f;
        public const float MergeSizeCap = 0.6f;
        // The gameplay camera sits ~20 m from its focus (PrototypeCameraRig offset 0/15/−13), so the brief's
        // 18/20/35 m (written for a 2 m / 15 m A/B camera) are measured from the camera as "focus + 10 / 12 / 25 m".
        public const float FarLodMetres = 30f;
        public const float EventLodNoDetailMetres = 32f;
        public const float EventLodNoCardMetres = 45f;
        public const float SettledSeconds = 2f;
        public const float EvictFadeSeconds = 1f;
        public const float RoundResetFadeSeconds = 2f;
        public const float RoundDecalSeconds = 1800f;          // "persistent for the round": longer than any round
        public const float SplashRayMetres = 2.5f;
        public const float PoolRayMetres = 1.0f;
        public const float PoolGrowFrom = 0.4f;
        // AD M4 (v003 review): final pool diameter ≤ 1.0 m (≤ 36 px at 20 m), heavy 1.2 m; at most 12 pools live, the oldest fades when a 13th arrives.
        public const float PoolSize = 1.0f;
        public const float PoolSizeHeavy = 1.2f;
        public const int PoolShareMax = 12;
        public const float PoolGrowSeconds = 3f;
        public const float PoolGrowSecondsHeavy = 3.5f;

        private static readonly List<VfxCatalogEntry> s_splash = new List<VfxCatalogEntry>(8);
        private static readonly List<VfxCatalogEntry> s_heavy = new List<VfxCatalogEntry>(8);
        private static readonly List<VfxCatalogEntry> s_ground = new List<VfxCatalogEntry>(8);
        private static readonly List<VfxCatalogEntry> s_wall = new List<VfxCatalogEntry>(8);
        private static readonly List<VfxCatalogEntry> s_pool = new List<VfxCatalogEntry>(8);

        private PrototypePool<FlipbookEffect> _cards;
        private PrototypePool<DecalEffect> _decals;
        private PrototypePool<BloodDropletEffect> _droplets;
        private readonly BloodDecalMerger _merger = new BloodDecalMerger();
        private readonly List<DecalEffect> _live = new List<DecalEffect>(DecalCapacity);
        private readonly RaycastHit[] _hits = new RaycastHit[1];
        private VfxCatalogEntry _dropletRow;
        private int _mask;
        private uint _seed = 0x9E3779B9u;
        private int _counter;
        private float _lastPlacedSize;
        private float _nextLodPass;
        private bool _ready;
        private bool _subscribed;

        public bool IsReady => _ready;
        public bool DropletsReady => _droplets != null;
        public PrototypePool<DecalEffect> DecalPool => _decals;
        public PrototypePool<BloodDropletEffect> DropletPool => _droplets;
        public BloodDecalMerger Merger => _merger;

        // Counters (tests / perf stage readout).
        public int Splashes { get; private set; }
        public int DropletBursts { get; private set; }
        public int Marks { get; private set; }
        public int Pools { get; private set; }
        public int Merged { get; private set; }
        public int Evicted { get; private set; }
        public int SkippedForGold { get; private set; }
        /// <summary>Final (grown) size of the last death pool placed — the gold clipping readout.</summary>
        public float LastPoolSize { get; private set; }
        public int LiveMarks { get { Prune(); return _live.Count; } }

        public int LiveImpactMarks
        {
            get
            {
                Prune();
                int n = 0;
                for (int i = 0; i < _live.Count; i++) if (_live[i].EvictionClass == DecalEffect.Priority.Impact) n++;
                return n;
            }
        }

        public void Configure(Transform templateRoot, PrototypePool<FlipbookEffect> cards)
        {
            _cards = cards;
            if (!VfxLibrary.IsReady || cards == null)
            {
                _ready = false;
                return;
            }

            if (_decals == null)
            {
                var template = new GameObject("FX_AP010_BloodDecal");
                template.transform.SetParent(templateRoot, false);
                template.SetActive(false);
                _decals = PoolService.GetOrCreate(template.AddComponent<DecalEffect>(), DecalCapacity / 4, DecalCapacity, PoolExhaustedPolicy.RecycleOldest);
                _merger.Configure(templateRoot);
                _mask = (1 << 0) | PrototypeLayerMask("Environment");
            }

            if (_droplets == null && VfxLibrary.TryGetPlayable(VfxIds.BloodDroplet, out _dropletRow))
            {
                var template = new GameObject("FX_AP010_BloodDroplets");
                template.transform.SetParent(templateRoot, false);
                template.SetActive(false);
                BloodDropletEffect t = template.AddComponent<BloodDropletEffect>();
                t.Configure(_dropletRow);
                _droplets = PoolService.GetOrCreate(t, 3, DropletCapacity, PoolExhaustedPolicy.RecycleOldest);
            }

            if (!_subscribed)
            {
                _subscribed = true;
                RunEvents.RoundStarted += OnRoundStarted;
                RunEvents.RunEnded += OnRunEnded;
            }

            _ready = true;
        }

        private static int PrototypeLayerMask(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? 1 << layer : 0;
        }

        // ------------------------------------------------------------------ events

        /// <summary>A landed flesh hit (normal, critical/heavy, or the flesh under a broken plate).</summary>
        public bool PlayHit(Vector3 point, Vector3 normal, Vector3 shotDirection, HitClass hitClass)
        {
            if (!_ready)
            {
                return false;
            }

            float distance = CameraDistance(point);
            Vector3 dir = SplashDirection(normal, shotDirection);
            bool any = false;

            int cards; float scale; int drops; float dropMin, dropMax, speedMin, speedMax; float spread; float markSize; float dripSize; bool drip;
            switch (hitClass)
            {
                case HitClass.Critical:
                    cards = 2; scale = 1.3f; drops = 22; dropMin = 0.03f; dropMax = 0.05f; speedMin = 3f; speedMax = 5f; spread = 20f; markSize = 0.55f; dripSize = 0.35f; drip = true;
                    break;
                case HitClass.ArmorBreak:
                    cards = 2; scale = 1.1f; drops = 14; dropMin = 0.02f; dropMax = 0.05f; speedMin = 2f; speedMax = 4f; spread = 25f; markSize = 0.45f; dripSize = 0f; drip = false;
                    break;
                default:
                    cards = 1; scale = 1f; drops = 12; dropMin = 0.02f; dropMax = 0.05f; speedMin = 2f; speedMax = 4f; spread = 0f; markSize = 0.4f; dripSize = 0.25f; drip = NextUnit() < 0.4f;
                    break;
            }

            if (distance >= EventLodNoDetailMetres)
            {
                cards = distance >= EventLodNoCardMetres ? 0 : 1;
                scale *= 1.2f;
                drops = 0;
                markSize = 0f;
                drip = false;
            }

            bool heavySheet = hitClass != HitClass.Normal;
            for (int i = 0; i < cards; i++)
            {
                float roll = cards == 1 ? 0f : (i == 0 ? -spread : spread);
                // v003: the A7 heavy-hit sheets (twin cone / lobe burst) carry the critical / armour-break spread themselves — one card, the second card only for the v002 fallback.
                any |= PlaySplashCard(point, Quaternion.AngleAxis(roll, normal) * dir, scale, heavySheet);
                if (heavySheet && s_heavy.Count > 0 && s_heavy[0].Family == VfxFamily.Blood) break;
            }

            if (drops > 0)
            {
                any |= PlayDroplets(point, dir, drops, dropMin, dropMax, speedMin, speedMax, 0.45f);
            }

            if (markSize > 0f)
            {
                any |= PlaySplashMark(point, dir, markSize);
            }

            if (drip && dripSize > 0f)
            {
                any |= PlayDripMark(point, dripSize);
            }

            return any;
        }

        /// <summary>The blood of a kill: two cards, 36 droplets and the growing pool at the feet. The death sheet itself stays in <see cref="VfxDressing"/>.</summary>
        public bool PlayDeath(Vector3 feet, Vector3 point, Vector3 normal, Vector3 shotDirection, bool heavy)
        {
            if (!_ready)
            {
                return false;
            }

            float distance = CameraDistance(point);
            Vector3 dir = SplashDirection(normal, shotDirection);
            bool any = false;

            if (distance < EventLodNoCardMetres)
            {
                if (distance < EventLodNoDetailMetres)
                {
                    any |= PlaySplashCard(point, Quaternion.AngleAxis(-30f, normal) * dir, 1.3f, false);
                    any |= PlaySplashCard(point, Quaternion.AngleAxis(30f, normal) * dir, 1.3f, false);
                    any |= PlayDroplets(point, dir, 36, 0.03f, 0.06f, 3f, 6f, 0.6f);
                }
                else
                {
                    any |= PlaySplashCard(point, dir, 1.3f * 1.2f, false);
                }
            }

            any |= PlayPool(feet, heavy);
            return any;
        }

        /// <summary>Per-frame housekeeping: the far-LOD absorb pass (every 0.5 s) and the merged mesh rebuild.</summary>
        public void Tick(float now)
        {
            if (!_ready)
            {
                return;
            }

            if (now >= _nextLodPass)
            {
                _nextLodPass = now + 0.5f;
                FarLodPass();
            }

            _merger.Tick(now);
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>
        /// One splash card. AP-010B v003 rows (family Blood) play as authored: manifest size and
        /// duration (their capsule-cover ledger is 0), origin (0.12, 0.5) on the hit point with the
        /// sheet's +u rolled to the splash direction, colour as painted (no recolour). The v002
        /// flesh puff fallback keeps the brief's numbers: scale, 0.26 s, 0.35 × size offset, recolour.
        /// </summary>
        private bool PlaySplashCard(Vector3 point, Vector3 dir, float scale, bool heavy)
        {
            List<VfxCatalogEntry> slot = heavy ? s_heavy : s_splash;
            if (BloodSlots.Resolve(heavy ? BloodSlots.Heavy : BloodSlots.Splash, slot) == 0)
            {
                return false;
            }

            VfxCatalogEntry row = slot[NextIndex(slot.Count)];
            bool authored = row.Family == VfxFamily.Blood;
            float size = authored ? row.WorldSizeMetres : row.WorldSizeMetres * scale;
            FlipbookEffect fx = _cards.Get(authored ? point : point + dir * (0.35f * size), Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            if (authored)
            {
                fx.Play(row, FlipbookEffect.Facing.Camera, size, BloodPalette.AuthoredSheetTint, 1f, 0.2f);
                // AD M2: the A7 twin-cone / lobe sheets are symmetric; random mirror + a ±12° roll bias give every burst a handedness.
                float jitter = heavy ? (NextUnit() - 0.5f) * 24f : 0f;
                fx.SetAlign(jitter != 0f ? Quaternion.AngleAxis(jitter, Vector3.up) * dir : dir, true);
                fx.SetMirror(NextUnit() < 0.5f);
            }
            else
            {
                Color tint = BloodPalette.TintFor(row, BloodPalette.Fresh, BloodPalette.SplashCalibration * BloodPalette.FreshHdrGain);
                tint.a = 1f;
                fx.Play(row, FlipbookEffect.Facing.Camera, size, tint, 1f, SplashFadeFraction, SplashSeconds);
                fx.SetAlign(dir);
            }

            Splashes++;
            return true;
        }

        private bool PlayDroplets(Vector3 point, Vector3 dir, int count, float sizeMin, float sizeMax, float speedMin, float speedMax, float life)
        {
            if (_droplets == null)
            {
                return false;
            }

            BloodDropletEffect burst = _droplets.Get(point, Quaternion.LookRotation(dir, Vector3.up));
            if (burst == null)
            {
                return false;
            }

            burst.Play(count, sizeMin, sizeMax, speedMin, speedMax, life);
            DropletBursts++;
            return true;
        }

        private bool PlaySplashMark(Vector3 point, Vector3 dir, float size)
        {
            if (Physics.RaycastNonAlloc(point, MarkRay(dir), _hits, SplashRayMetres, _mask, QueryTriggerInteraction.Ignore) == 0)
            {
                return false;
            }

            RaycastHit hit = _hits[0];
            bool ground = hit.normal.y > 0.5f;
            List<VfxCatalogEntry> slot = ground ? s_ground : s_wall;
            if (BloodSlots.Resolve(ground ? BloodSlots.GroundDecal : BloodSlots.WallDecal, slot) == 0)
            {
                return false;
            }

            // Farther along the ray → smaller, fainter mark. v003 marks are authored at their own size.
            float reach = 1f - 0.4f * Mathf.Clamp01(hit.distance / SplashRayMetres);
            VfxCatalogEntry row = slot[NextIndex(slot.Count)];
            float markSize = row.Family == VfxFamily.Blood ? row.WorldSizeMetres * (0.5f + 0.5f * reach) : size * reach;
            return PlaceMark(row, hit.point, hit.normal, markSize, DecalEffect.Priority.Impact, 0f, 0f, ground ? Random.Range(0f, 360f) : 0f);
        }

        private bool PlayDripMark(Vector3 point, float size)
        {
            if (Physics.RaycastNonAlloc(point, Vector3.down, _hits, SplashRayMetres, _mask, QueryTriggerInteraction.Ignore) == 0)
            {
                return false;
            }

            if (BloodSlots.Resolve(BloodSlots.GroundDecal, s_ground) == 0)
            {
                return false;
            }

            VfxCatalogEntry row = s_ground[NextIndex(s_ground.Count)];
            return PlaceMark(row, _hits[0].point, _hits[0].normal, row.Family == VfxFamily.Blood ? row.WorldSizeMetres * 0.6f : size, DecalEffect.Priority.Impact, 0f, 0f, Random.Range(0f, 360f));
        }

        private bool PlayPool(Vector3 feet, bool heavy)
        {
            if (Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, _hits, PoolRayMetres + 0.5f, _mask, QueryTriggerInteraction.Ignore) == 0)
            {
                return false;   // no ground under the body → no pool (brief 4)
            }

            if (BloodSlots.Resolve(BloodSlots.Pool, s_pool) == 0)
            {
                return false;
            }

            float size = heavy ? PoolSizeHeavy : PoolSize;
            float grow = heavy ? PoolGrowSecondsHeavy : PoolGrowSeconds;
            bool placed = PlaceMark(s_pool[NextIndex(s_pool.Count)], _hits[0].point, _hits[0].normal, size, DecalEffect.Priority.Pool, PoolGrowFrom, grow, Random.Range(0f, 360f));
            if (placed)
            {
                Pools++;
                LastPoolSize = _lastPlacedSize;
            }

            return placed;
        }

        /// <summary>Gold exclusion, merge rule, eviction, then the pooled mark. False when skipped.</summary>
        private bool PlaceMark(VfxCatalogEntry row, Vector3 point, Vector3 normal, float size, DecalEffect.Priority priority, float growFrom, float growSeconds, float spin)
        {
            float goldDistance = NearestGoldDistance(point);
            if (goldDistance < GoldExclusionMetres)
            {
                SkippedForGold++;
                return false;
            }

            // A pool whose edge would reach the exclusion ring is clipped instead of skipped.
            if (priority == DecalEffect.Priority.Pool && goldDistance < float.MaxValue)
            {
                size = Mathf.Min(size, 2f * (goldDistance - GoldExclusionMetres));
                if (size < 0.2f)
                {
                    SkippedForGold++;
                    return false;
                }
            }

            Prune();
            if (priority == DecalEffect.Priority.Impact)
            {
                for (int i = 0; i < _live.Count; i++)
                {
                    DecalEffect d = _live[i];
                    if (d.EvictionClass == DecalEffect.Priority.Impact && !d.IsFading && (d.transform.position - point).sqrMagnitude < MergeDistanceMetres * MergeDistanceMetres)
                    {
                        d.SetSize(Mathf.Min(MergeSizeCap, d.CurrentSize * MergeGrowth));
                        return true;
                    }
                }
            }

            Evict(priority);

            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            Vector3 up = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            DecalEffect decal = _decals.Get(point, Quaternion.LookRotation(n, up));
            if (decal == null)
            {
                return false;
            }

            _lastPlacedSize = size;
            // v003 marks are painted in the band: shown as authored, drying only darkens (wet → dry ratio); v002 marks are recoloured.
            bool authored = row.Family == VfxFamily.Blood;
            decal.Play(row, size, RoundDecalSeconds, DecalAlphaCap, authored ? Color.white : BloodPalette.TintFor(row, BloodPalette.DecalWet, BloodPalette.MarkCalibration), spin);
            decal.SetEvictionClass(priority);
            decal.SetDrying(true, authored);
            if (growSeconds > 0f)
            {
                decal.SetGrow(growFrom, growSeconds);
            }

            // A RecycleOldest instance comes back as the same object: drop its stale entry before re-listing it.
            _live.Remove(decal);
            _live.Add(decal);
            Marks++;
            return true;
        }

        /// <summary>
        /// LRU with classes. Impact marks keep to their 40 % share: every impact mark that is
        /// not already fading beyond the share fades now (a burst of hits fades several at once,
        /// so the share holds after the burst, not only while it lasts). Near the capacity any
        /// oldest mark fades, impact first, death pools last.
        /// </summary>
        private void Evict(DecalEffect.Priority incoming)
        {
            int impactsHolding = 0, holding = 0;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].IsFading) continue;
                holding++;
                if (_live[i].EvictionClass == DecalEffect.Priority.Impact) impactsHolding++;
            }

            if (incoming == DecalEffect.Priority.Impact)
            {
                while (impactsHolding >= ImpactShareMax && FadeOldest(DecalEffect.Priority.Impact))
                {
                    impactsHolding--;
                    holding--;
                }
            }
            else
            {
                int poolsHolding = holding - impactsHolding;
                while (poolsHolding >= PoolShareMax && FadeOldest(DecalEffect.Priority.Pool))
                {
                    poolsHolding--;
                    holding--;
                }
            }

            while (holding >= DecalCapacity - EvictHeadroom)
            {
                if (FadeOldest(DecalEffect.Priority.Impact) || FadeOldest(DecalEffect.Priority.Pool))
                {
                    holding--;
                }
                else
                {
                    break;
                }
            }
        }

        private bool FadeOldest(DecalEffect.Priority priority)
        {
            DecalEffect oldest = null;
            for (int i = 0; i < _live.Count; i++)
            {
                DecalEffect d = _live[i];
                if (d.EvictionClass == priority && !d.IsFading && (oldest == null || d.AgeSeconds > oldest.AgeSeconds))
                {
                    oldest = d;
                }
            }

            if (oldest == null)
            {
                return false;
            }

            oldest.BeginFade(EvictFadeSeconds);   // marks it fading either way; the loops above count it out
            Evicted++;
            return true;
        }

        private void FarLodPass()
        {
            if (!FeedbackCameraRef.TryGetPosition(out Vector3 camera))
            {
                return;
            }

            Prune();
            float far2 = FarLodMetres * FarLodMetres;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                DecalEffect d = _live[i];
                if (d.AgeSeconds < SettledSeconds || d.IsFading || (d.transform.position - camera).sqrMagnitude < far2)
                {
                    continue;
                }

                if (_merger.Absorb(d))
                {
                    Merged++;
                    _live.RemoveAt(i);
                    d.Despawn();
                }
            }
        }

        private void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i] == null || !_live[i].IsSpawned)
                {
                    _live.RemoveAt(i);
                }
            }
        }

        /// <summary>Fades every mark and drops the merged chunks (a new round starts clean; the run end clears at once).</summary>
        public void ResetMarks(float fadeSeconds)
        {
            Prune();
            for (int i = 0; i < _live.Count; i++)
            {
                if (fadeSeconds > 0f) _live[i].BeginFade(fadeSeconds);
                else _live[i].Despawn();
            }

            if (fadeSeconds <= 0f)
            {
                _live.Clear();
            }

            _merger.Clear();
        }

        private void OnRoundStarted(int round)
        {
            ResetMarks(RoundResetFadeSeconds);
        }

        private void OnRunEnded(RunSummary summary)
        {
            ResetMarks(0f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Exit spray: 60 % of the shot direction + 40 % of its reflection about the hit
        /// normal. (The brief's draft had the weights the other way round; for a frontal hit,
        /// normal = −shot, that mix collapses to a weak vector pointing back at the shooter,
        /// so the marks would land at the player's feet instead of behind the target.)
        /// </summary>
        public static Vector3 SplashDirection(Vector3 normal, Vector3 shotDirection)
        {
            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            Vector3 s = shotDirection.sqrMagnitude > 1e-4f ? shotDirection.normalized : -n;
            Vector3 dir = s * 0.6f + Vector3.Reflect(s, n) * 0.4f;
            return dir.sqrMagnitude > 1e-4f ? dir.normalized : s;
        }

        /// <summary>The mark ray: the splash direction bent 0.3 down (blood arcs to the ground).</summary>
        public static Vector3 MarkRay(Vector3 splashDirection)
        {
            return (splashDirection + Vector3.down * 0.3f).normalized;
        }

        private static float CameraDistance(Vector3 point)
        {
            return FeedbackCameraRef.TryGetPosition(out Vector3 camera) ? Vector3.Distance(camera, point) : 0f;
        }

        private static float NearestGoldDistance(Vector3 point)
        {
            GoldService gold = GoldService.Instance;
            return gold != null ? gold.NearestLiveOrbDistance(point) : float.MaxValue;
        }

        private int NextIndex(int count)
        {
            return count <= 1 ? 0 : (_counter++) % count;
        }

        /// <summary>Cheap deterministic 0..1 sequence for the probability rolls (tests can count on the pattern).</summary>
        private float NextUnit()
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            return (_seed & 0xFFFFFFu) / 16777216f;
        }
    }
}
