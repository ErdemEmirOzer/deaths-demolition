using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-024: authored DEFAULTS for <see cref="InputPreferences"/> (deadzone, rumble,
    /// aim assist), one asset at Assets/_Prototype/Input/Resources/DD_InputSettings.
    /// The player's own values are persisted separately by
    /// <see cref="InputPreferencesStore"/>; "reset to defaults" comes back here. The
    /// S-09 settings screen reads the ranges from <see cref="InputPreferences"/> and
    /// the defaults from this asset. Missing asset = code defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "DD_InputSettings", menuName = "Deaths Demolition/Input Settings")]
    public sealed class InputSettingsAsset : ScriptableObject
    {
        public const string ResourcePath = "DD_InputSettings";

        [SerializeField] private InputPreferences _defaults = new InputPreferences();

        public InputPreferences Defaults => _defaults;

        /// <summary>A fresh copy of the authored defaults (clamped), or code defaults when no asset exists.</summary>
        public static InputPreferences LoadDefaults()
        {
            InputSettingsAsset asset = Resources.Load<InputSettingsAsset>(ResourcePath);
            if (asset == null || asset._defaults == null)
            {
                return new InputPreferences();
            }

            return asset._defaults.Copy().Clamp();
        }
    }
}
