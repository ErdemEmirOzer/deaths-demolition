using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>One stat of the Selection StatStrip: its pack label, the printed value and the bar (0..1, or &lt; 0 = no bar).</summary>
    public struct WeaponStat
    {
        public string Key;
        public string Label;
        public string Value;
        public float Bar01;
    }

    /// <summary>
    /// M4-R1 · E9 Selection StatStrip (R1 content pack section 1: each weapon's four stats, "wiring guidance, not a string"). Every
    /// number is read from the weapon's Arsenal definition (WP-069), so the screen invents none (GD P14).
    /// Bars compare a value with the classic roster: longer = more of the good thing. Reload, accuracy (spread) and self
    /// damage count "less is better", so their bars are the roster's best value over this one. A stat that fewer than two
    /// classic weapons have (blast radius, self damage) has nothing to compare with and gets no bar, only its number.
    /// </summary>
    public static class WeaponStatReadout
    {
        public const string Damage = "stat.damage";
        public const string Pellets = "stat.pellets";
        public const string FireRate = "stat.fire_rate";
        public const string Magazine = "stat.magazine";
        public const string Reload = "stat.reload";
        public const string Range = "stat.range";
        public const string Accuracy = "stat.accuracy";
        public const string Handling = "stat.handling";
        public const string Stagger = "stat.stagger";
        public const string Penetration = "stat.penetration";
        public const string Armour = "stat.armour";
        public const string Headshot = "stat.headshot";
        public const string BlastRadius = "stat.blast_radius";
        public const string SelfDamage = "stat.self_damage";

        /// <summary>The pack's StatStrip per weapon (two rows of two, in this order).</summary>
        private static readonly Dictionary<string, string[]> Strips = new Dictionary<string, string[]>
        {
            { "ar_1", new[] { Damage, FireRate, Magazine, Range } },
            { "ar_2", new[] { Damage, FireRate, Headshot, Armour } },
            { "smg_1", new[] { FireRate, Magazine, Handling, Armour } },
            { "smg_2", new[] { Damage, Stagger, Magazine, FireRate } },
            { "sg_1", new[] { Damage, Pellets, Range, Reload } },
            { "sg_2", new[] { Damage, Pellets, Reload, Stagger } },
            { "lmg_1", new[] { Magazine, FireRate, Handling, Reload } },
            { "lmg_2", new[] { Damage, Armour, Penetration, Handling } },
            { "sr_1", new[] { Damage, Penetration, Headshot, Accuracy } },
            { "rl_1", new[] { Damage, BlastRadius, SelfDamage, Reload } },
        };

        private static readonly string[] Fallback = { Damage, FireRate, Magazine, Reload };

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>The four stat keys of this weapon's strip (the generic four for a weapon the pack has not written).</summary>
        public static string[] StripOf(string weaponId)
        {
            string[] keys;
            return weaponId != null && Strips.TryGetValue(weaponId, out keys) ? keys : Fallback;
        }

        /// <summary>Fill <paramref name="into"/> (length ≥ 4) with the weapon's four stats. Built when a card is shown, never per frame.</summary>
        public static void Fill(WeaponDefinition def, WeaponStat[] into)
        {
            string[] keys = StripOf(def != null ? def.weaponId : null);
            for (int i = 0; i < 4 && i < into.Length; i++)
            {
                into[i] = Read(def, keys[i]);
            }
        }

        public static WeaponStat Read(WeaponDefinition def, string key)
        {
            var stat = new WeaponStat { Key = key, Label = UiStrings.Get(key, key), Value = "—", Bar01 = -1f };
            if (def == null)
            {
                return stat;
            }

            float value = Raw(def, key);
            stat.Value = Print(def, key, value);
            stat.Bar01 = Bar(key, value);
            return stat;
        }

        /// <summary>The number the stat is about, in the unit the value prints in.</summary>
        public static float Raw(WeaponDefinition d, string key)
        {
            switch (key)
            {
                case Damage: return d.explosive.enabled ? d.explosive.directHitDamage + d.explosive.blastDamage : d.fire.damage;
                case Pellets: return Mathf.Max(1, d.fire.pelletCount);
                case FireRate: return d.fire.rpm;
                case Magazine: return d.ammo.magazine;
                case Reload: return d.ammo.reloadTacSeconds;
                case Range: return d.projectile.maxRange;
                case Accuracy: return d.accuracy.thirdPerson.hip;
                case Handling: return d.accuracy.moveSpeedMultiplier;
                case Stagger: return d.projectile.stagger;
                case Penetration: return d.projectile.penetration;
                case Armour: return d.projectile.armorDamageMultiplier;
                case Headshot: return 2f * d.projectile.headMultiplier;   // the regional ×2.0 rule times the weapon's own
                case BlastRadius: return d.explosive.enabled ? d.explosive.outerRadius : 0f;
                case SelfDamage: return d.explosive.enabled ? d.explosive.selfDamageMax : 0f;
                default: return 0f;
            }
        }

        private static bool LessIsBetter(string key)
        {
            return key == Reload || key == Accuracy || key == SelfDamage;
        }

        private static string Print(WeaponDefinition d, string key, float v)
        {
            switch (key)
            {
                case Damage:
                    return d.fire.pelletCount > 1 && !d.explosive.enabled
                        ? Fmt("stat.fmt.pellets", "{value} × {pellets}", Num(v), "pellets", d.fire.pelletCount.ToString(Invariant))
                        : Num(v);
                case FireRate: return Fmt("stat.fmt.rpm", "{value} RPM", Num(v));
                case Magazine:
                    return d.ammo.reserve >= 0
                        ? Fmt("stat.fmt.mag_reserve", "{value} / {reserve}", Num(v), "reserve", d.ammo.reserve.ToString(Invariant))
                        : Num(v);
                case Reload: return Fmt("stat.fmt.seconds", "{value} s", Num(v));
                case Range:
                case BlastRadius: return Fmt("stat.fmt.metres", "{value} m", Num(v));
                case Accuracy: return Num(v) + "°";
                case Handling:
                case Armour:
                case Headshot: return Fmt("stat.fmt.multiplier", "×{value}", Num(v));
                case Stagger: return Mathf.RoundToInt(v * 100f).ToString(Invariant) + "%";
                case Penetration: return Fmt("stat.fmt.bodies", "{value} BODIES", Num(v));
                case SelfDamage: return Num(v);
                default: return Num(v);
            }
        }

        private static string Num(float v)
        {
            return Mathf.Abs(v - Mathf.Round(v)) < 0.005f ? UiStrings.Number(Mathf.RoundToInt(v)) : UiStrings.Number(v, "0.##");   // M-65
        }

        private static string Fmt(string key, string fallback, string value, string name2 = null, string value2 = null)
        {
            string text = UiStrings.Get(key, fallback).Replace("{value}", value);
            return name2 != null ? text.Replace("{" + name2 + "}", value2) : text;
        }

        // ------------------------------------------------------------------ roster comparison

        private static Dictionary<string, Vector2> s_extent;   // key → (best-for-bar, count of classic weapons with it)

        /// <summary>Tests: recompute the roster maxima (another catalog was installed).</summary>
        public static void ResetForTests()
        {
            s_extent = null;
        }

        private static float Bar(string key, float v)
        {
            if (s_extent == null)
            {
                s_extent = BuildExtent();
            }

            Vector2 e;
            if (!s_extent.TryGetValue(key, out e) || e.y < 2f || v <= 0f || e.x <= 0f)
            {
                return -1f;
            }

            return Mathf.Clamp01(LessIsBetter(key) ? e.x / v : v / e.x);
        }

        private static Dictionary<string, Vector2> BuildExtent()
        {
            var keys = new[] { Damage, Pellets, FireRate, Magazine, Reload, Range, Accuracy, Handling, Stagger, Penetration, Armour, Headshot, BlastRadius, SelfDamage };
            var extent = new Dictionary<string, Vector2>(keys.Length);
            IReadOnlyList<WeaponDefinition> all = Arsenal.All;
            foreach (string key in keys)
            {
                float best = 0f;
                int count = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    WeaponDefinition d = all[i];
                    if (d == null || !d.enabled || d.slot != WeaponSlotKind.Classic)
                    {
                        continue;
                    }

                    float v = Raw(d, key);
                    if (v <= 0f || (key == Pellets && v <= 1f))
                    {
                        continue;
                    }

                    count++;
                    best = count == 1 ? v : LessIsBetter(key) ? Mathf.Min(best, v) : Mathf.Max(best, v);
                }

                extent[key] = new Vector2(best, count);
            }

            return extent;
        }
    }
}
