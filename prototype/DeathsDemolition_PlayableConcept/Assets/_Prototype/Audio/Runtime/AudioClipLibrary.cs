using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// Bakes every recipe into AudioClips once, keeps them, and hands out variations in an
    /// order that can never repeat itself back to back.
    ///
    /// THE VARIATION PROBLEM, AND WHY RANDOM IS NOT THE ANSWER
    /// The acceptance criterion is zero consecutive repeats. Uniform random picking from
    /// four variations repeats about a quarter of the time, and at an assault rifle's
    /// 8.5 rounds/second a quarter is roughly twice a second - which the ear hears as a
    /// machine, which is the exact artefact the variations exist to hide.
    ///
    /// So this does not roll dice. Each sound owns a DECK: a shuffled permutation of its
    /// variations that is dealt through in order and reshuffled when empty, with the
    /// reshuffle rejected if it would put the last-dealt card first. That gives
    ///   * zero consecutive repeats, guaranteed rather than probable;
    ///   * even distribution - every variation is heard exactly once per deck, so the
    ///     "good" variation never dominates by luck;
    ///   * no allocation per shot.
    /// Combined with the live pitch jitter in the mix plan, four baked variations produce
    /// far more apparent variety than eight randomly picked ones would.
    ///
    /// MEMORY
    /// Every clip is 44.1 kHz mono float in RAM, so 4 bytes a sample and no streaming and
    /// no decompression cost. Total is measured, not estimated - see
    /// <see cref="MemoryBytes"/>, which the F1 panel prints. Baking is spread over frames
    /// by <see cref="Prewarm"/> so the first round does not stutter while a 3.4 second
    /// gong is synthesised.
    /// </summary>
    public sealed class AudioClipLibrary
    {
        /// <summary>PROTOTYPE-ONLY / INVENTED: synthesis time allowed per prewarm frame.</summary>
        private const double PrewarmMillisecondsPerFrame = 3.0;

        /// <summary>
        /// The memory budget, and the only number in this folder that is allowed to fail
        /// loudly.
        ///
        /// MEASURED, not estimated. The whole library baked out-of-engine comes to
        /// 147 clips / 4.83 M samples / 18.4 MB, split:
        ///
        ///     ambience   8.34 MB   six 6 s beds (6.06) + four scatter one-shots (2.28)
        ///     interface  3.80 MB   dominated by the 3.4 s gong and the 3.6 s death
        ///     target     2.61 MB
        ///     weapon     2.28 MB
        ///     impact     1.40 MB   32 clips, the most-used family and the cheapest
        ///
        /// 24 MB is that plus 30% of headroom for a later task adding sounds. Past it means
        /// somebody has added a long clip without thinking about it, and a warning in the
        /// console on the frame it happens is worth far more than a line in a document.
        /// </summary>
        public const long MemoryBudgetBytes = 24L * 1024L * 1024L;

        private bool _budgetWarned;

        private sealed class Entry
        {
            public AudioClip[] Clips;
            public int[] Deck;
            public int DeckIndex;
            public int LastDealt = -1;
        }

        private readonly Dictionary<AudioSoundId, Entry> _entries = new Dictionary<AudioSoundId, Entry>(48);
        private SynthRandom _shuffleRng = new SynthRandom(0x5EED1234);

        /// <summary>Clips baked so far.</summary>
        public int ClipCount { get; private set; }

        /// <summary>Sample memory held by the library, in bytes. F1 readout.</summary>
        public long MemoryBytes { get; private set; }

        /// <summary>Seconds of wall clock spent synthesising. F1 readout.</summary>
        public double BakeMilliseconds { get; private set; }

        /// <summary>True once <see cref="Prewarm"/> has walked the whole id list.</summary>
        public bool PrewarmComplete { get; private set; }

        /// <summary>Every sound the library knows how to make, in bake order.</summary>
        public static readonly AudioSoundId[] AllSounds =
        {
            // Combat first: these are the ones a player can hear inside the first second
            // of a round, so they are the ones that must not be baked lazily.
            AudioSoundId.RifleFire, AudioSoundId.SmgFire, AudioSoundId.ShotgunFire,
            AudioSoundId.HeavyFire, AudioSoundId.DryFire,
            AudioSoundId.ReloadStart, AudioSoundId.ReloadInsert, AudioSoundId.ReloadEnd,
            AudioSoundId.HeavyMotorLoop, AudioSoundId.HeavySpinDown,

            AudioSoundId.FleshHit, AudioSoundId.CriticalHit,
            AudioSoundId.ArmorImpact, AudioSoundId.ArmorCrack, AudioSoundId.ArmorBreak,
            AudioSoundId.EnemyDeath, AudioSoundId.HeavyEnemyDeath,

            AudioSoundId.HitMarker, AudioSoundId.KillMarker,

            AudioSoundId.ImpactDefault, AudioSoundId.ImpactStone, AudioSoundId.ImpactWood,
            AudioSoundId.ImpactFoliage, AudioSoundId.ImpactWater, AudioSoundId.ImpactMetal,
            AudioSoundId.ImpactDirt, AudioSoundId.ImpactPaper,

            AudioSoundId.Ricochet, AudioSoundId.ChainArc, AudioSoundId.Explosion,

            AudioSoundId.PlayerDamage, AudioSoundId.PlayerDeath,

            AudioSoundId.SwSpawnGong, AudioSoundId.SwBeaconLoop, AudioSoundId.SwPickup,
            AudioSoundId.SwFinalSeconds, AudioSoundId.SwExpired, AudioSoundId.SwGroundExpired,

            AudioSoundId.GoldPickup, AudioSoundId.RoundCountdownTick,
            AudioSoundId.RoundStart, AudioSoundId.RoundComplete,
            AudioSoundId.UiNavigate, AudioSoundId.UiConfirm, AudioSoundId.UiBack,
            AudioSoundId.UiDenied, AudioSoundId.UpgradePurchase,

            // Beds last. They are the biggest buffers and the ambience director fades them
            // in over a second anyway, so a frame or two of delay is free.
            AudioSoundId.AmbWind, AudioSoundId.AmbLeaves, AudioSoundId.AmbWater,
            AudioSoundId.AmbPalaceTone, AudioSoundId.AmbTensionLow, AudioSoundId.AmbTensionHigh,
            AudioSoundId.AmbWoodCreak, AudioSoundId.AmbMetalRing,
            AudioSoundId.AmbBambooKnock, AudioSoundId.AmbStoneSettle
        };

        /// <summary>
        /// Bakes the library a few milliseconds at a time. Run it as a coroutine on the
        /// director; anything asked for before its turn is baked on demand instead.
        /// </summary>
        public IEnumerator Prewarm()
        {
            Stopwatch frameWatch = new Stopwatch();

            for (int i = 0; i < AllSounds.Length; i++)
            {
                if (!frameWatch.IsRunning)
                {
                    frameWatch.Restart();
                }

                EnsureBaked(AllSounds[i]);

                if (frameWatch.Elapsed.TotalMilliseconds >= PrewarmMillisecondsPerFrame)
                {
                    frameWatch.Reset();
                    yield return null;
                }
            }

            PrewarmComplete = true;
        }

        /// <summary>
        /// Next clip for this sound. Never returns the same variation twice in a row when
        /// the sound has more than one.
        /// </summary>
        public AudioClip Next(AudioSoundId id, out int variation)
        {
            Entry entry = EnsureBaked(id);
            variation = 0;

            if (entry == null || entry.Clips.Length == 0)
            {
                return null;
            }

            if (entry.Clips.Length == 1)
            {
                return entry.Clips[0];
            }

            if (entry.DeckIndex >= entry.Deck.Length)
            {
                Shuffle(entry);
            }

            variation = entry.Deck[entry.DeckIndex];
            entry.DeckIndex++;
            entry.LastDealt = variation;

            return entry.Clips[variation];
        }

        /// <summary>The single clip of a sound that has no variations - loops, mostly.</summary>
        public AudioClip First(AudioSoundId id)
        {
            Entry entry = EnsureBaked(id);
            return entry != null && entry.Clips.Length > 0 ? entry.Clips[0] : null;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<AudioSoundId, Entry> pair in _entries)
            {
                AudioClip[] clips = pair.Value.Clips;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null)
                    {
                        Object.Destroy(clips[i]);
                    }
                }
            }

            _entries.Clear();
            ClipCount = 0;
            MemoryBytes = 0;
            PrewarmComplete = false;
        }

        /// <summary>Human-readable memory line for the F1 panel.</summary>
        public string DescribeMemory()
        {
            return string.Format(
                "{0} clips, {1:0.0} MB, baked in {2:0} ms{3}",
                ClipCount, MemoryBytes / (1024.0 * 1024.0), BakeMilliseconds,
                PrewarmComplete ? "" : " (prewarming)");
        }

        private Entry EnsureBaked(AudioSoundId id)
        {
            if (_entries.TryGetValue(id, out Entry existing))
            {
                return existing;
            }

            Stopwatch watch = Stopwatch.StartNew();

            int count = Mathf.Max(1, AudioRecipes.VariationCount(id));
            AudioClip[] clips = new AudioClip[count];

            for (int v = 0; v < count; v++)
            {
                float[] samples = AudioRecipes.Build(id, v);
                if (samples == null || samples.Length == 0)
                {
                    samples = new float[64];
                }

                clips[v] = SynthOps.ToClip(samples, "dd_" + id + "_" + v);
                MemoryBytes += (long)samples.Length * 4L;
                ClipCount++;
            }

            watch.Stop();
            BakeMilliseconds += watch.Elapsed.TotalMilliseconds;

            if (!_budgetWarned && MemoryBytes > MemoryBudgetBytes)
            {
                _budgetWarned = true;
                UnityEngine.Debug.LogWarning(
                    $"[Audio] Clip library is over its {MemoryBudgetBytes / (1024 * 1024)} MB budget " +
                    $"({MemoryBytes / (1024.0 * 1024.0):0.0} MB after baking {id}). " +
                    "Shorten a clip or drop a variation - see AudioRecipes.VariationCount.");
            }

            Entry entry = new Entry
            {
                Clips = clips,
                Deck = new int[count],
                DeckIndex = count // forces a shuffle on the first deal
            };

            for (int i = 0; i < count; i++)
            {
                entry.Deck[i] = i;
            }

            _entries.Add(id, entry);
            return entry;
        }

        /// <summary>
        /// Fisher-Yates, then one guard: if the new deck opens with the card we just
        /// dealt, swap it with the second. That single line is what turns "usually does
        /// not repeat" into "cannot repeat".
        /// </summary>
        private void Shuffle(Entry entry)
        {
            int n = entry.Deck.Length;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _shuffleRng.RangeInt(0, i + 1);
                int tmp = entry.Deck[i];
                entry.Deck[i] = entry.Deck[j];
                entry.Deck[j] = tmp;
            }

            if (n > 1 && entry.Deck[0] == entry.LastDealt)
            {
                int tmp = entry.Deck[0];
                entry.Deck[0] = entry.Deck[1];
                entry.Deck[1] = tmp;
            }

            entry.DeckIndex = 0;
        }
    }
}
