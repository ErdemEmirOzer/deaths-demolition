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

        /// <summary>Master volume, 0-1. Also on the F1 panel's Audio tab.</summary>
        public static float MasterVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }
    }
}
