using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Optional authoring asset for the spawn layout.
    ///
    /// The spawn director works with no asset at all - it falls back to
    /// <see cref="DefaultSpawnPointLayout"/>. Create one only when the map task wants to
    /// re-author positions or base weights without touching code:
    ///
    ///     Create > Deaths Demolition > Spawn Point Layout
    ///     right-click the asset header > "Reset To Default Layout"
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnPointLayout", menuName = "Deaths Demolition/Spawn Point Layout", order = 10)]
    public sealed class SpawnPointLayoutAsset : ScriptableObject
    {
        [SerializeField] private List<SpawnPointDefinition> _points = new List<SpawnPointDefinition>();

        public IReadOnlyList<SpawnPointDefinition> Points => _points;

        public int Count => _points.Count;

        [ContextMenu("Reset To Default Layout")]
        public void ResetToDefaultLayout()
        {
            DefaultSpawnPointLayout.Fill(_points);
        }

        private void Reset()
        {
            ResetToDefaultLayout();
        }
    }
}
