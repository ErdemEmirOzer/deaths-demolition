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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Refine = null;
            RefinedCount = 0;
            PondCenters.Clear();
        }
    }
}
