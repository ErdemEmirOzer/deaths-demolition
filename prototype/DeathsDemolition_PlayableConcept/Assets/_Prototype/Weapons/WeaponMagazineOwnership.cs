using System;
using DD.Prototype.Characters;

namespace DD.Prototype.Weapons
{
    /// <summary>WP-082: where a magazine weapon's magazine is during a reload.</summary>
    public enum MagazineOwner
    {
        Weapon = 0,
        HandOld = 1,   // the spent magazine, out of the well
        HandNew = 2    // the fresh magazine, from the pouch
    }

    /// <summary>WP-082: the four reload beats, in order.</summary>
    public enum MagazineStep
    {
        Detach = 0,       // weapon → hand (the spent one)
        HandAttach = 1,   // the spent one drops, the fresh one is in the hand
        Insert = 2,       // hand → weapon
        BoltRelease = 3   // chambered; the magazine stays in the weapon
    }

    /// <summary>
    /// WP-082 reload magazine contract v1 (S-02 with S-06, C-00 05.10 · K/wp-069b5/wp082-sarjor-sozlesme-v1.md): who holds the
    /// magazine while a magazine weapon reloads — weapon → hand (spent) → hand (fresh) → weapon. The clip's events drive it
    /// (mag_detach / mag_hand_attach / mag_insert / bolt_release; the AP-009C names mag_out / mag_in / chamber are aliases);
    /// a beat the clip does not carry lands at the reload's timeline mark plus <see cref="DeadlineGraceSeconds"/>; the end of
    /// the reload — complete, cancelled, a weapon switch, a Special, death, a camera change — puts it back in the weapon at once,
    /// so it is never left in a hand or in the air. Forward only: a repeated beat is ignored, a later one lands the skipped
    /// ones first. Presentation only — the ammo stays on ClassicWeaponRuntime's timer. Pure state, no allocation per frame.
    /// </summary>
    public sealed class WeaponMagazineOwnership
    {
        /// <summary>A beat the clip has not sent lands this long after its timeline mark.</summary>
        public const float DeadlineGraceSeconds = 0.10f;

        /// <summary>Reload progress marks of the four beats for a magazine weapon (the 45-frame reference: 12 / 21 / 30 / 40).</summary>
        public static readonly float[] MagazineMarks = { 12f / 45f, 21f / 45f, 30f / 45f, 40f / 45f };

        public const int StepCount = 4;

        /// <summary>
        /// Clip event names per step: the contract's name first, then the AP-009C alias; rl_1's AP-026-B reload names its round
        /// <c>rocket_in</c> (→ Insert, the tube's MagIn) and <c>rocket_seat</c> (→ BoltRelease, its Chamber) (C-00 05.10, §12).
        /// </summary>
        public static readonly string[][] EventNames =
        {
            new[] { "mag_detach", "mag_out" },
            new[] { "mag_hand_attach" },
            new[] { "mag_insert", "mag_in", "rocket_in" },
            new[] { "bolt_release", "chamber", "rocket_seat" },
        };

        public MagazineOwner Owner { get; private set; } = MagazineOwner.Weapon;

        /// <summary>The next beat (0…3), or −1 when no reload runs.</summary>
        public int NextStep { get; private set; } = -1;

        /// <summary>The first and the last beat this reload has: a tactical one ends on Insert (the round is chambered — no bolt
        /// release, so the timeline never lands one, C-00 05.10 §12); an empty tube starts at HandAttach.</summary>
        public int FirstStep { get; private set; }
        public int LastStep { get; private set; } = (int)MagazineStep.BoltRelease;

        /// <summary>Beats a complete reload of this kind lands: 4 empty, 3 tactical (MagOut · HandAttach · MagIn), 3 empty tube.</summary>
        public int ExpectedSteps => LastStep - FirstStep + 1;

        public bool Active => NextStep >= 0;

        /// <summary>Beats landed by the clip's event / by the timeline deadline; reloads ended before their last beat.</summary>
        public int FromEvent { get; private set; }
        public int FromTimer { get; private set; }
        public int EndedEarly { get; private set; }

        /// <summary>A beat landed: the step and whether the clip sent it (false = the deadline).</summary>
        public event Action<MagazineStep, bool> Stepped;

