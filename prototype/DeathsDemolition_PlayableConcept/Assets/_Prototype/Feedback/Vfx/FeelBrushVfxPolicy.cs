using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-093 Faz2: AP-037 v001.1 spatial and temporal contract; no gameplay state.</summary>
    public static class FeelBrushVfxPolicy
    {
        public const int DropletCapacity = 12, ExplosionCapacity = 8;
        public const int KillWindowCapacity = 32;
        public const float KillWindowSeconds = .6f;
        public const float StainInterval = .25f, KillSeconds = .25f;
        public const float MultiSeconds = .30f, ShockGrowSeconds = .18f, ShockSeconds = .25f;
        public const float MultiCenterDiameterUv = .840f, ShockOuterDiameterUv = .829f;
        public static readonly Color BloodTint = new Color(94f / 255f, 26f / 255f, 22f / 255f);
        public static readonly Color CriticalTint = new Color(196f / 255f, 54f / 255f, 43f / 255f);
        public static readonly Color InkTint = new Color(28f / 255f, 26f / 255f, 31f / 255f);
        public static readonly Color DustTint = new Color(158f / 255f, 140f / 255f, 122f / 255f);
        public static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float MultiRadius(int tier) => tier <= 1 ? 1.5f : tier == 2 ? 2.5f : 3.5f;
        public static float MultiQuadMetres(int tier) => 2f * MultiRadius(tier) / MultiCenterDiameterUv;
        public static float ShockQuadMetres(float radius) => 2f * 1.2f * Mathf.Max(.05f, radius) / ShockOuterDiameterUv;
        public static int KillFrame(float normalizedAge) => Mathf.Clamp(1 + Mathf.FloorToInt(Mathf.Clamp01(normalizedAge) * 15f), 1, 15);
        public static Vector4 AtlasUv(int frame, int columns, int rows)
        {
            columns = Mathf.Max(1, columns); rows = Mathf.Max(1, rows);
            frame = Mathf.Clamp(frame, 0, columns * rows - 1);
            return new Vector4(1f / columns, 1f / rows, (frame % columns) / (float)columns,
                1f - (frame / columns + 1f) / rows);
        }
        public static Vector3 FlatDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }
        public static Quaternion GroundRotation(Vector3 direction, Vector3 normal)
        {
            Vector3 right = Vector3.ProjectOnPlane(FlatDirection(direction), normal);
            if (right.sqrMagnitude < .0001f) right = Vector3.ProjectOnPlane(Vector3.right, normal);
            return Quaternion.LookRotation(normal, Vector3.Cross(normal, right.normalized));
        }
    }
    /// <summary>Fixed storage matching FeelEvents' 32 kill chain slots; no allocation after construction.</summary>
    public sealed class FeelBrushKillWindow
    {
        private struct Sample { public Vector3 Position; public int GroupId; public float At; }
        private readonly Sample[] _samples = new Sample[FeelBrushVfxPolicy.KillWindowCapacity];
        private int _next;
        public int Count { get; private set; }
        public void Record(Vector3 position, int groupId, float now)
        {
            // FeelEvents expires a trigger group after an idle gap, not from its first kill.
            float last = float.NegativeInfinity;
            if (groupId != 0)
            {
                for (int i = 0; i < Count; i++)
                    if (_samples[i].GroupId == groupId) last = Mathf.Max(last, _samples[i].At);
                if (now - last > FeelBrushVfxPolicy.KillWindowSeconds)
                    for (int i = 0; i < Count; i++)
                        if (_samples[i].GroupId == groupId) _samples[i].GroupId = 0;
            }
            _samples[_next] = new Sample { Position = position, GroupId = groupId, At = now };
            _next = (_next + 1) % _samples.Length;
            Count = Mathf.Min(Count + 1, _samples.Length);
        }
        public Vector3 Center(float now, bool sameGroup, int groupId, Vector3 fallback)
        {
            Vector3 sum = Vector3.zero; int count = 0;
            for (int i = 0; i < Count; i++)
            {
                Sample sample = _samples[i]; float age = now - sample.At;
                if (age < 0f || (!sameGroup && age > FeelBrushVfxPolicy.KillWindowSeconds)) continue;
                if (sameGroup && (groupId == 0 || sample.GroupId != groupId)) continue;
                sum += sample.Position; count++;
            }
            return count > 0 ? sum / count : fallback;
        }
        public void Clear() { Count = 0; _next = 0; }
    }

}
