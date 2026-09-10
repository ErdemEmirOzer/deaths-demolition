using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// The three cosmetic skins named in the request. Unlocked from the start, purely
    /// visual, and they never touch a gameplay number.
    /// </summary>
    public enum PlayerSkin
    {
        IvoryCrimson = 0,
        IndigoBlack = 1,
        MossCream = 2
    }

    /// <summary>
    /// PROTOTYPE-ONLY colour palettes. The request names the pairs
    /// (Ivory/Crimson, Indigo/Black, Moss/Cream) but no colour values, so every RGB
    /// here is invented. They are placeholder art direction, not a product decision.
    /// </summary>
    public static class PlayerSkins
    {
        public struct Palette
        {
            /// <summary>English, shown on the selection screen.</summary>
            public string DisplayName;

            /// <summary>Torso and hips.</summary>
            public Color Primary;

            /// <summary>Limbs.</summary>
            public Color Secondary;

            /// <summary>Sash, head band and weapon furniture. The read-at-a-glance colour.</summary>
            public Color Accent;
        }

        public const PlayerSkin Default = PlayerSkin.IvoryCrimson;

        public static readonly Palette[] All =
        {
            new Palette
            {
                DisplayName = "Ivory / Crimson",
                Primary = new Color(0.902f, 0.878f, 0.812f, 1f),
                Secondary = new Color(0.761f, 0.729f, 0.663f, 1f),
                Accent = new Color(0.639f, 0.129f, 0.145f, 1f)
            },
            new Palette
            {
                DisplayName = "Indigo / Black",
                Primary = new Color(0.196f, 0.220f, 0.373f, 1f),
                Secondary = new Color(0.106f, 0.114f, 0.153f, 1f),
                Accent = new Color(0.404f, 0.451f, 0.678f, 1f)
            },
            new Palette
            {
                DisplayName = "Moss / Cream",
                Primary = new Color(0.325f, 0.396f, 0.278f, 1f),
                Secondary = new Color(0.878f, 0.847f, 0.741f, 1f),
                Accent = new Color(0.549f, 0.482f, 0.290f, 1f)
            }
        };

        public static int Count
        {
            get { return All.Length; }
        }

        public static Palette Get(PlayerSkin skin)
        {
            int index = (int)skin;
            if (index < 0 || index >= All.Length)
            {
                index = (int)Default;
            }

            return All[index];
        }

        public static string DisplayName(PlayerSkin skin)
        {
            return Get(skin).DisplayName;
        }
    }
}
