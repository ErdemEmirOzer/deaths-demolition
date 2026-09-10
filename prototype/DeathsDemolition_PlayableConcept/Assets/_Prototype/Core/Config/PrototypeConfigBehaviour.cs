using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// Base class for anything that caches values out of <see cref="PrototypeConfig"/>.
    ///
    /// Contract for every downstream system:
    ///   1. Derive from this instead of MonoBehaviour.
    ///   2. Put all config reads inside ApplyConfig.
    ///   3. Recompute derived state (timers, max health, spawn schedules) there too.
    ///
    /// ApplyConfig is called once on enable and again on every live edit, so a value
    /// changed in the Inspector or in the F1 panel mid-run lands immediately.
    ///
    /// If a subclass overrides OnEnable / OnDisable it MUST call base.
    /// </summary>
    public abstract class PrototypeConfigBehaviour : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use PrototypeConfig.Active.")]
        [SerializeField] private PrototypeConfig _configOverride;

        private PrototypeConfig _bound;

        /// <summary>The config this behaviour is currently bound to. Never cache this across frames.</summary>
        protected PrototypeConfig Config => _bound;

        /// <summary>True once ApplyConfig has run at least one time.</summary>
        protected bool HasConfig => _bound != null;

        /// <summary>The asset assigned in the Inspector, or null when the active one is used.</summary>
        protected PrototypeConfig ConfigOverride => _configOverride;

        protected virtual void OnEnable()
        {
            Bind(_configOverride != null ? _configOverride : PrototypeConfig.Active);
            PrototypeConfig.ActiveChanged += OnActiveConfigChanged;
        }

        protected virtual void OnDisable()
        {
            PrototypeConfig.ActiveChanged -= OnActiveConfigChanged;
            Unbind();
        }

        /// <summary>
        /// Re-read everything this behaviour needs from the config. Called on enable
        /// and on every live change. Must be safe to call many times per second.
        /// </summary>
        protected abstract void ApplyConfig(PrototypeConfig config);

        /// <summary>Force a rebind, e.g. after assigning a different override at runtime.</summary>
        protected void RebindConfig(PrototypeConfig config)
        {
            Unbind();
            Bind(config);
        }

        private void OnActiveConfigChanged(PrototypeConfig config)
        {
            if (_configOverride != null)
            {
                return;
            }

            RebindConfig(config);
        }

        private void Bind(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            _bound = config;
            _bound.Changed += OnConfigChanged;
            ApplyConfig(_bound);
        }

        private void Unbind()
        {
            if (_bound == null)
            {
                return;
            }

            _bound.Changed -= OnConfigChanged;
            _bound = null;
        }

        private void OnConfigChanged(PrototypeConfig config)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            ApplyConfig(config);
        }
    }

    /// <summary>
    /// Allocation-free alternative for consumers that are not MonoBehaviours, or that
    /// live inside a hot loop and would rather poll than hold an event subscription.
    ///
    ///     private ConfigRevisionWatcher _watcher;
    ///     ...
    ///     if (_watcher.Consume(config)) { RecomputeCachedValues(config); }
    /// </summary>
    public struct ConfigRevisionWatcher
    {
        private int _seenRevision;
        private bool _primed;

        /// <summary>
        /// Returns true exactly once per config change (and once on the first call),
        /// which is the signal to recompute cached values. Allocates nothing.
        /// </summary>
        public bool Consume(PrototypeConfig config)
        {
            if (config == null)
            {
                return false;
            }

            int revision = config.Revision;
            if (_primed && revision == _seenRevision)
            {
                return false;
            }

            _seenRevision = revision;
            _primed = true;
            return true;
        }
    }
}
