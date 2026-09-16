using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032 · ADR-032-1 · Measures a locomotion clip's honest no-slip ground speed ON THE UNITY
    /// RIG: the manifest's <c>speed_mps</c> was measured in Blender, and Humanoid retargeting moves
    /// the feet a few percent differently (shamble_A: 1.464 → ≈1.36 m/s measured, i.e. 7 % skating
    /// at the manifest rate). The clip is sampled frame by frame (no playback); between two
    /// authored contact events (foot_l / foot_r) the planted foot is the one travelling backward,
    /// and its mean backward speed is the speed the body must move at rate 1 for zero slip.
    /// </summary>
    public static class StrideCalibrator
    {
        public struct Result
        {
            public float speedMps;
            public int windows;
            public string report;
        }

        /// <summary>
        /// <paramref name="root"/> is the model root the clip is sampled on (Animator with the
        /// character's avatar); <paramref name="eventsText"/> is the manifest "name=frame;…" string.
        /// Returns 0 when the clip has fewer than two contact events.
        /// </summary>
        public static Result Measure(GameObject root, Animator animator, AnimationClip clip, int frames, int fps, string eventsText)
        {
            return Measure(root, animator, clip, frames, fps, eventsText, FootTravelDirection(clip != null ? clip.name : ""));
        }

        /// <summary>
        /// The direction the PLANTED foot travels in model space for a clip (opposite to the body's travel):
        /// walk_back → +Z, strafe_left → +X (body goes −X), strafe_right → −X, everything else → −Z.
        /// </summary>
        public static Vector3 FootTravelDirection(string clipName)
        {
            if (clipName.Contains("walk_back")) return Vector3.forward;
            if (clipName.Contains("strafe_left")) return Vector3.right;
            if (clipName.Contains("strafe_right")) return Vector3.left;
            return Vector3.back;
        }

        public static Result Measure(GameObject root, Animator animator, AnimationClip clip, int frames, int fps, string eventsText, Vector3 footTravel)
        {
            var result = new Result();
            if (root == null || animator == null || clip == null || frames < 2) return result;
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lf == null || rf == null) return result;

            var contacts = new List<int>();
            int l = ZombieSkinnedVisual.FrameOf(eventsText ?? "", "foot_l");
            int r = ZombieSkinnedVisual.FrameOf(eventsText ?? "", "foot_r");
            if (l >= 0) contacts.Add(l);
            if (r >= 0) contacts.Add(r);
            contacts.Sort();
            if (contacts.Count < 2) return result;
            int cycle = frames - 1;   // loop clips: frame n == frame 0

            // Sample every frame once (local foot position projected on the planted-foot travel axis).
            var lz = new float[cycle + 1];
            var rz = new float[cycle + 1];
            Vector3 axis = footTravel.normalized;
            for (int f = 0; f <= cycle; f++)
            {
                clip.SampleAnimation(root, f / (float)fps);
                lz[f] = Vector3.Dot(root.transform.InverseTransformPoint(lf.position), axis);
                rz[f] = Vector3.Dot(root.transform.InverseTransformPoint(rf.position), axis);
            }

            float sum = 0f;
            int windows = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < contacts.Count; i++)
            {
                int from = contacts[i];
                int to = i + 1 < contacts.Count ? contacts[i + 1] : contacts[0] + cycle;
                float dl = 0f, dr = 0f;
                int n = 0;
                for (int f = from; f < to; f++)
                {
                    int a = f % cycle, b = (f + 1) % cycle;
                    dl += lz[b] - lz[a];
                    dr += rz[b] - rz[a];
                    n++;
                }

                if (n == 0) continue;
                // planted = the foot that travelled the most ALONG the planted-foot axis over the window
                float planted = Mathf.Max(dl, dr);
                float speed = planted / n * fps;
                sb.Append(from).Append('→').Append(to).Append(':').Append(speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(dl >= dr ? "L " : "R ");
                sum += speed;
                windows++;
            }

            result.speedMps = windows > 0 ? sum / windows : 0f;
            result.windows = windows;
            result.report = sb.ToString();
            return result;
        }
    }
}
