using UnityEngine;

namespace DD.Prototype.Core.Pooling
{
    /// <summary>
    /// Base for every pooled object: enemies, projectiles, impact effects, gold orbs,
    /// damage numbers.
    ///
    /// The owning pool is written into the instance ONCE, at Instantiate time. That is
    /// the whole trick behind the "no GetComponent, no Find in a crowd" rule: releasing
    /// an object never has to look anything up, it just follows the cached reference.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class PooledBehaviour : MonoBehaviour
    {
        internal IPrototypePool OwnerPool;

        /// <summary>True while the object is checked out of its pool.</summary>
        public bool IsSpawned { get; internal set; }

        /// <summary>True when this instance came from a pool rather than a plain Instantiate.</summary>
        public bool IsPooled => OwnerPool != null;

        /// <summary>
        /// Give the object back to its pool. Safe to call twice; safe to call on an
        /// instance that was never pooled (it just deactivates).
        /// </summary>
        public void Despawn()
        {
            if (OwnerPool != null)
            {
                OwnerPool.ReleaseObject(this);
                return;
            }

            if (this != null)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Reset all per-life state here: health, timers, trails, particles, velocity.
        /// Called after the transform is placed and the object is activated.
        /// </summary>
        protected virtual void OnSpawned()
        {
        }

        /// <summary>
        /// Release per-life references here. Called before the object is deactivated.
        /// Must not despawn other objects synchronously.
        /// </summary>
        protected virtual void OnDespawned()
        {
        }

        internal void InvokeSpawned()
        {
            IsSpawned = true;
            OnSpawned();
        }

        internal void InvokeDespawned()
        {
            IsSpawned = false;
            OnDespawned();
        }
    }
}
