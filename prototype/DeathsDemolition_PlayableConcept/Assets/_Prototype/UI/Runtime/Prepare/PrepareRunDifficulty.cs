using DD.Prototype.Difficulty;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-106 · what Prepare Run's difficulty rows read and write - the modes in their order, the one chosen, and a lock per
    /// mode. The screen never touches <see cref="DifficultyService"/> itself: the rows go through this seam, so a test (or a
    /// later rule that opens a mode on the profile) swaps the source without touching the layout.
    /// </summary>
    public interface IPrepareDifficulty
    {
        /// <summary>The modes on the screen, top to bottom.</summary>
        int Count { get; }

        /// <summary>The mode's stable id ("normal" / "hard" / "demolition"): the string keys read <c>difficulty.&lt;id&gt;.*</c>.</summary>
        string IdAt(int index);

        /// <summary>How hard the mode is, 1 … 3: the row's indicator draws this many full marks.</summary>
        int LevelAt(int index);

        /// <summary>A mode that cannot be chosen yet (its row stays focusable; a press is refused with Denied).</summary>
        bool IsLockedAt(int index);

        /// <summary>The row of the mode the next run starts with.</summary>
        int Selected { get; }

        /// <summary>Chooses row <paramref name="index"/> for the next run; false (nothing changes) when it is locked or out of range.</summary>
        bool Select(int index);
    }

    /// <summary>
    /// WP-106 · the source Prepare Run reads: WP-102's <see cref="DifficultyService"/> (Normal / Hard / Demolition, every mode
    /// open, the choice kept for the session - a restarted game opens on Normal, as the weapon and DEC-037 / DEC-039 do; a
    /// <c>-ddDifficulty</c> launch override shows as the chosen row and locks the others). Tests install their own with
    /// <see cref="SetSourceForTests"/>.
    /// </summary>
    public static class PrepareDifficulty
    {
        private static IPrepareDifficulty s_override;

        /// <summary>The live source: the test override, else the difficulty service.</summary>
        public static IPrepareDifficulty Source
        {
            get { return s_override ?? ServiceSource.Instance; }
        }

        /// <summary>Test seam: a fixed source (null puts the service back).</summary>
        public static void SetSourceForTests(IPrepareDifficulty source)
        {
            s_override = source;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_override = null;
        }

        /// <summary>WP-102's three modes in their persisted order (DifficultyMode: the numeric value never changes).</summary>
        private sealed class ServiceSource : IPrepareDifficulty
        {
            public static readonly ServiceSource Instance = new ServiceSource();

            private static readonly DifficultyMode[] Modes = { DifficultyMode.Normal, DifficultyMode.Hard, DifficultyMode.Demolition };

            public int Count
            {
                get { return Modes.Length; }
            }

            public string IdAt(int index)
            {
                return index >= 0 && index < Modes.Length ? DifficultyService.Id(Modes[index]) : null;
            }

            public int LevelAt(int index)
            {
                return Mathf.Clamp(index + 1, 1, 3);
            }

            public bool IsLockedAt(int index)
            {
                // WP-102 (user note 2): all three modes are open from the start. S-03 (12.10): a -ddDifficulty launch fixes the
                // mode for the process (DifficultyService.LaunchOverride ignores the menu's writes) - the other rows show locked.
                return DifficultyService.LaunchOverride && index != Selected;
            }

            public int Selected
            {
                get
                {
                    DifficultyMode mode = DifficultyService.Selected;
                    for (int i = 0; i < Modes.Length; i++)
                    {
                        if (Modes[i] == mode)
                        {
                            return i;
                        }
                    }

                    return 0;
                }
            }

            public bool Select(int index)
            {
                if (index < 0 || index >= Modes.Length || IsLockedAt(index))
                {
                    return false;
                }

                DifficultyService.Selected = Modes[index];
                return true;
            }
        }
    }
}
