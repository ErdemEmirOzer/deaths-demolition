namespace DD.Prototype.Profile
{
    public static partial class ProfileService
    {
        /// <summary>
        /// WP-104 (C-00 11.10 decision 2): Ash the v5 → v6 respec gave back and the player has not been shown yet (0 = none).
        /// S-09 shows it once ("AWAKENING +N Ash" / "UYANIŞ +N Kül") and then calls <see cref="AcknowledgeAshRefundNotice"/>.
        /// </summary>
        public static int PendingAshRefundNotice
        {
            get
            {
                EnsureLoaded();
                return s_data.PendingAshRefundNotice;
            }
        }

        /// <summary>
        /// WP-104: the refund notice was shown — clear it. The same atomic path as a meta command (candidate → validate →
        /// save → publish), so a failed write leaves RAM = disk and the notice comes back next launch. Idempotent.
        /// </summary>
        public static MetaCommandResult AcknowledgeAshRefundNotice()
        {
            EnsureLoaded();
            if (s_data.PendingAshRefundNotice == 0) return MetaCommandResult.Done;
            if (ProfileStore.SaveLocked) return MetaCommandResult.SaveLocked;
            ProfileData candidate = s_data.Clone();
            candidate.PendingAshRefundNotice = 0;
            return CommitMeta(candidate);
        }
    }
}
