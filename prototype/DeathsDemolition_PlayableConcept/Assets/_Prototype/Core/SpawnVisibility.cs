namespace DD.Prototype.Core
{
    /// <summary>
    /// WP-040 · DEC-081 (user decision 2026-09-17): the camera-visibility class of an enemy spawn,
    /// decided once by the spawn director at spawn time and read by the enemy's rise policy
    /// (rise from the ground only where the camera cannot see the pop-in it hides). Lives in
    /// Core so Spawn and Enemies share it without either referencing the other.
    /// </summary>
    public enum SpawnVisibility
    {
        /// <summary>No camera to judge by (headless test, debug spawn). The rise policy treats it as "do not rise".</summary>
        Unknown = 0,
        /// <summary>Inside the view (with the margin band) — no rise; the body walks at once.</summary>
        OnScreen = 1,
        /// <summary>Clearly outside the view (behind the camera or past the margin band): the body may rise.</summary>
        OffScreen = 2,
    }
}
