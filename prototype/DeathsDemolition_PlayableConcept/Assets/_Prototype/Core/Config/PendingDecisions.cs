namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// Canonical keys for the still-open decision lines listed under
    /// "Kararlastirilmasi sonraya birakilan alanlar" in planning/00-PROJECT-PLAN.md.
    ///
    /// The Turkish strings are quoted verbatim on purpose: they are the traceability
    /// key back into the planning document and must NOT be translated or reworded.
    /// <see cref="ToEnglishLabel"/> supplies the short English caption used by any
    /// in-game surface (F1 debug panel), so the game itself stays English-only.
    /// </summary>
    public static class PendingDecisions
    {
        /// planning/00-PROJECT-PLAN.md line 111
        public const string CombatScaling = "Kesin can, hasar, zırh ve round ölçeklendirme değerleri";

        /// planning/00-PROJECT-PLAN.md line 112
        public const string ExplosionShares = "Patlama hasarının bölgelere dağılım oranları";

        /// planning/00-PROJECT-PLAN.md line 113
        public const string ProfileXp = "Profil XP değerleri, formülü ve seviye eğrisi";

        /// planning/00-PROJECT-PLAN.md line 114
        public const string UpgradesAndGold =
            "Geliştirme seçenekleri, paternleri ve gold ekonomisinin sayısal ayrıntıları";

        /// planning/00-PROJECT-PLAN.md line 115
        public const string SpecialWeapon = "Special Weapon türleri, çıkacağı roundlar ve süre değerleri";

        /// planning/00-PROJECT-PLAN.md line 116
        public const string HealthRegen =
            "Otomatik can yenilenmesinin hasarsız bekleme süresi, yenilenme hızı ve sınırı";

        /// <summary>
        /// Used for prototype values that occupy no pending planning line at all
        /// (camera framing, spawn safety geometry, feel tuning). They are still
        /// PROTOTYPE-ONLY, they simply do not pre-empt an open product decision.
        /// </summary>
        public const string None = "PROTOTYPE-ONLY / no pending planning decision";

        /// <summary>
        /// Short English caption for the in-game debug panel.
        /// </summary>
        public static string ToEnglishLabel(string pendingDecision)
        {
            switch (pendingDecision)
            {
                case CombatScaling: return "Health / damage / armor / round scaling values";
                case ExplosionShares: return "Explosion damage share per body region";
                case ProfileXp: return "Profile XP values, formula and level curve";
                case UpgradesAndGold: return "Upgrade options, patterns and gold economy numbers";
                case SpecialWeapon: return "Special Weapon types, rounds and durations";
                case HealthRegen: return "Health regen delay, rate and cap";
                case None: return "No pending planning decision";
                default: return pendingDecision;
            }
        }

        /// <summary>All pending-decision keys this prototype is allowed to occupy.</summary>
        public static readonly string[] All =
        {
            CombatScaling,
            ExplosionShares,
            ProfileXp,
            UpgradesAndGold,
            SpecialWeapon,
            HealthRegen,
            None
        };
    }
}
