using System;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>WP-016 · Which character visuals the run uses.</summary>
    public enum CharacterVisualSource
    {
        /// <summary>The M1 primitive rigs (fallback and A/B reference).</summary>
        Procedural = 0,
        /// <summary>The AP-006 player and AP-007 zombie skinned meshes (Import Contract v1.1).</summary>
        Skinned = 1
    }

    /// <summary>
    /// WP-016 · The character art the prototype binds at spawn, as one asset in Resources
    /// (<c>Assets/_Art/Resources/DD_Characters.asset</c>) written by the art pipeline
    /// (<c>ArtCharacterPrefabBuilder</c>) and read by <c>PlayerRigFactory</c> and
    /// <c>EnemyFactory</c>. Same shape as <c>WorldSourceSettings</c>: DD.Prototype never
    /// references DD.Art; the pipeline fills references the prototype types declare.
    /// A command-line flag (<c>-ddCharacters Procedural|Skinned</c>) or the
    /// <c>DD_CHARACTERS</c> environment variable overrides the asset for A/B runs; with the
    /// asset absent or the source Procedural every factory keeps its M1 primitive body.
    /// </summary>
    public sealed class CharacterAssetSet : ScriptableObject
    {
        public const string ResourceName = "DD_Characters";
        public const string CommandLineFlag = "-ddCharacters";
        public const string EnvironmentVariable = "DD_CHARACTERS";

        [SerializeField] private CharacterVisualSource _source = CharacterVisualSource.Skinned;

        [Header("Player (AP-006)")]
        [Tooltip("Player_AP006 prefab: PlayerSkinnedVisual root → model (Animator + LODGroup + skinned LOD0/1/2).")]
        [SerializeField] private GameObject _playerPrefab;
        [Tooltip("Albedo per PlayerSkin index (0 Ivory/Crimson, 1 Indigo/Black = 2a, 2 Moss/Cream). Skin = albedo swap only.")]
        [SerializeField] private Texture2D[] _playerSkinAlbedos = new Texture2D[3];
        [SerializeField] private CharacterAnimationSet _playerAnimation;

        [Header("Zombie (AP-007)")]
        [Tooltip("Zombie_AP007 prefab: ZombieSkinnedVisual root → model (Animator + LODGroup + 4 LOD × 4 slot skinned renderers).")]
        [SerializeField] private GameObject _zombiePrefab;
        [SerializeField] private ZombieModuleLibrary _zombieLibrary;
        [SerializeField] private CharacterAnimationSet _zombieAnimation;

        public CharacterVisualSource Source => _source;
        public GameObject PlayerPrefab => _playerPrefab;
        public Texture2D[] PlayerSkinAlbedos => _playerSkinAlbedos;
        public CharacterAnimationSet PlayerAnimation => _playerAnimation;
        public GameObject ZombiePrefab => _zombiePrefab;
        public ZombieModuleLibrary ZombieLibrary => _zombieLibrary;
        public CharacterAnimationSet ZombieAnimation => _zombieAnimation;

        public bool PlayerReady => _playerPrefab != null && _playerSkinAlbedos != null && _playerSkinAlbedos.Length >= 3 && _playerAnimation != null;
        public bool ZombieReady => _zombiePrefab != null && _zombieLibrary != null && _zombieAnimation != null;

        private static CharacterAssetSet s_cached;
        private static bool s_loaded;
        private static bool s_logged;

        /// <summary>The Resources asset (cached; null when absent).</summary>
        public static CharacterAssetSet Load()
        {
            if (!s_loaded)
            {
                s_cached = Resources.Load<CharacterAssetSet>(ResourceName);
                s_loaded = true;
            }

            return s_cached;
        }

        /// <summary>The effective source: flag/env override, else the asset, else Procedural. Logged once.</summary>
        public static CharacterVisualSource Resolve()
        {
            CharacterAssetSet set = Load();
            CharacterVisualSource source = set != null ? set.Source : CharacterVisualSource.Procedural;
            string origin = set != null ? "asset" : "default (no " + ResourceName + " asset)";
            string forced = Arg(CommandLineFlag);
            if (string.IsNullOrEmpty(forced))
            {
                forced = Environment.GetEnvironmentVariable(EnvironmentVariable);
            }

            if (!string.IsNullOrEmpty(forced) && Enum.TryParse(forced, true, out CharacterVisualSource parsed))
            {
                source = parsed;
                origin = "override '" + forced + "'";
            }

            if (!s_logged)
            {
                s_logged = true;
                Debug.Log("[Characters] visual source = " + source + " (" + origin + ")");
            }

            return source;
        }

        /// <summary>True when the skinned player can be built (source Skinned and the player references are set).</summary>
        public static bool UseSkinnedPlayer()
        {
            CharacterAssetSet set = Load();
            return Resolve() == CharacterVisualSource.Skinned && set != null && set.PlayerReady;
        }

        public static bool UseSkinnedZombie()
        {
            CharacterAssetSet set = Load();
            return Resolve() == CharacterVisualSource.Skinned && set != null && set.ZombieReady;
        }

        /// <summary>Tests: drop the cache so a freshly written asset or flag is read again.</summary>
        public static void ResetCache()
        {
            s_cached = null;
            s_loaded = false;
            s_logged = false;
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
