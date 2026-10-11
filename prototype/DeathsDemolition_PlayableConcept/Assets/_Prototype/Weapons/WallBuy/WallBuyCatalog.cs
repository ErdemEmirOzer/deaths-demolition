using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4, user 11.10 note 8): the wall buy data of a world - the price of every classic weapon, the ammo refill
    /// share, the reach and the station points - read from <c>Resources/WallBuy/wallbuy-v6.json</c> (S-04 owns the prices,
    /// S-05 the points). The rules live in <see cref="WallBuyRules"/>, the scene side in <see cref="WallBuyDirector"/>.
    /// </summary>
    public sealed class WallBuyCatalog
    {
        public const string ResourcePath = "WallBuy/wallbuy-v6";

        [Serializable]
        private sealed class PriceRow
        {
            public string weapon;
            public int price;
        }

        [Serializable]
        public sealed class StationRow
        {
            public string id;
            public string weapon;
            public float x;
            public float z;
            public float yaw;
        }

        [Serializable]
        private sealed class FileData
        {
            public string schema;
            public float refillShare = 0.5f;
            public float interactRadius = 2.2f;
            public float snapRadius = 4f;
            public PriceRow[] prices = new PriceRow[0];
            public StationRow[] stations = new StationRow[0];
        }

        private readonly Dictionary<ClassicWeaponId, int> _prices = new Dictionary<ClassicWeaponId, int>();
        private readonly List<StationRow> _stations = new List<StationRow>();

        public string Schema { get; private set; }

        /// <summary>The share of a weapon's price an ammo refill costs (CoD: half).</summary>
        public float RefillShare { get; private set; }

        /// <summary>Horizontal metres from a station's centre within which the player can use it.</summary>
        public float InteractRadius { get; private set; }

        /// <summary>Metres a station point may move to land on the NavMesh.</summary>
        public float SnapRadius { get; private set; }

        public IReadOnlyList<StationRow> Stations => _stations;

        private static WallBuyCatalog s_loaded;

        /// <summary>The shipped catalog, loaded once (null when the file is missing or unreadable).</summary>
        public static WallBuyCatalog Current
        {
            get
            {
                if (s_loaded == null)
                {
                    TextAsset text = Resources.Load<TextAsset>(ResourcePath);
                    s_loaded = text != null ? Parse(text.text) : null;
                }

                return s_loaded;
            }
        }

        /// <summary>Test seam: replace the shipped catalog (null reloads it from Resources).</summary>
        public static void SetForTests(WallBuyCatalog catalog)
        {
            s_loaded = catalog;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_loaded = null;
        }

        /// <summary>Read a catalog from its JSON text; null when the text is not a catalog.</summary>
        public static WallBuyCatalog Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            FileData data;
            try
            {
                data = JsonUtility.FromJson<FileData>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (data == null) return null;
            var catalog = new WallBuyCatalog
            {
                Schema = data.schema,
                RefillShare = Mathf.Clamp01(data.refillShare),
                InteractRadius = Mathf.Max(0.5f, data.interactRadius),
                SnapRadius = Mathf.Max(0f, data.snapRadius)
            };

            if (data.prices != null)
            {
                for (int i = 0; i < data.prices.Length; i++)
                {
                    PriceRow row = data.prices[i];
                    if (row != null && row.price > 0 && TryWeapon(row.weapon, out ClassicWeaponId id)) catalog._prices[id] = row.price;
                }
            }

            if (data.stations != null)
            {
                for (int i = 0; i < data.stations.Length; i++)
                {
                    StationRow row = data.stations[i];
                    if (row != null && TryWeapon(row.weapon, out _)) catalog._stations.Add(row);
                }
            }

            return catalog;
        }

        /// <summary>The weapon's wall price, or 0 when this world does not sell it.</summary>
        public int PriceOf(ClassicWeaponId id)
        {
            return _prices.TryGetValue(id, out int price) ? price : 0;
        }

        /// <summary>The ammo refill price of a weapon: <see cref="RefillShare"/> of its price, rounded up, at least 1.</summary>
        public int RefillPriceOf(ClassicWeaponId id)
        {
            return WallBuyRules.RefillCost(PriceOf(id), RefillShare);
        }

        /// <summary>A weapon key (ar_1 …) or legacy name to a classic id; false for Specials and unknown keys.</summary>
        public static bool TryWeapon(string key, out ClassicWeaponId id)
        {
            WeaponDefinition d = Arsenal.Get(key);
            if (d != null && d.slot == WeaponSlotKind.Classic && (int)d.id >= 0)
            {
                id = d.id;
                return true;
            }

            id = ClassicWeaponId.AssaultRifle;
            return false;
        }
    }
}
