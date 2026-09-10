namespace DD.Prototype.Core.Pooling
{
    /// <summary>What happens when the capacity is reached and nothing is free.</summary>
    public enum PoolExhaustedPolicy
    {
        /// <summary>Refuse the request. Get returns null; the caller must handle it.</summary>
        ReturnNull = 0,

        /// <summary>Steal the oldest live instance. Correct for impact FX and damage numbers.</summary>
        RecycleOldest = 1,

        /// <summary>Grow past the capacity and log a warning once. Only for diagnosis.</summary>
        GrowWithWarning = 2
    }

    /// <summary>Non-generic face of a pool, used by <see cref="PooledBehaviour.Despawn"/>.</summary>
    public interface IPrototypePool
    {
        string Name { get; }
        int CountSpawned { get; }
        int CountAvailable { get; }
        int Capacity { get; }
        PoolExhaustedPolicy ExhaustedPolicy { get; }

        void ReleaseObject(PooledBehaviour instance);
        void Prewarm(int count);

        /// <summary>Return every live instance. Called on scene transitions.</summary>
        void ReleaseAllSpawned();

        /// <summary>Destroy every instance, live or free.</summary>
        void DestroyAll();
    }
}
