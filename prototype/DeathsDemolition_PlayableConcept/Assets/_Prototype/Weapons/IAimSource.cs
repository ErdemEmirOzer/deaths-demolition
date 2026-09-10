using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Where the weapon points.
    ///
    /// The player task owns aiming; the weapon must not re-derive it from the mouse, or
    /// the two would drift apart by a frame and the tracer would leave the barrel at a
    /// different angle than the character faces.
    ///
    /// Implementing this is optional. With nothing assigned, <see cref="WeaponController"/>
    /// falls back to its own transform's forward, which is correct by construction if the
    /// controller sits on the character's aim pivot - and the request already requires the
    /// character to face the aim direction at all times.
    /// </summary>
    public interface IAimSource
    {
        /// <summary>World-space direction the shot should travel. Need not be normalised.</summary>
        Vector3 AimDirection { get; }

        /// <summary>World-space point shots originate from. Usually the muzzle.</summary>
        Vector3 MuzzlePosition { get; }

        /// <summary>False to suppress firing entirely - cutscene, death, menu.</summary>
        bool CanFire { get; }
    }

    /// <summary>
    /// The character the weapon is mounted on, from the weapon's point of view.
    ///
    /// This is the second half of the G-02 hand-off and the reason
    /// <see cref="WeaponController"/> still contains no reference to any player type: the
    /// weapon says "I fired, this hard, in this direction" and something else decides
    /// what the character's arms do about it. <c>PlayerWeaponBinding</c> is the shipped
    /// implementation; a test double or a turret would implement the same three calls.
    ///
    /// Every member is optional in the sense that the controller null-checks the host and
    /// keeps firing without one.
    /// </summary>
    public interface IWeaponHost
    {
        /// <summary>Where a weapon model should be parented. Null to keep the weapon's own rig root.</summary>
        Transform WeaponMount { get; }

        /// <summary>
        /// One trigger pull happened. <paramref name="strength01"/> is on the same scale
        /// the character animator expects - roughly 0.25 for an SMG and 1 for a shotgun.
        /// <paramref name="worldDirection"/> is where the shot went; the kick is applied
        /// against it.
        /// </summary>
        void OnWeaponFired(float strength01, Vector3 worldDirection);

        /// <summary>A reload started and will take this many seconds.</summary>
        void OnReloadStarted(float duration);

        /// <summary>
        /// A reload stopped. <paramref name="completed"/> is false when it was cut short -
        /// the Special Weapon arriving mid-reload is the case that matters (REQ-055).
        /// </summary>
        void OnReloadFinished(bool completed);
    }
}
