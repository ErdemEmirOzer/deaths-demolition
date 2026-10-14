using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4, user 11.10 note 8): the run scene's wall buy stations (CoD Zombies). Brought by the RunDirector like the
    /// gold service. Once the world is up it builds one <see cref="WallBuyStation"/> per catalog point, snapped to the
    /// NavMesh; every frame it finds the station within reach of the player (XZ, <see cref="WallBuyCatalog.InteractRadius"/>),
    /// publishes its prompt (<see cref="WallBuyEvents.PromptChanged"/>, only when it changes) and, on the Interact press
    /// (E / gamepad A), runs the purchase: <see cref="GoldWallet.TrySpend"/> with <see cref="GoldSource.WallBuy"/>, then
    /// <see cref="WeaponController.BuyClassic"/> - the weapon swap or, for the weapon in hand, the ammo refill. Gold is only
    /// taken when the weapon side can go through; nothing allocates per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallBuyDirector : MonoBehaviour
    {
        public static WallBuyDirector Instance { get; private set; }

        /// <summary>R4 rule (C-00 12.10): new per-frame code carries a marker - the reach check, the prompt and a press.</summary>
        private static readonly ProfilerMarker s_updateMarker = new ProfilerMarker("WP107.WallBuy.Update");

        private readonly List<WallBuyStation> _stations = new List<WallBuyStation>();
        private WallBuyCatalog _catalog;
        private Transform _root;
        private Material _wood, _plank, _sign, _lacquer;
        private bool _built;

        /// <summary>The stations built in this scene (empty until the world is up).</summary>
        public IReadOnlyList<WallBuyStation> Stations => _stations;

        public WallBuyCatalog Catalog => _catalog;

        /// <summary>The station in reach this frame, or null.</summary>
        public WallBuyStation InReach { get; private set; }

        /// <summary>Test seam: a press at the station in reach without the input device.</summary>
        public bool TestPress { get; set; }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            WallBuyEvents.ClearState();
        }

        private void OnDestroy()
        {
            if (_wood != null) Destroy(_wood);
            if (_plank != null) Destroy(_plank);
            if (_sign != null) Destroy(_sign);
            if (_lacquer != null) Destroy(_lacquer);
        }

        private void Update()
        {
            using (s_updateMarker.Auto()) Tick();
        }

        private void Tick()
        {
            if (!_built && !TryBuild()) return;

            PlayerRig rig = PlayerRegistry.Primary;
            WallBuyStation station = rig != null ? Nearest(rig.transform.position) : null;
            InReach = station;
            if (station == null)
            {
                WallBuyEvents.PublishPrompt(default);
                TestPress = false;
                return;
            }

            WallBuyQuote quote = QuoteFor(station);
            if (quote.Block == WallBuyBlock.Unavailable)
            {
                // Frozen run (round-end beat, upgrade screen) or no wallet: no prompt, a press does nothing (no "denied" sound).
                WallBuyEvents.PublishPrompt(default);
                TestPress = false;
                return;
            }

            WallBuyEvents.PublishPrompt(new WallBuyPrompt(station.Id, station.Weapon, station.WeaponKey, quote, station.transform.position));

            bool pressed = TestPress || InputSourceRegistry.Current.InteractPressed();
            TestPress = false;
            if (pressed) Resolve(station, quote);
        }

        /// <summary>The offer of <paramref name="station"/> for the player's state now.</summary>
        public WallBuyQuote QuoteFor(WallBuyStation station)
        {
            WeaponController controller = WeaponRegistry.ActiveController;
            GoldWallet wallet = GoldService.Instance != null ? GoldService.Instance.Wallet : null;
            RunDirector run = RunDirector.Instance;
            bool available = controller != null && wallet != null && wallet.IsOpen && run != null && WallBuyRules.PhaseAllows(run.Phase);
            bool hasClassic = controller != null && controller.Classic != null && controller.Classic.Definition != null;
            ClassicWeaponId inHand = hasClassic ? controller.Classic.Id : ClassicWeaponId.AssaultRifle;
            int round = run != null ? Mathf.Max(1, run.Round) : 1;
            return WallBuyRules.Quote(station.Weapon, _catalog.PriceOf(station.Weapon), _catalog.RefillShare, available,
                controller != null && controller.SpecialWeaponActive, hasClassic, inHand,
                controller != null && controller.IsClassicAmmoFull(round), wallet != null ? wallet.Balance : 0);
        }

        private void Resolve(WallBuyStation station, WallBuyQuote quote)
        {
            Vector3 at = station.transform.position;
            if (!quote.CanBuy)
            {
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Denied, quote.Block, station.Id, station.Weapon, quote.Cost, at));
                return;
            }

            GoldWallet wallet = GoldService.Instance.Wallet;
            WeaponController controller = WeaponRegistry.ActiveController;
            int round = Mathf.Max(1, RunDirector.Instance.Round);
            int transaction = wallet.NextTransactionId();
            GoldTransactionResult paid = wallet.TrySpend(quote.Cost, GoldSource.WallBuy, transaction);
            if (paid != GoldTransactionResult.Applied)
            {
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Denied, WallBuyBlock.NotEnoughGold, station.Id, station.Weapon, quote.Cost, at));
                return;
            }

            if (!controller.BuyClassic(station.Weapon, round))
            {
                // The quote said yes, the weapon side said no (a race with a Special pickup this frame): give the gold back.
                wallet.TryRefund(transaction);
                WallBuyEvents.PublishResolved(new WallBuyOutcome(WallBuyResult.Denied, WallBuyBlock.Unavailable, station.Id, station.Weapon, quote.Cost, at));
                return;
            }

            WallBuyResult result = quote.Action == WallBuyAction.Refill ? WallBuyResult.Refilled : WallBuyResult.Bought;
            WallBuyEvents.PublishResolved(new WallBuyOutcome(result, WallBuyBlock.None, station.Id, station.Weapon, quote.Cost, at));
        }

        private WallBuyStation Nearest(Vector3 player)
        {
            float best = _catalog.InteractRadius * _catalog.InteractRadius;
            WallBuyStation found = null;
            for (int i = 0; i < _stations.Count; i++)
            {
                WallBuyStation s = _stations[i];
                if (s == null) continue;
                Vector3 d = s.transform.position - player;
                float d2 = d.x * d.x + d.z * d.z;
                if (d2 <= best && Mathf.Abs(d.y) < 2.5f)
                {
                    best = d2;
                    found = s;
                }
            }

            return found;
        }

        /// <summary>Build the stations once the world and the player exist. False while it waits.</summary>
        private bool TryBuild()
        {
            if (PlayerRegistry.Primary == null || !PrototypeShaders.IsReady) return false;
            _catalog = WallBuyCatalog.Current;
            _built = true;
            if (_catalog == null)
            {
                Debug.LogWarning("[WallBuyDirector] no wall buy catalog at Resources/" + WallBuyCatalog.ResourcePath);
                return false;
            }

            _wood = NewLit("WallBuy/Wood", new Color(0.22f, 0.15f, 0.10f));
            _plank = NewLit("WallBuy/Plank", new Color(0.40f, 0.29f, 0.18f));
            _sign = NewLit("WallBuy/Sign", new Color(0.80f, 0.74f, 0.60f));
            _lacquer = NewLit("WallBuy/Lacquer", new Color(0.52f, 0.11f, 0.07f));
            _root = new GameObject("WallBuyStations").transform;
            _root.SetParent(transform, false);

            WallBuyStationLibrary art = WallBuyStationLibrary.Current;   // WP-107B: the AP-040 bodies (placeholder racks without it)
            CollectOverhead();   // before the stations exist: their own renderers are never in the list
            IReadOnlyList<WallBuyCatalog.StationRow> rows = _catalog.Stations;
            for (int i = 0; i < rows.Count; i++)
            {
                WallBuyCatalog.StationRow row = rows[i];
                if (!WallBuyCatalog.TryWeapon(row.weapon, out ClassicWeaponId weapon) || _catalog.PriceOf(weapon) <= 0) continue;
                var go = new GameObject("WallBuy_" + row.id);
                go.transform.SetParent(_root, false);
                go.transform.SetPositionAndRotation(Snap(new Vector3(row.x, 0f, row.z), _catalog.SnapRadius), Quaternion.Euler(0f, row.yaw, 0f));
                var station = go.AddComponent<WallBuyStation>();
                WeaponDefinition def = Arsenal.Get(weapon);
                station.Init(row.id, weapon, def != null ? def.weaponId : row.weapon, def != null && def.IsWallBuyOnly, art, _wood, _plank, _sign, _lacquer);
                station.SettledMetres = Settle(station);
                Carve(station);
                _stations.Add(station);
            }

            return true;
        }

        /// <summary>
        /// a1-fix (R4 FINAL a1x 14.10, WorldBoundaryPlayTests.NavMeshYollari): the station's box collider stood on a baked NavMesh
        /// that did not know it was there. A zombie path, and the capsule test's walk, ran into the board. WB_CourtyardWest at
        /// (−13, 0) is on the west–east line: the 0.45 m capsule stopped at x −14.07, which is −13 − 0.59 (the board's half width)
        /// − 0.45 − 0.03 skin.
        /// A carving obstacle the size of the collider cuts the NavMesh under the station (plus the agent radius) once, after
        /// <see cref="Settle"/>. The station never moves again, so paths go round it.
        /// Added after Settle on purpose: Settle reads the NavMesh at each candidate's standing point. The carve's front edge is
        /// half the depth + 0.5 m from the pivot (board 0.8 m, shrine 1.0 m). That stays short of the standing point
        /// (FrontDepth + 0.9 m: 1.2 / 1.4 m).
        /// </summary>
        private static void Carve(WallBuyStation station)
        {
            BoxCollider box = station.GetComponent<BoxCollider>();
            if (box == null) return;
            var obstacle = station.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;   // it never moves: carve on the first NavMesh update, not after the stationary wait
        }

        /// <summary>
        /// Metres from a station's pivot to the far edge of its carved NavMesh hole: the collider's larger half extent in XZ plus
        /// the agent radius. Tests use it to tell a station that stands on the NavMesh from one that stands off it.
        /// </summary>
        public static float CarveReach(WallBuyStation station)
        {
            BoxCollider box = station != null ? station.GetComponent<BoxCollider>() : null;
            if (box == null) return 0f;
            return Mathf.Max(box.size.x, box.size.z) * 0.5f + NavMesh.GetSettingsByID(0).agentRadius;
        }

        public const float SettleSearchMetres = 20f, SettleStepMetres = 0.75f, RoofCheckMetres = 30f, StandRadius = 0.4f;

        /// <summary>
        /// WP-107B (AD 12.10 MUST 1): the top-down camera sits south of and above the player (north is screen up), so a tall roof
        /// just south of a station hides it although nothing is over it: the station's top must also see along this direction
        /// (≈ 60° up, toward -Z) without a CameraOccluder in the way.
        /// </summary>
        public static readonly Vector3 CameraLine = new Vector3(0f, 0.866f, -0.5f);

        /// <summary>
        /// WP-107B (AD 12.10 MUST 1/2): a station stands in the open - nothing above it (no roof, eave or beam collider within
        /// <see cref="RoofCheckMetres"/> over its top corners and its standing point), its body overlaps no other collider, and its
        /// front has NavMesh standing room. Where the catalog point fails, the nearest clean point within
        /// <see cref="SettleSearchMetres"/> (rings of <see cref="SettleStepMetres"/>, eight bearings, deterministic) is taken.
        /// Returns the metres moved (0 = the catalog point was clean; negative = no clean point found, the catalog point kept).
        /// </summary>
        private static float Settle(WallBuyStation station)
        {
            Transform t = station.transform;
            Vector3 origin = t.position;
            if (IsClear(station)) return 0f;
            for (float r = SettleStepMetres; r <= SettleSearchMetres + 0.001f; r += SettleStepMetres)
            {
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f;
                    Vector3 candidate = Snap(origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, 1f);
                    t.position = candidate;
                    Physics.SyncTransforms();
                    if (IsClear(station)) return (candidate - origin).magnitude;
                }
            }

            t.position = origin;
            Physics.SyncTransforms();
            Debug.LogWarning("[WallBuyDirector] no clean point within " + SettleSearchMetres + " m of " + station.Id + " - at its catalog point: " + BlockReason(station));
            return -1f;
        }

        /// <summary>Open sky over the station and its standing point, no body overlap, NavMesh in front (see <see cref="Settle"/>).</summary>
        public static bool IsClear(WallBuyStation station) => BlockReason(station) == null;

        /// <summary>Why a station is not in the open (null = it is): the first failing check and what failed it (tests, logs).</summary>
        public static string BlockReason(WallBuyStation station)
        {
            BoxCollider box = station.GetComponent<BoxCollider>();
            if (box == null) return null;
            Transform t = station.transform;
            Vector3 half = box.size * 0.5f;
            Vector3 stand = StandPoint(station);
            if (!NavMesh.SamplePosition(stand, out NavMeshHit _, 0.6f, NavMesh.AllAreas)) return "no NavMesh at the standing point";
            for (int c = 0; c < 5; c++)
            {
                Vector3 from = c < 4
                    ? t.TransformPoint(box.center + new Vector3((c & 1) == 0 ? -half.x : half.x, half.y + 0.05f, (c & 2) == 0 ? -half.z : half.z))
                    : stand + Vector3.up * 2f;
                // Solid colliders of every layer, plus the roofs' CameraOccluder layer even if a roof is a trigger (not the
                // region / acoustic volumes, which are triggers on other layers).
                if (Physics.Raycast(from, Vector3.up, out RaycastHit hit, RoofCheckMetres, ~0, QueryTriggerInteraction.Ignore)
                    || Physics.Raycast(from, Vector3.up, out hit, RoofCheckMetres, 1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder, QueryTriggerInteraction.Collide))
                    return "collider above: " + hit.collider.name;
                if (c < 4 && Physics.Raycast(from, CameraLine, out hit, RoofCheckMetres, 1 << DD.Prototype.Core.PrototypeLayers.CameraOccluder, QueryTriggerInteraction.Collide))
                    return "occluder on the camera line: " + hit.collider.name;
                int over = c < 4 ? OverheadBlocks(from) : -1;
                if (over >= 0) return "renderer above: " + s_overheadNames[over];
            }

            // The body, lifted off the ground and shrunk a little: any other static collider inside it is a clip.
            Vector3 centre = t.TransformPoint(box.center + Vector3.up * 0.1f);
            Vector3 inner = new Vector3(Mathf.Max(0.05f, half.x - 0.04f), Mathf.Max(0.05f, half.y - 0.12f), Mathf.Max(0.05f, half.z - 0.04f));
            Collider[] hits = Physics.OverlapBox(centre, inner, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider h = hits[i];
                if (h == box || h.attachedRigidbody != null || h.transform.IsChildOf(t)) continue;
                return "collider inside the body: " + h.name;
            }

            Collider[] standHits = Physics.OverlapCapsule(stand + Vector3.up * (StandRadius + 0.15f), stand + Vector3.up * 1.6f, StandRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < standHits.Length; i++)
            {
                if (standHits[i].attachedRigidbody == null && !standHits[i].transform.IsChildOf(t)) return "collider at the standing point: " + standHits[i].name;
            }

            return null;
        }

        private static readonly List<Bounds> s_overhead = new List<Bounds>(512);
        private static readonly List<string> s_overheadNames = new List<string>(512);
        private static int s_overheadScene = -1;

        /// <summary>
        /// b107h: the sakura canopies (S-05's placed trees) have no collider, so no ray saw the one over WB_SakuraNorth and the
        /// station stood fully hidden under it. The renderers that rise above a station's top count too: every enabled mesh
        /// renderer at least 1.5 m tall, not a moving body (player, zombies, pickups), not the stations themselves, not a
        /// sky / ground plane (over 60 m across). Collected once per scene; their bounds are conservative.
        /// </summary>
        public static void CollectOverhead()
        {
            s_overhead.Clear();
            s_overheadNames.Clear();
            s_overheadScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            MeshRenderer[] all = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer r = all[i];
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer == 5) continue;
                Bounds b = r.bounds;
                if (b.max.y < 1.5f || b.extents.x > 30f || b.extents.z > 30f) continue;
                if (r.GetComponentInParent<WallBuyStation>() != null || r.GetComponentInParent<Rigidbody>() != null
                    || r.GetComponentInParent<CharacterController>() != null || r.GetComponentInParent<NavMeshAgent>() != null) continue;
                s_overhead.Add(b);
                s_overheadNames.Add(r.name);
            }
        }

        /// <summary>
        /// A renderer between a station's top corner and the sky or the camera: the up / camera-line ray enters its bounds within
        /// <see cref="RoofCheckMetres"/>; a corner already inside bounds counts only when they rise 1.5 m over it (a canopy, not a
        /// fence or wall the station stands beside).
        /// </summary>
        private static int OverheadBlocks(Vector3 from)
        {
            if (s_overheadScene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) CollectOverhead();
            var up = new Ray(from, Vector3.up);
            var cam = new Ray(from, CameraLine);
            for (int i = 0; i < s_overhead.Count; i++)
            {
                Bounds b = s_overhead[i];
                if (b.max.y <= from.y || b.SqrDistance(from) > RoofCheckMetres * RoofCheckMetres) continue;
                if (b.Contains(from))
                {
                    if (b.max.y > from.y + 1.5f) return i;
                    continue;
                }

                if ((b.IntersectRay(up, out float d) && d <= RoofCheckMetres) || (b.IntersectRay(cam, out d) && d <= RoofCheckMetres)) return i;
            }

            return -1;
        }

        /// <summary>Where the player stands to use the station: in front of its face.</summary>
        public static Vector3 StandPoint(WallBuyStation station)
        {
            return station.transform.position + station.transform.forward * (station.FrontDepth + 0.9f);
        }

        /// <summary>The nearest NavMesh point within <paramref name="radius"/> (a station the player can always walk to); the point itself without a NavMesh.</summary>
        public static Vector3 Snap(Vector3 point, float radius)
        {
            if (radius > 0f && NavMesh.SamplePosition(point + Vector3.up, out NavMeshHit hit, radius, NavMesh.AllAreas)) return hit.position;
            return point;
        }

        private static Material NewLit(string materialName, Color color)
        {
            var m = new Material(PrototypeShaders.Lit) { name = materialName };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            return m;
        }
    }
}
