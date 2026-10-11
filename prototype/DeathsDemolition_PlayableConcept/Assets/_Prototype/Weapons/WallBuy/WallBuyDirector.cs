using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
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
                station.Init(row.id, weapon, def != null ? def.weaponId : row.weapon, def != null && def.IsWallBuyOnly, _wood, _plank, _sign, _lacquer);
                _stations.Add(station);
            }

            return true;
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
