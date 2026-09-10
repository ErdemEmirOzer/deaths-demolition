using System.Collections.Generic;
using DD.Prototype.Audio;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The interface sounds. Distinct enough to be told apart with eyes closed.</summary>
    public enum UiSoundKind
    {
        /// <summary>Focus moved to another control. Quiet, high, very short.</summary>
        Move = 0,

        /// <summary>A control was activated. Two-tone, rising.</summary>
        Confirm = 1,

        /// <summary>Went back a screen, or cancelled a dialog. Two-tone, falling.</summary>
        Back = 2,

        /// <summary>A locked control was touched. Dull, low, no pitch movement.</summary>
        Denied = 3,

        /// <summary>Something new was revealed on the results screen. Bright, longer.</summary>
        Reveal = 4
    }

    /// <summary>
    /// The interface's voice: one call site for every UI sound in the build.
    ///
    /// WHO ACTUALLY MAKES THE SOUND
    /// ----------------------------
    /// Two systems can. <see cref="PrototypeAudio"/> (G-09) owns the game's audio library,
    /// its bus mix and its F1 tab, and exposes exactly four interface calls - Navigate,
    /// Confirm, Back, Denied. When its director is alive, THIS CLASS FORWARDS TO IT and the
    /// interface sits inside the real mix:
    ///
    ///     UiSoundKind.Move    -> PrototypeAudio.Navigate()
    ///     UiSoundKind.Confirm -> PrototypeAudio.Confirm()
    ///     UiSoundKind.Back    -> PrototypeAudio.Back()
    ///     UiSoundKind.Denied  -> PrototypeAudio.Denied()
    ///
    /// The forward happens here and not at the ~8 call sites on purpose: a screen must not
    /// have to know which audio system is loaded, and a sound must never play twice because
    /// a button called both.
    ///
    /// The local synthesiser below stays as the fallback. The Main Menu and Selection
    /// scenes can boot without an audio director, and <see cref="UiSoundKind.Reveal"/> has
    /// no counterpart in the library at all, so a silent menu would otherwise be one missing
    /// component away. Clips are generated from arithmetic at first use, there is no
    /// imported asset, and the fallback never enters the combat audio budget.
    ///
    /// One AudioSource, unpooled, 2D, on a DontDestroyOnLoad object. Interface sounds
    /// are single-digit-per-second by nature; a pool would be more machinery than the
    /// problem has.
    ///
    /// <see cref="Enabled"/> gates BOTH paths, so the F1 "UI sound" toggle still means what
    /// it says.
    /// </summary>
    public static class UiSound
    {
        private const int SampleRate = 44100;

        /// <summary>PROTOTYPE-ONLY. Quiet enough to sit under the atmosphere bed.</summary>
        private const float Volume = 0.32f;

        /// <summary>Repeats of the same sound inside this window are dropped.</summary>
        private const float RetriggerWindow = 0.035f;

        private static AudioSource s_source;
        private static readonly Dictionary<UiSoundKind, AudioClip> s_clips = new Dictionary<UiSoundKind, AudioClip>(8);
        private static readonly Dictionary<UiSoundKind, float> s_lastPlayed = new Dictionary<UiSoundKind, float>(8);

        /// <summary>Turned off by the F1 panel or by a screen that wants silence.</summary>
        public static bool Enabled = true;

        public static void Play(UiSoundKind kind)
        {
            if (!Enabled || !Application.isPlaying)
            {
                return;
            }

            float now = Time.unscaledTime;
            float last;
            if (s_lastPlayed.TryGetValue(kind, out last) && now - last < RetriggerWindow)
            {
                return;
            }

            s_lastPlayed[kind] = now;

            // The real audio system takes it when it is present. Returning here is what
            // keeps the sound from playing twice.
            if (PlayThroughAudioDirector(kind))
            {
                return;
            }

            AudioSource source = Source();
            if (source == null)
            {
                return;
            }

            AudioClip clip = Clip(kind);
            if (clip == null)
            {
                return;
            }

            source.PlayOneShot(clip, Volume);
        }

        /// <summary>
        /// Hands the sound to G-09's audio director. False means "nobody took it" - either
        /// there is no director in the scene, or the kind has no library counterpart - and
        /// the caller falls back to the local synthesiser.
        /// </summary>
        private static bool PlayThroughAudioDirector(UiSoundKind kind)
        {
            if (PrototypeAudioDirector.Instance == null)
            {
                return false;
            }

            switch (kind)
            {
                case UiSoundKind.Move:
                    PrototypeAudio.Navigate();
                    return true;

                case UiSoundKind.Confirm:
                    PrototypeAudio.Confirm();
                    return true;

                case UiSoundKind.Back:
                    PrototypeAudio.Back();
                    return true;

                case UiSoundKind.Denied:
                    PrototypeAudio.Denied();
                    return true;

                default:
                    // Reveal. The library has no equivalent; the local voice keeps it.
                    return false;
            }
        }

        private static AudioSource Source()
        {
            if (s_source != null)
            {
                return s_source;
            }

            var go = new GameObject("UI Audio")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Object.DontDestroyOnLoad(go);

            s_source = go.AddComponent<AudioSource>();
            s_source.playOnAwake = false;
            s_source.spatialBlend = 0f;
            s_source.dopplerLevel = 0f;
            s_source.bypassEffects = true;
            s_source.bypassReverbZones = true;

            // Menus and the pause screen run at timeScale 0; audio must not care.
            s_source.ignoreListenerPause = true;
            s_source.ignoreListenerVolume = false;

            return s_source;
        }

        private static AudioClip Clip(UiSoundKind kind)
        {
            AudioClip cached;
            if (s_clips.TryGetValue(kind, out cached) && cached != null)
            {
                return cached;
            }

            AudioClip clip = Synthesise(kind);
            s_clips[kind] = clip;
            return clip;
        }

        /// <summary>
        /// PROTOTYPE-ONLY synthesis. Every number here is placeholder art direction:
        /// a decaying sine pair with a touch of noise on the attack, which is enough to
        /// read as "wooden" rather than as "beep" and sits inside the palace theme.
        /// </summary>
        private static AudioClip Synthesise(UiSoundKind kind)
        {
            float duration;
            float startHz;
            float endHz;
            float decay;
            float noise;

            switch (kind)
            {
                case UiSoundKind.Move:
                    duration = 0.055f;
                    startHz = 1180f;
                    endHz = 1180f;
                    decay = 60f;
                    noise = 0.10f;
                    break;

                case UiSoundKind.Confirm:
                    duration = 0.16f;
                    startHz = 640f;
                    endHz = 960f;
                    decay = 22f;
                    noise = 0.06f;
                    break;

                case UiSoundKind.Back:
                    duration = 0.16f;
                    startHz = 720f;
                    endHz = 440f;
                    decay = 22f;
                    noise = 0.06f;
                    break;

                case UiSoundKind.Denied:
                    duration = 0.13f;
                    startHz = 210f;
                    endHz = 190f;
                    decay = 28f;
                    noise = 0.22f;
                    break;

                default: // Reveal
                    duration = 0.42f;
                    startHz = 880f;
                    endHz = 1320f;
                    decay = 7f;
                    noise = 0.03f;
                    break;
            }

            int samples = Mathf.Max(16, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[samples];

            var random = new System.Random(unchecked((int)kind * 7919 + 13));
            float phase = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += 2f * Mathf.PI * hz / SampleRate;

                float envelope = Mathf.Exp(-decay * (t * duration));

                // Short attack ramp removes the click a hard start would produce.
                float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));

                float tone = Mathf.Sin(phase) * 0.8f + Mathf.Sin(phase * 2f) * 0.2f;
                float grain = (float)(random.NextDouble() * 2.0 - 1.0) * noise * Mathf.Exp(-decay * 3f * (t * duration));

                data[i] = Mathf.Clamp((tone + grain) * envelope * attack, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("UiSound." + kind, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_source = null;
            s_clips.Clear();
            s_lastPlayed.Clear();
            Enabled = true;
        }
    }
}
