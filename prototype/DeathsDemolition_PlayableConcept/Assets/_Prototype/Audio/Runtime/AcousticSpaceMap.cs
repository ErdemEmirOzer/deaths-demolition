using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 · WP-074: S-05's acoustic spaces (acoustic-spaces-v6.json, baked by AudioCatalogBuilder into
    /// <c>Resources/DD_AcousticSpaces.asset</c>). Rooms are Hall, corridors Corridor, everything else Open. The
    /// coordinates belong to the V6 world only, so the map answers while that scene is loaded; elsewhere (AP005)
    /// every point is Open. A 1 m hysteresis keeps the current space at a threshold (STANDARDS §5).
    /// </summary>
    public sealed class AcousticSpaceMap : ScriptableObject
    {
        public const string ResourceName = "DD_AcousticSpaces";
        public const float HysteresisMeters = 1f;
        private const float VerticalSlack = 0.5f;

        [Serializable]
        public sealed class Space
        {
            public string id;
            public AcousticSpace enclosure;
            public float[] polygonXZ;
            public float floor;
            public float ceiling;
        }

        [SerializeField] private string _sourceHash = "";
        [SerializeField] private string _worldScene = "World_V6_Greybox";
        [SerializeField] private List<Space> _spaces = new List<Space>();

        [NonSerialized] private int _current = -1;
        [NonSerialized] private int _sceneFrame = -1;
        [NonSerialized] private bool _sceneLoaded;

        public string SourceHash => _sourceHash;
        public string WorldScene => _worldScene;
        public IReadOnlyList<Space> Spaces => _spaces;

        public static AcousticSpaceMap Load() => Resources.Load<AcousticSpaceMap>(ResourceName);

        public void Set(string sourceHash, string worldScene, List<Space> spaces)
        {
            _sourceHash = sourceHash;
            _worldScene = worldScene;
            _spaces = spaces;
            _current = -1;
        }

        /// <summary>The runtime resolver: Open unless the map's world scene is loaded.</summary>
        public AcousticSpace Resolve(Vector3 p)
        {
            if (_sceneFrame != Time.frameCount)
            {
                _sceneFrame = Time.frameCount;
                _sceneLoaded = SceneManager.GetSceneByName(_worldScene).isLoaded;
            }

            return _sceneLoaded ? ResolveInMap(p) : AcousticSpace.Open;
        }

        /// <summary>The map alone (tests, tools): sticky to the current space within <see cref="HysteresisMeters"/>.</summary>
        public AcousticSpace ResolveInMap(Vector3 p)
        {
            if (_current >= 0 && _current < _spaces.Count)
            {
                Space c = _spaces[_current];
                if (InBand(c, p) && (AudioRegionMap.Contains(c.polygonXZ, p.x, p.z) || EdgeDistance(c.polygonXZ, p.x, p.z) < HysteresisMeters))
                {
                    return c.enclosure;
                }
            }

            _current = -1;
            for (int i = 0; i < _spaces.Count; i++)
            {
                Space s = _spaces[i];
                if (InBand(s, p) && AudioRegionMap.Contains(s.polygonXZ, p.x, p.z))
                {
                    _current = i;
                    return s.enclosure;
                }
            }

            return AcousticSpace.Open;
        }

        /// <summary>Forget the current space and the per-frame scene check (tests: EditMode frames do not advance).</summary>
        public void ResetHysteresis()
        {
            _current = -1;
            _sceneFrame = -1;
        }

        private static bool InBand(Space s, Vector3 p) => p.y >= s.floor - VerticalSlack && p.y <= s.ceiling + VerticalSlack;

        private static float EdgeDistance(float[] xz, float px, float pz)
        {
            int n = xz.Length / 2;
            float best = float.MaxValue;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float ax = xz[j * 2], az = xz[j * 2 + 1], bx = xz[i * 2], bz = xz[i * 2 + 1];
                float dx = bx - ax, dz = bz - az;
                float len = dx * dx + dz * dz;
                float t = len > 1e-6f ? Mathf.Clamp01(((px - ax) * dx + (pz - az) * dz) / len) : 0f;
                float qx = ax + t * dx - px, qz = az + t * dz - pz;
                best = Mathf.Min(best, Mathf.Sqrt(qx * qx + qz * qz));
            }

            return best;
        }
    }
}
