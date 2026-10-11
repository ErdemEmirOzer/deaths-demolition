using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The four calls the interface needs, and nothing else.
    ///
    /// ⚠ THIS IS AN OPEN CONTRACT. The request lists "UI navigation" among the events that
    /// must be separable, but the UI kit on disk (Assets/_Prototype/UI/Runtime/Kit) has no
    /// navigation, confirm or cancel event to subscribe to yet - G-08 is writing it in
    /// parallel. Rather than invent an event surface inside somebody else's task, the
    /// sounds exist, they are baked, they are on the F1 panel, and they are one static
    /// call away:
    ///
    ///     PrototypeAudio.Navigate();   // selection moved
    ///     PrototypeAudio.Confirm();    // accepted
    ///     PrototypeAudio.Back();       // cancelled / closed
    ///     PrototypeAudio.Denied();     // refused - not enough gold, locked
    ///
    /// The upgrade screen does NOT need to call these: RunAudioEvent.UpgradePurchased,
    /// UpgradeRefused and UpgradeSkipped already reach the director on their own.
    ///
    /// Every call is a no-op when there is no director, so calling these from a menu scene
    /// that boots without one is safe.
    /// </summary>
    public static class PrototypeAudio
    {
        /// <summary>Selection moved. The most frequent sound in the game after the flesh hit.</summary>
        public static void Navigate()
        {
            Play(AudioSoundId.UiNavigate);
        }

        /// <summary>Accepted. Rising interval.</summary>
        public static void Confirm()
        {
            Play(AudioSoundId.UiConfirm);
        }

        /// <summary>Cancelled or closed. The same interval, falling.</summary>
        public static void Back()
        {
            Play(AudioSoundId.UiBack);
        }

        /// <summary>Refused. The one dissonance in the interface palette.</summary>
        public static void Denied()
        {
            Play(AudioSoundId.UiDenied);
        }

        /// <summary>WP-021C: the results screen's NEW badge (UiSound.Reveal). Was a local synth; now the AP-011 clip.</summary>
        public static void Reveal()
        {
            Play(AudioSoundId.UiReveal);
        }

        /// <summary>Plays any library sound directly. Prefer an event where one exists.</summary>
        public static void Play(AudioSoundId sound, float gain = 1f)
        {
            PrototypeAudioDirector director = PrototypeAudioDirector.Instance;
            if (director == null)
            {
                return;
            }

            director.PlayDirect(sound, Vector3.zero, gain);
        }

        /// <summary>Plays any library sound at a world position.</summary>
        public static void PlayAt(AudioSoundId sound, Vector3 position, float gain = 1f)
        {
            PrototypeAudioDirector director = PrototypeAudioDirector.Instance;
            if (director == null)
            {
                return;
            }

            director.PlayDirect(sound, position, gain);
        }

        private static float s_masterVolume = 1f;

        /// <summary>
        /// Master volume, 0-1. Single owner (WP-021C, S-09 contract): <c>UiSettings.MasterVolume</c>
        /// writes here and raises <c>MasterVolumeChanged</c>; the director mirrors the value onto the
        /// mixer's exposed "MasterVolume" (dB, ramped over ~60 ms so a slider never clicks). Without a
        /// director or mixer the value lands on <see cref="AudioListener.volume"/> exactly as before.
        /// Also on the F1 panel's Audio tab.
        /// </summary>
        public static float MasterVolume
        {
            get => s_masterVolume;
            set
            {
                s_masterVolume = Mathf.Clamp01(value);
                PrototypeAudioDirector director = PrototypeAudioDirector.Instance;
                if (director != null && director.MixerOwnsMaster)
                {
                    AudioListener.volume = 1f;
                    director.RequestMasterVolume(s_masterVolume);
                }
                else
                {
                    AudioListener.volume = s_masterVolume;
                }
            }
        }

        /// <summary>
        /// WP-068 U8: the UI SOUNDS slider, 0-1. <c>UiSettings.UiVolume</c> writes here; the director applies it as the
        /// player multiplier of the software Interface bus (not the mixer's exposed "UiVolume", which would override the
        /// snapshot UI trims). Every Interface voice follows on its next tick.
        /// </summary>
        public static float UiVolume
        {
            get => s_uiVolume;
            set
            {
                s_uiVolume = Mathf.Clamp01(value);
                PrototypeAudioDirector director = PrototypeAudioDirector.Instance;
                if (director != null)
                {
                    director.Mixer.SetPlayerVolume(AudioBus.Interface, s_uiVolume);
                }
            }
        }

        private static float s_uiVolume = 1f;

        /// <summary>
        /// WP-105 (S-09 contract, C-00 11.10): the MUSIC slider, 0-1. <c>UiSettings.MusicVolume</c> writes here; the director ramps
        /// it over the master's 60 ms onto the player multiplier of the software Music bus - not the mixer's exposed "MusicVolume",
        /// which would override the snapshots (as for <see cref="UiVolume"/>). Every music layer and stinger follows on its next tick.
        /// </summary>
        public static float MusicVolume
        {
            get => s_musicVolume;
            set => s_musicVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// WP-105: the EFFECTS slider, 0-1: the Weapon, Impact, Target and Ambience buses - every game sound but the music and the
        /// interface (the ambience beds count as effects). <c>UiSettings.EffectsVolume</c> writes here; ramped like
        /// <see cref="MusicVolume"/>.
        /// </summary>
        public static float EffectsVolume
        {
            get => s_effectsVolume;
            set => s_effectsVolume = Mathf.Clamp01(value);
        }

        private static float s_musicVolume = 1f;
        private static float s_effectsVolume = 1f;
    }
}
