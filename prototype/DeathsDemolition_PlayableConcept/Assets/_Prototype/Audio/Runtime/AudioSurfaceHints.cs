using System;
using System.Collections.Generic;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-021C · ADR-021-1 v2 · The footstep surface, refined by the world's own data.
    ///
    /// <see cref="SurfaceResolver"/> answers per collider (a SurfaceTag on the instance root,
    /// written at scene load by <c>DD.Art.Ap005SurfaceTags</c> from the placement family). The
    /// courtyard ground is ONE collider, though, and AP-005's routes say which strips of it are
    /// paved: the world assembly registers a <see cref="Refine"/> hook that turns the ground's
    /// Dirt into Stone inside a stone route, and nothing else. The audio assembly cannot reference
    /// DD.Art (it references us), hence the delegate. No hook = the collider's answer as before.
    /// </summary>
    public static class AudioSurfaceHints
    {
        /// <summary>(world point, collider's surface) → surface. Registered by the world assembly; null = pass-through.</summary>
        public static Func<Vector3, SurfaceKind, SurfaceKind> Refine;

        /// <summary>Refinements applied (answer differed from the collider's). Test / F1 readout.</summary>
        public static int RefinedCount { get; private set; }

        /// <summary>
        /// WP-058B (WP-038 AP-015P plates): a finer footstep than <see cref="SurfaceKind"/> can carry - gravel field,
        /// cut-stone pavement, shallow water - decided by the world assembly from the plate under the foot.
        /// (collider, point, resolved kind) → sound, or None to fall back to the kind's default clip.
        /// </summary>
        public static Func<Collider, Vector3, SurfaceKind, AudioSoundId> FootstepVariant;

        /// <summary>Footstep sound for a hit: the world's variant when it has one, else the kind's default.</summary>
        public static AudioSoundId Footstep(Collider collider, Vector3 point, SurfaceKind kind, Func<SurfaceKind, AudioSoundId> byKind)
        {
            Func<Collider, Vector3, SurfaceKind, AudioSoundId> variant = FootstepVariant;
            if (variant != null)
            {
                AudioSoundId v = variant(collider, point, kind);
                if (v != AudioSoundId.None)
                {
                    return v;
                }
            }

            return byKind(kind);
        }

        public static SurfaceKind Resolve(Collider collider, Vector3 point)
        {
            SurfaceKind kind = SurfaceResolver.Resolve(collider);
            Func<Vector3, SurfaceKind, SurfaceKind> refine = Refine;
            if (refine == null)
            {
                return kind;
            }

            SurfaceKind refined = refine(point, kind);
            if (refined != kind)
            {
                RefinedCount++;
            }

            return refined;
        }

        /// <summary>
        /// WP-048 (Look B): world points the ambience can anchor 3D beds to, registered by the world
        /// assembly at scene load (the koi pond centroid from the placement families). Empty = no pond bed.
        /// </summary>
        public static readonly List<Vector3> PondCenters = new List<Vector3>(4);

        /// <summary>WP-072 · DEC-102: bamboo grove centres (clusters of bamboo stalk / cluster instances) for the grove bed and gusts.</summary>
        public static readonly List<Vector3> BambooGroves = new List<Vector3>(8);

        /// <summary>WP-065C: AP-017 fūrin wind chimes (eave hang points), registered by the world assembly; the ambience rings the nearest in the wind.</summary>
        public static readonly List<Vector3> WindChimes = new List<Vector3>(16);

        /// <summary>WP-072 · DEC-102 on World v5 (V6): eave-line points (roof edges) for the after-rain eave drips; the fūrin points serve too.</summary>
        public static readonly List<Vector3> EavePoints = new List<Vector3>(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Refine = null;
            FootstepVariant = null;
            RefinedCount = 0;
            PondCenters.Clear();
            WindChimes.Clear();
            BambooGroves.Clear();
            EavePoints.Clear();
        }
    }
}
