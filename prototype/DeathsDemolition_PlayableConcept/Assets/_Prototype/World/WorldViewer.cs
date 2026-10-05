using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// WP-081 · the world as a view only (the main menu's cinematic backdrop, the user's G5 addition). While
    /// <see cref="Active"/> is set, an authored world scene that loads builds its look - atmosphere, sky, fog, the layout -
    /// and nothing of the run: no markers, no NavMesh verification, no run systems, no HUD, no real-time planar
    /// reflection (AP-028 v002 menu budget: baked probes only). The menu sets it before it loads the world additively
    /// and clears it when it goes; the run never sees it set.
    /// </summary>
    public static class WorldViewer
    {
        /// <summary>True while a menu backdrop owns the world scene being loaded or shown.</summary>
        public static bool Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = false;
        }
    }
}
