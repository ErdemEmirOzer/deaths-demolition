using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 ADR-069-1 · advances <see cref="ProjectileSystem"/> once a frame (after gameplay
    /// Updates, so a shot fired this frame and the bodies it flies at are both current) and draws
    /// the INTERIM trail: one additive streak per live projectile, length = speed × 0.020 s
    /// (S-07 WP-073, clamped 1.2–8 m and to the distance flown). S-07 WP-073 replaces the
    /// drawing with the real trail/tracer set reading the same state; the simulation stays here.
    /// Created on the first spawn, survives scene loads, holds no gameplay state of its own.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class ProjectileDriver : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "WP-069 interim trail: seconds of flight shown as the streak (S-07 WP-073 Δ: L = v × 0.020 s).", true)]
        public const float StreakSeconds = 0.020f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 interim trail: streak length clamp, metres (S-07 WP-073: 1.2–8 m).", true)]
        public const float StreakMin = 1.2f, StreakMax = 8f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 interim trail: streak width, metres (≥ 2 px at the top-down camera, AD §3.3).", true)]
        public const float StreakWidth = 0.07f;

        private static ProjectileDriver s_instance;

        /// <summary>When false the interim streaks are not drawn (S-07's renderer took over, or tests).</summary>
        public static bool DrawInterimTrails { get; set; } = true;

        private Mesh _mesh;
        private Material _material;

        public static void EnsureExists()
        {
            if (s_instance != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("ProjectileDriver");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ProjectileDriver>();
        }

        private void Awake()
        {
            _mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _material = FeedbackMaterials.Additive(new Color(2.2f, 1.45f, 0.6f, 1f));
        }

        private void Update()
        {
            ProjectileSystem.Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (!DrawInterimTrails || _mesh == null || _material == null)
            {
                return;
            }

            int n = ProjectileSystem.LiveCount;
            for (int i = 0; i < n; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                float speed = p.Speed;
                float length = Mathf.Clamp(speed * StreakSeconds, StreakMin, StreakMax);
                length = Mathf.Min(length, p.Travelled + 0.3f);
                if (length <= 0.05f)
                {
                    continue;
                }

                Vector3 direction = p.Direction;
                Vector3 centre = p.Position - direction * (length * 0.5f);
                float width = p.Kind == ProjectileKind.Pellet ? StreakWidth * 0.7f : StreakWidth;
                Matrix4x4 matrix = Matrix4x4.TRS(centre, Quaternion.LookRotation(direction, Vector3.up), new Vector3(width, width, length));
                Graphics.DrawMesh(_mesh, matrix, _material, 0);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            DrawInterimTrails = true;
        }
    }
}