        /// <summary>WP-104 B4 (S-06): the reload's magazine visual picks the beats — rl_1's tube starts empty; a belt box or the weapon's
        /// own part runs the magazine beats (<see cref="Begin(bool, bool)"/>).</summary>
        public void Begin(DD.Prototype.Weapons.Attachments.MagazineVisualKind kind, bool tactical)
        {
            Begin(kind == DD.Prototype.Weapons.Attachments.MagazineVisualKind.Tube, tactical);
        }

        /// <summary>
        /// A reload of a magazine weapon starts: the magazine is in the weapon, the first beat is next. <paramref name="emptyTube"/>
        /// (rl_1, S-06 05.10): the tube starts empty — nothing to detach; the round comes to the hand (mag_hand_attach), goes in
        /// (rocket_in = mag_insert) and seats (rocket_seat = bolt_release). <paramref name="tactical"/> (a round still chambered,
        /// the _tac clip): three beats, the last is Insert — the timeline never lands a bolt release.
        /// </summary>
        public void Begin(bool emptyTube = false, bool tactical = false)
        {
            if (Active) End();
            Owner = MagazineOwner.Weapon;
            FirstStep = emptyTube ? (int)MagazineStep.HandAttach : 0;
            LastStep = tactical && !emptyTube ? (int)MagazineStep.Insert : (int)MagazineStep.BoltRelease;
            NextStep = FirstStep;
        }

        /// <summary>The clip sent <paramref name="step"/>: lands it (and any skipped earlier beat first). False when not expected.</summary>
        public bool OnEvent(MagazineStep step)
        {
            int s = (int)step;
            if (!Active || s < NextStep) return false;
            while (NextStep < s) Land((MagazineStep)NextStep, false);
            Land(step, true);
            return true;
        }

        /// <summary>
        /// Advance by the reload's progress (0…1) over <paramref name="reloadSeconds"/>: a beat whose mark plus the grace has passed
        /// lands from the timeline. <paramref name="marks"/> null = <see cref="MagazineMarks"/>.
        /// </summary>
        public void Tick(float progress01, float reloadSeconds, float[] marks = null)
        {
            if (!Active) return;
            float[] m = marks ?? MagazineMarks;
            float grace = reloadSeconds > 0f ? DeadlineGraceSeconds / reloadSeconds : 0f;
            while (Active && NextStep <= LastStep && progress01 >= m[NextStep] + grace) Land((MagazineStep)NextStep, false);
        }

        /// <summary>The reload ended (complete, cancelled, switch, Special, death, camera change): the magazine is in the weapon now.</summary>
        public void End()
        {
            if (!Active) return;
            if (NextStep <= LastStep) EndedEarly++;
            NextStep = -1;
            Owner = MagazineOwner.Weapon;
        }

        /// <summary>
        /// The step a clip event names, by the contract's names and the aliases (<see cref="EventNames"/>); false for any other event.
        /// B-03 B1 (C-00 05.10): a switch on the enum, not name compares — the contract's spellings mag_detach / mag_insert /
        /// bolt_release arrive as MagOut / MagIn / Chamber (CharacterAnimationEvents.Synonyms); an EditMode test holds it to the table.
        /// </summary>
        public static bool TryStepFor(CharacterAnimationEvent e, out MagazineStep step)
        {
            switch (e)
            {
                case CharacterAnimationEvent.MagOut: step = MagazineStep.Detach; return true;
                case CharacterAnimationEvent.MagHandAttach: step = MagazineStep.HandAttach; return true;
                case CharacterAnimationEvent.MagIn:
                case CharacterAnimationEvent.RocketIn: step = MagazineStep.Insert; return true;
                case CharacterAnimationEvent.Chamber:
                case CharacterAnimationEvent.RocketSeat: step = MagazineStep.BoltRelease; return true;
                default: step = MagazineStep.Detach; return false;
            }
        }

        private void Land(MagazineStep step, bool fromEvent)
        {
            switch (step)
            {
                case MagazineStep.Detach: Owner = MagazineOwner.HandOld; break;
                case MagazineStep.HandAttach: Owner = MagazineOwner.HandNew; break;
                default: Owner = MagazineOwner.Weapon; break;
            }

            if (fromEvent) FromEvent++;
            else FromTimer++;
            NextStep = (int)step + 1;
            if (NextStep >= StepCount) NextStep = -1;
            // a tactical reload is done after Insert; a clip's own bolt_release past it (if one ever sends it) is still accepted
            else if (NextStep > LastStep) NextStep = StepCount - 1;
            Stepped?.Invoke(step, fromEvent);
        }
    }
}
