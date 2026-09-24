using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The one object that listens to the game and makes noise.
    ///
    /// It subscribes to five surfaces and owns everything downstream of them:
    ///
    ///   AudioEvents.Cue            33 combat events   (weapons, damage, special weapon)
    ///   AudioEvents.LoopStopped    2 loops            (beacon, rotary motor)
    ///   RunAudioEvents.Cue         10 run-flow events (gold, rounds, upgrades)
    ///   RunEvents.*                phase, round start / complete - drives ambience, music, snapshots
    ///   PlayerHealth.Any*          damage and death
    ///   DamageEvents.*  (WP-021)   zombie vocal set, through AudioEventRouter
    ///
    /// WP-021 added the Unity AudioMixer (AudioMixerBridge: six groups, four snapshots),
    /// the produced sound set (AudioCatalog over the procedural library), regional
    /// ambience (AmbienceDirector + AudioRegionMap) and the four-layer score
    /// (MusicDirector). The event map is ADR-021-1 (AudioEventMap).
    ///
    /// Nothing else in the project touches an AudioSource, and nothing in this folder is
    /// referenced by anything outside it. Deleting the whole Audio directory would leave a
    /// silent but perfectly working game, which is the test of whether the contract held.
    ///
    /// IT CREATES ITSELF. Four tasks are editing this project in parallel and none of them
    /// can be asked to remember to drop an audio prefab into a scene, so it comes up on
    /// AfterSceneLoad and survives scene changes.
    ///
    /// PAUSE (the request's last HUD line, and requirement 4 of this task)
    /// The pause menu freezes the simulation through <see cref="RunFlowClock"/>. Audio has
    /// to freeze WITH it, but only for pause - the round-end and upgrade screens use the
    /// same clock and must keep their ambience, because a silent upgrade screen is the
    /// opposite of "atmosfer sakinleşsin". So this polls for the PAUSE HOLD SPECIFICALLY
    /// rather than for RunFlowClock.IsFrozen, and sets AudioListener.pause, which suspends
    /// loops and one-shots mid-sample and resumes them exactly where they were.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeAudioDirector : PrototypeConfigBehaviour
    {
        private const string HostName = "[Audio Director]";
        private const string AudioTab = "Audio";

        /// <summary>
        /// AudioSources allocated for world one-shots. The BUDGET is
        /// <c>Feedback.MaxCombatAudioVoices</c> (16 by default); this is only the ceiling
        /// the F1 slider can be pushed to without a reallocation.
        /// </summary>
        private const int WorldHardCapacity = 32;

        /// <summary>
        /// The interface pool. PROTOTYPE-ONLY / INVENTED at 6, and deliberately separate
        /// from the world pool rather than carved out of it: hit markers, gold, round
        /// countdown and player damage are the game TALKING to the player, and a crowd of
        /// zombies must never be able to make the game stop talking. Six is enough for a
        /// magnet streak of six orbs, which was the worst case that existed; WP-021C adds the
        /// threat / low-health chips and the UI event set on the same pool, so eight: a streak
        /// plus a SURROUNDED chip plus a hover never steal from each other.
        /// </summary>
        private const int SignalCapacity = 8;

        /// <summary>Simultaneous looping sounds. There are two in the game; four is slack.</summary>
        private const int LoopCapacity = 4;

        private static PrototypeAudioDirector s_instance;

        private readonly AudioBusMixer _mixer = new AudioBusMixer();
        private readonly ArmorFatigueTracker _armour = new ArmorFatigueTracker();

        // WP-021: created in Awake, not as a field initializer - the catalog is a
        // Resources.Load and Unity forbids that from a MonoBehaviour constructor.
        private AudioClipLibrary _library;

        private AudioVoicePool _world;
        private AudioVoicePool _signal;
        private AmbienceDirector _ambience;

        // WP-021
        private AudioMixerBridge _bridge;
        private MusicDirector _music;
        private AudioEventRouter _router;
        private AudioListenerAnchor _listener;
        private WeaponFireAudio _weaponFire;
        private CasingAudio _casings;
        private ImpactAudio _impacts;
        private ProjectileFlightAudio _flight;
        private readonly CombatAudioCue[] _heldImpacts = new CombatAudioCue[16];
        private AudioSnapshot _snapshotBeforePause = AudioSnapshot.Run;

        private readonly List<DebugPanelEntry> _debugEntries = new List<DebugPanelEntry>(16);
        private readonly Dictionary<CombatAudioEvent, LoopVoice> _loops =
            new Dictionary<CombatAudioEvent, LoopVoice>(LoopCapacity);

        private Transform _loopRoot;
        private bool _paused;

        // WP-021C: the user master volume on the mixer's exposed parameter, ramped in linear
        // gain so a settings slider (20 steps) never lands as a click on the master bus.
        private const float MasterRampSeconds = 0.06f;
        private float _masterTarget = 1f;
        private float _masterApplied = -1f;
        private int _configuredBudget = 16;
        private int _budgetOverride;
        private int _suppressedSeen;

        public static PrototypeAudioDirector Instance => s_instance;

        /// <summary>True when the mixer asset is present: the exposed "MasterVolume" is the master, not AudioListener.volume.</summary>
        public bool MixerOwnsMaster => _bridge != null && _bridge.Available;

        /// <summary>The master volume the mixer is ramping towards (0-1). Test seam.</summary>
        public float MasterVolumeTarget => _masterTarget;

        /// <summary>WP-021C: from <see cref="PrototypeAudio.MasterVolume"/> / <c>UiSettings.MasterVolumeChanged</c>. Applied over <see cref="MasterRampSeconds"/> in Update.</summary>
        public void RequestMasterVolume(float linear01)
        {
            _masterTarget = Mathf.Clamp01(linear01);
        }

        /// <summary>WP-021 seams for tests and the F1 panel.</summary>
        public AmbienceDirector Ambience => _ambience;
        public MusicDirector Music => _music;
        public AudioEventRouter Router => _router;
        public AudioMixerBridge Bridge => _bridge;
        public AudioListenerAnchor Listener => _listener;
        public AudioClipLibrary Library => _library;
        public AudioBusMixer Mixer => _mixer;
        public AudioVoicePool WorldPool => _world;
        public AudioVoicePool SignalPool => _signal;
        public bool IsPaused => _paused;
        public int LoopCount => _loops.Count;
        public Vector3 LoopPosition(CombatAudioEvent evt) =>
            _loops.TryGetValue(evt, out LoopVoice voice) && voice.Source != null
                ? voice.Source.transform.position
                : Vector3.zero;

        /// <summary>Sounds actually handed to a voice since start (both pools). Test readout.</summary>
        public int PlayedCount { get; private set; }

        /// <summary>The last sound that got a voice. Test readout.</summary>
        public AudioSoundId LastPlayed { get; private set; }

        /// <summary>WP-072 B1 test / F1 surface: the player's fire voice.</summary>
        public WeaponFireAudio WeaponFire => _weaponFire;

        /// <summary>WP-072 B2 test / F1 surface: the player's casing landings.</summary>
        public CasingAudio Casings => _casings;

        /// <summary>WP-072 B3 test / F1 surface: the projectile impact voice.</summary>
        public ImpactAudio Impacts => _impacts;

        /// <summary>WP-072 B5 test / F1 surface: projectile flight loops.</summary>
        public ProjectileFlightAudio Flight => _flight;

        /// <summary>WP-072 B5 (STANDARDS §7): near ≤ 12 m, mid 12–35 m, far &gt; 35 m from the listener.</summary>
        public static AudioSoundId ExplosionLayer(float meters)
        {
            return meters <= 12f ? AudioSoundId.ExplosionNear : meters <= 35f ? AudioSoundId.ExplosionMid : AudioSoundId.ExplosionFar;
        }

        /// <summary>Total cues refused by the voice budget across both pools. F1 readout, and the O3 measurement.</summary>
        public int DroppedCues => (_world != null ? _world.DroppedCount : 0) + (_signal != null ? _signal.DroppedCount : 0);

        // ================================================================== lifecycle

        public static PrototypeAudioDirector EnsureExists()
        {
            if (s_instance != null)
            {
                s_instance.SynchronizeRunPhase();
                return s_instance;
            }

            var go = new GameObject(HostName);
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PrototypeAudioDirector>();
            return s_instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            s_instance = null;
            EnsureExists();
        }

        private void Awake()
        {
            _loopRoot = new GameObject("Loops").transform;
            _loopRoot.SetParent(transform, false);

            _library = new AudioClipLibrary(AudioCatalog.Load());
            _bridge = new AudioMixerBridge();

            _world = new AudioVoicePool("World", transform, WorldHardCapacity, _mixer, _bridge);
            _signal = new AudioVoicePool("Signal", transform, SignalCapacity, _mixer, _bridge);
            _ambience = new AmbienceDirector(transform, _library, _mixer, _world, _bridge, AudioRegionMap.Load());
            _music = new MusicDirector(transform, _library, _mixer, _bridge);
            _router = new AudioEventRouter(PlayResolved);
            _router.SetFieldLoopHandlers(StartFieldLoop, StopFieldLoop);   // WP-058
            _router.SetUiVoiceHandlers(StartUiVoice, StopUiVoice);          // WP-068 s11
            _listener = new AudioListenerAnchor(transform);
            _weaponFire = new WeaponFireAudio(_library, _mixer, _bridge, _loopRoot, PlayResolved);   // WP-072 B1
            _casings = new CasingAudio(_library, PlayResolved);                                        // WP-072 B2
            _impacts = new ImpactAudio(_library, PlayResolved);                                        // WP-072 B3
            _router.SetProducedCheck(_library.IsProduced);
            BuildFieldLoops();
            BuildUiVoices();
            _signal.SetIgnoreListenerPause(true);   // WP-068 s11: the pause menu is heard
            _mixer.SetPlayerVolume(AudioBus.Interface, PrototypeAudio.UiVolume);   // WP-068 U8 (settings may load before the director)

            // v003 (ADR-021-2): distance darkening and the idle range are measured from the
            // player-anchored listener, not the camera.
            System.Func<Vector3> listenerPosition = () => _listener.Transform.position;
            _flight = new ProjectileFlightAudio(_library, _mixer, _bridge, _loopRoot, listenerPosition);   // WP-072 B5
            _world.SetListener(listenerPosition);
            _router.SetListener(listenerPosition);

            _world.Capacity = _configuredBudget;
            _bridge.Transition(AudioSnapshot.Menu, 0f);

            // WP-021C: adopt whatever master volume the settings owner already set (the menu
            // scene can boot before this director exists) and take it off the listener.
            _masterTarget = PrototypeAudio.MasterVolume;
            if (_bridge.Available)
            {
                AudioListener.volume = 1f;
                _bridge.SetVolume(AudioMixerBridge.GroupMaster, _masterTarget);
                _masterApplied = _masterTarget;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioOutputCapture.TryArm(gameObject);   // WP-060: -ddAudioCapture <wav> records the Player output for the listening round
#endif
        }

        private IEnumerator Start()
        {
            // Combat clips first, beds last - see AudioClipLibrary.AllSounds. A few frames
            // of prewarm at boot is invisible; a 40 ms hitch the first time a shotgun fires
            // is not.
            yield return StartCoroutine(_library.Prewarm());

            _ambience.Start();
            _music.Start();
            SynchronizeRunPhase();

            // One line per boot for the Player log (WP-021 smoke evidence): what is wired.
            Debug.Log("[Audio] ready · " + _bridge.Describe() + " · " + _library.DescribeMemory()
                      + " · beds " + AmbienceDirector.BedCount + " (" + (_ambience.Started ? "playing" : "silent") + ")"
                      + " · music " + MusicDirector.LayerCount + " (" + (_music.Started ? "playing" : "silent") + ")"
                      + " · region map " + (AudioRegionMap.Load() != null ? AudioRegionMap.Load().Polygons.Count + " polygons" : "none"));
        }

        protected override void OnEnable()
        {
            s_instance = this;
            base.OnEnable();

            AudioEvents.Cue += OnCombatCue;
            _casings?.Enable();   // WP-072 B2
            _impacts?.Enable();   // WP-072 B3
            _flight?.Enable();    // WP-072 B5
            AudioEvents.LoopStoppedAt += OnLoopStopped;
            AudioEvents.CueSuppressed += OnCueSuppressed;

            RunAudioEvents.Cue += OnRunCue;
            RunEvents.PhaseChanged += OnPhaseChanged;
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;

            PlayerHealth.AnyDamaged += OnPlayerDamaged;
            PlayerHealth.AnyPlayerDied += OnPlayerDied;

            DD.Prototype.UI.UiSettings.MasterVolumeChanged += RequestMasterVolume;   // WP-021C (S-09 contract)
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;   // WP-065E evidence
            DD.Prototype.Weapons.WeaponEvents.SpecialWeaponEnded += StopAllFieldLoops;

            _router?.Subscribe();
            _listener?.Subscribe();

            RegisterDebugPanel();
            SynchronizeRunPhase();
        }

        protected override void OnDisable()
        {
            AudioEvents.Cue -= OnCombatCue;
            _casings?.Disable();   // WP-072 B2
            _impacts?.Disable();   // WP-072 B3
            _flight?.Disable();    // WP-072 B5
            AudioEvents.LoopStoppedAt -= OnLoopStopped;
            AudioEvents.CueSuppressed -= OnCueSuppressed;

            RunAudioEvents.Cue -= OnRunCue;
            RunEvents.PhaseChanged -= OnPhaseChanged;
            RunEvents.RunStarted -= OnRunStarted;
            RunEvents.RoundStarted -= OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted;

            PlayerHealth.AnyDamaged -= OnPlayerDamaged;
            PlayerHealth.AnyPlayerDied -= OnPlayerDied;

            DD.Prototype.UI.UiSettings.MasterVolumeChanged -= RequestMasterVolume;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            DD.Prototype.Weapons.WeaponEvents.SpecialWeaponEnded -= StopAllFieldLoops;

            _router?.Unsubscribe();
            _listener?.Unsubscribe();

            StopAllLoops();
            StopAllFieldLoops();
            AudioListener.pause = false;
            _mixer.Muted = false;
            _paused = false;

            UnregisterDebugPanel();
            base.OnDisable();

            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void OnDestroy()
        {
            AudioListener.pause = false;
            _library?.Dispose();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            _configuredBudget = Mathf.Clamp(config.Feedback.MaxCombatAudioVoices, 4, WorldHardCapacity);

            if (_world != null)
            {
                _world.Capacity = _budgetOverride > 0 ? _budgetOverride : _configuredBudget;
            }
        }

        // ================================================================== per frame

        private void Update()
        {
            // Unscaled throughout. A hit stop drops the time scale to 0.06 for 45 ms and
            // audio must not stretch with it - the whole point of a hit stop is that the
            // sound keeps going while the picture stops.
            float dt = Time.unscaledDeltaTime;

            UpdatePause();
            TickMasterVolume(dt);

            _mixer.Tick(dt);
            _world.Tick(dt, _paused);
            _signal.Tick(dt, false);   // WP-068 s11: interface voices ignore the listener pause, so they age through it
            _ambience.Tick(dt, _paused);
            _music.Tick(dt, _paused);

            if (!_paused)
            {
                _router.Tick();
            }

            TickLoops(dt);
            if (!_paused) _weaponFire.Tick(dt);   // WP-072 B1: trigger release → body loop fade + tail
            if (!_paused) _casings.Tick();         // WP-072 B2: casings land after their fall
            if (!_paused) _flight.Tick(dt);        // WP-072 B5: rocket / orb / gel loops follow their projectile
            TickFieldLoops();
            TickUiVoices(dt);

            if (_deathInkAt > 0f && Time.unscaledTime >= _deathInkAt)
            {
                _deathInkAt = -1f;
                PlayResolved(AudioMixPlan.UiEvent(AudioSoundId.UiInkBleed), Vector3.zero, 1f, 40);   // WP-068: ink under the death stinger
            }
        }

        private void LateUpdate()
        {
            // After the player moved and the camera followed (ADR-021-2).
            _listener.LateTick();

            // WP-072 B3: legacy surface cues no projectile impact replaced this frame play now (one frame late).
            int n = _impacts.DrainUnreplaced(_heldImpacts);
            for (int i = 0; i < n; i++)
            {
                PlayCombatCue(_heldImpacts[i]);
            }
        }

        /// <summary>WP-021C: ramp the exposed master parameter towards the requested value (linear domain, then dB).</summary>
        private void TickMasterVolume(float dt)
        {
            if (!MixerOwnsMaster || Mathf.Approximately(_masterApplied, _masterTarget))
            {
                return;
            }

            float next = _masterApplied < 0f
                ? _masterTarget
                : Mathf.MoveTowards(_masterApplied, _masterTarget, dt / MasterRampSeconds);
            _bridge.SetVolume(AudioMixerBridge.GroupMaster, next);
            _masterApplied = next;
        }

        /// <summary>
        /// Requirement: "pause sesi de dondurur".
        ///
        /// Deliberately NOT <c>RunFlowClock.IsFrozen</c>. That is true for the round-end
        /// freeze and the upgrade screen too, and both of those are supposed to keep
        /// playing - the upgrade screen is where the request wants the atmosphere to
        /// calm down, which it cannot do while muted. Only the pause hold silences audio.
        /// </summary>
        private void UpdatePause()
        {
            bool paused = RunFlowClock.HasHold(RunFlowClock.PauseHold);
            if (paused == _paused)
            {
                return;
            }

            _paused = paused;
            AudioListener.pause = paused;
            _mixer.Muted = paused;

            // WP-068 s11: pause = the short fusuma slide + stop, resume = the opening slide (Interface pool, heard through the listener pause).
            PlayResolved(AudioMixPlan.UiEvent(paused ? AudioSoundId.UiPauseOpen : AudioSoundId.UiPauseClose), Vector3.zero, 1f, 60);

            // WP-021: the Pause snapshot on top of the listener pause, so a UI cue raised
            // from the pause menu (which the listener pause does not stop) sits in a quiet
            // room, and the world comes back over a short ramp on resume.
            if (paused)
            {
                _snapshotBeforePause = _bridge.Current;
                _bridge.Transition(AudioSnapshot.Pause, 0.15f);
            }
            else
            {
                _bridge.Transition(_snapshotBeforePause, 0.6f);
            }
        }

        /// <summary>Phase → mixer snapshot (WP-021 §2). The pause hold overrides it while held.</summary>
        private static AudioSnapshot SnapshotFor(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Fighting:
                case RunPhase.Countdown:
                    return AudioSnapshot.Run;
                case RunPhase.RoundComplete:
                case RunPhase.Upgrading:
                    return AudioSnapshot.RoundBreak;
                default:
                    return AudioSnapshot.Menu;
            }
        }

        // ================================================================== combat

        /// <summary>WP-072 B3: a held legacy impact cue, played through the normal path once (no second hold, no second telemetry row).</summary>
        private void PlayCombatCue(in CombatAudioCue cue)
        {
            _releasingHeld = true;
            try
            {
                OnCombatCue(cue);
            }
            finally
            {
                _releasingHeld = false;
            }
        }

        private bool _releasingHeld;

        private void OnCombatCue(CombatAudioCue cue)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!_releasingHeld) AudioPlaybackTelemetry.RecordCombatCue(cue.Event, false);
#endif
            if (_paused)
            {
                return;
            }

            if (cue.IsLoop)
            {
                StartLoop(cue);
                return;
            }

            if (cue.Event == CombatAudioEvent.SpecialWeaponPickup)
            {
                _music.OnSpecialPickup();   // WP-057 stinger over the layers
            }

            // WP-072 B1: a fire cue that names its weapon speaks with that weapon's v016 voice;
            // without produced clips it falls through to the legacy intensity-picked sound.
            if (cue.Event == CombatAudioEvent.ClassicWeaponFire && _weaponFire.TryHandle(cue))
            {
                return;
            }

            // WP-072 B3: a world impact waits for the projectile's own calibre voice (same call chain, one line later).
            if (cue.Event >= CombatAudioEvent.SurfaceImpactDefault && cue.Event <= CombatAudioEvent.SurfaceImpactPaper && !_releasingHeld && _impacts.TryHold(cue))
            {
                return;
            }

            // WP-072 B5: an explosion is heard as one of three distance layers (STANDARDS §7, picked once at the blast).
            if (cue.Event == CombatAudioEvent.Explosion && _library.IsProduced(AudioSoundId.ExplosionNear))
            {
                AudioPlacement ep = AudioMixPlan.Resolve(cue, false);
                ep.Sound = ExplosionLayer(Vector3.Distance(_listener.Transform.position, cue.Position));
                if (cue.Position == Vector3.zero) ep.Spatial = 0f;
                PlayResolved(ep, cue.Position, Mathf.Clamp(cue.Intensity <= 0f ? 1f : cue.Intensity, 0.25f, 1.4f), cue.Priority);
                return;
            }

            // WP-072 B2: dry fire speaks with the equipped weapon's weight family (hammer / firing pin).
            if (cue.Event == CombatAudioEvent.ClassicWeaponDryFire)
            {
                string audioId = WeaponFoleyV3.Equipped();
                AudioSoundId dry = WeaponFoleyV3.DryFire(WeaponFoleyV3.Family(audioId));
                if (_library.IsProduced(dry))
                {
                    AudioPlacement dp = AudioMixPlan.WeaponFoley(dry);
                    dp.Pitch = WeaponFoleyV3.Pitch(WeaponFoleyV3.HandlingSemitones(audioId));
                    PlayResolved(dp, cue.Position, 1f, cue.Priority);
                    return;
                }
            }

            bool failing = false;
            if (cue.Event == CombatAudioEvent.ArmorHit)
            {
                failing = _armour.RegisterHit(cue.Position);
            }
            else if (cue.Event == CombatAudioEvent.ArmorBreak)
            {
                // The plate is gone. Whatever wears armour next starts at rung one.
                _armour.Forget(cue.Position);
            }

            AudioPlacement placement = AudioMixPlan.Resolve(cue, failing);
            if (!placement.IsValid)
            {
                return;
            }

            // Intensity is a loudness hint for everything EXCEPT classic weapon fire, where
            // the mix plan already spent it choosing which of the three guns is speaking.
            float gain = cue.Event == CombatAudioEvent.ClassicWeaponFire
                ? 1f
                : Mathf.Clamp(cue.Intensity <= 0f ? 1f : cue.Intensity, 0.25f, 1.4f);

            // CombatAudioCue's own contract: Vector3.zero means "non-positional". The loop
            // path honoured it (StartLoop), the one-shot path did not, so the reload start /
            // end cues - raised without a position by ClassicWeaponRuntime - were played
            // spatially from the world origin instead of on the player (WP-002, AUD-04).
            if (cue.Position == Vector3.zero)
            {
                placement.Spatial = 0f;
            }

            PlayResolved(placement, cue.Position, gain, cue.Priority);
        }

        private void OnCueSuppressed(CombatAudioCue cue)
        {
            // Diagnosis only. G-03 already decided this cue is a duplicate of one that is
            // playing; making a sound here would undo the decision.
            _suppressedSeen++;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackTelemetry.RecordCombatCue(cue.Event, true);
#endif
        }

        // ================================================================== run flow

        /// <summary>
        /// Adopts the authoritative phase when this persistent director is created or found
        /// after a Run scene has already advanced. The event path remains the normal path;
        /// this closes only the late-subscriber gap and deliberately skips its one-shot cues.
        /// </summary>
        private void SynchronizeRunPhase()
        {
            RunDirector run = RunDirector.Instance;
            if (run == null)
            {
                return;
            }

            RunPhase phase = run.Phase;
            _ambience.SetPhase(phase);
            _music.SetPhase(phase);

            AudioSnapshot snapshot = SnapshotFor(phase);
            if (_paused)
            {
                _snapshotBeforePause = snapshot;
            }
            else
            {
                _bridge.Transition(snapshot, 0f);
            }
        }

        private void OnRunCue(RunAudioCue cue)
        {
            if (_paused)
            {
                return;
            }

            if (cue.Event == RunAudioEvent.RunEnded)
            {
                _music.OnRunEnded(false);   // WP-057: results music + run-end stinger (death already handled in OnPlayerDied)
            }

            AudioPlacement placement = AudioMixPlan.Resolve(cue);
            if (!placement.IsValid)
            {
                return;
            }

            float gain = Mathf.Clamp(cue.Intensity <= 0f ? 1f : cue.Intensity, 0.25f, 1.4f);
            PlayResolved(placement, cue.Position, gain, 60);
        }

        private void OnPhaseChanged(RunPhase phase)
        {
            _ambience.SetPhase(phase);
            _music.SetPhase(phase);

            AudioSnapshot snapshot = SnapshotFor(phase);
            if (_paused)
            {
                _snapshotBeforePause = snapshot;
            }
            else
            {
                _bridge.Transition(snapshot, phase == RunPhase.Fighting ? 0.8f : 2.0f);
            }

            if (phase == RunPhase.Dead || phase == RunPhase.None)
            {
                StopAllLoops();
                StopAllFieldLoops();
                _armour.Clear();
                _router.Clear();
            }

            // v003: the screen transition (menu ↔ run ↔ results) has its own cue, on the same
            // frame as the snapshot change (matrix §8).
            if (phase == RunPhase.None)
            {
                PlayResolved(AudioMixPlan.UiTransition(), Vector3.zero, 1f, 80);
            }
        }

        private void OnRunStarted(int runId)
        {
            _deathInkAt = -1f;
            StopAllFieldLoops();
            // v003: entering the run is a screen transition (matrix §8).
            PlayResolved(AudioMixPlan.UiTransition(), Vector3.zero, 1f, 80);
            _music.OnRunStarted();   // WP-057: leave the results state
        }

        private void OnRoundStarted(int round)
        {
            _music.OnRoundStarted();
            _armour.Clear();
        }

        private void OnRoundCompleted(int round)
        {
            _music.OnRoundCompleted();
            StopAllLoops();
        }

        // ================================================================== player

        private void OnPlayerDamaged(PlayerDamageInfo info)
        {
            if (_paused)
            {
                return;
            }

            // Scaled by how much of the remaining health went, not by the raw number: the
            // last 10 HP leaving is a far bigger event than the first 10, and the sound
            // should say so without the HUD having to.
            float severity = info.MaxHealth > 0f
                ? Mathf.Clamp01(info.AppliedAmount / (info.MaxHealth * 0.25f))
                : 1f;

            float lowHealth = info.MaxHealth > 0f ? 1f - Mathf.Clamp01(info.HealthAfter / info.MaxHealth) : 0f;

            AudioPlacement placement = AudioMixPlan.PlayerDamage();
            PlayResolved(placement, Vector3.zero, Mathf.Clamp(0.55f + 0.45f * severity + 0.25f * lowHealth, 0.4f, 1.3f), 100);
        }

        /// <summary>WP-065E: audio configuration / device resets seen (every playing source stops on one; the score and beds self-heal).</summary>
        public static int AudioConfigurationChanges { get; private set; }

        private static void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            AudioConfigurationChanges++;
        }

        private void OnPlayerDied(PlayerDeathInfo info)
        {
            _deathInkAt = Time.unscaledTime + DeathInkDelay;   // WP-068: the ink texture follows the stinger by 0.6 s
            StopAllLoops();
            StopAllFieldLoops();
            PlayResolved(AudioMixPlan.PlayerDeath(), Vector3.zero, 1f, 100);
            _music.OnRunEnded(true);   // WP-057: death stinger, then the results loop
        }

        // ================================================================== playback

        /// <summary>
        /// The single funnel every sound goes through. Chooses the pool, deals a
        /// variation, applies the duck the sound is entitled to, and hands it to the
        /// budget.
        /// </summary>
        private bool PlayResolved(in AudioPlacement placement, Vector3 position, float gainScale, int cuePriority)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackPool evidencePool = placement.Bus == AudioBus.Interface ? AudioPlaybackPool.Signal : AudioPlaybackPool.World;
            AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.Requested, evidencePool, placement.Sound,
                active: evidencePool == AudioPlaybackPool.Signal ? _signal.ActiveCount : _world.ActiveCount,
                capacity: evidencePool == AudioPlaybackPool.Signal ? _signal.Capacity : _world.Capacity);
#endif
            AudioClip clip = _library.Next(placement.Sound, out int _);
            if (clip == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.MissingClip, evidencePool, placement.Sound,
                    active: evidencePool == AudioPlaybackPool.Signal ? _signal.ActiveCount : _world.ActiveCount,
                    capacity: evidencePool == AudioPlaybackPool.Signal ? _signal.Capacity : _world.Capacity);
#endif
                return false;
            }

            float priority = AudioMixPlan.EffectivePriority(placement, cuePriority);

            AudioVoicePool pool = placement.Bus == AudioBus.Interface ? _signal : _world;
            bool played = pool.Play(placement, clip, position, gainScale, priority);

            if (played)
            {
                ApplyDuck(placement);
                PlayedCount++;
                LastPlayed = placement.Sound;
            }

            return played;
        }

        /// <summary>
        /// The sidechain. A landmark event makes room for itself by pulling the buses
        /// underneath it down for a moment; nothing here ever raises a level.
        /// </summary>
        private void ApplyDuck(in AudioPlacement placement)
        {
            if (placement.DuckSeconds <= 0f)
            {
                return;
            }

            if (placement.DuckAmbience < 1f)
            {
                _mixer.Duck(AudioBus.Ambience, placement.DuckAmbience, placement.DuckSeconds);
            }

            if (placement.DuckCombat < 1f)
            {
                _mixer.Duck(AudioBus.Weapon, placement.DuckCombat, placement.DuckSeconds);
                _mixer.Duck(AudioBus.Impact, placement.DuckCombat * 0.92f, placement.DuckSeconds);
                _mixer.Duck(AudioBus.Target, placement.DuckCombat, placement.DuckSeconds);
            }

            if (placement.DuckMusic > 0f && placement.DuckMusic < 1f)
            {
                _mixer.Duck(AudioBus.Music, placement.DuckMusic, placement.DuckSeconds);   // WP-057
            }
        }

        /// <summary>Escape hatch for the UI task and the F1 panel. Bypasses no budget.</summary>
        public bool PlayDirect(AudioSoundId sound, Vector3 position, float gain = 1f)
        {
            AudioPlacement placement = AudioMixPlan.Ui(sound);
            placement.Spatial = position == Vector3.zero ? 0f : 0.7f;
            return PlayResolved(placement, position, gain, 50);
        }

        // ================================================================== WP-058 · Special field loops

        /// <summary>One Ofuda source plus two Tide sources (the shipped MaxWells); Kagura's front is a one-shot.</summary>
        private const int TidePrimarySlot = 2;
        private const int TideSecondarySlot = 3;
        private readonly AudioSource[] _fieldLoops = new AudioSource[4];
        private readonly AudioSoundId[] _fieldLoopSound = new AudioSoundId[4];
        public int FieldLoopsPlaying { get { int n = 0; for (int i = 0; i < _fieldLoops.Length; i++) if (_fieldLoops[i] != null && _fieldLoops[i].isPlaying) n++; return n; } }

        private void BuildFieldLoops()
        {
            for (int i = 0; i < _fieldLoops.Length; i++)
            {
                var go = new GameObject("Special Field " + i);
                go.transform.SetParent(transform, false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.loop = true; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 4f; src.maxDistance = 40f; src.dopplerLevel = 0f; src.priority = 24; src.volume = 0f;
                _bridge.Route(src, AudioBus.Impact);
                _fieldLoops[i] = src;
            }
        }

        private bool StartFieldLoop(AudioSoundId sound, Vector3 at, int slot)
        {
            if (slot < 0 || slot > TidePrimarySlot) return false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.Requested, AudioPlaybackPool.Field, sound,
                active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
            int sourceSlot = slot;
            if (slot == TidePrimarySlot && _fieldLoops[TidePrimarySlot].isPlaying)
            {
                if (_fieldLoops[TideSecondarySlot].isPlaying)
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.BudgetDropped, AudioPlaybackPool.Field, sound,
                        active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
                    return false;
                }

                sourceSlot = TideSecondarySlot;
            }
            AudioClip clip = _library.First(sound);
            if (clip == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.MissingClip, AudioPlaybackPool.Field, sound,
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
                return false;
            }
            AudioSource src = _fieldLoops[sourceSlot];
            src.transform.position = at;
            if (src.clip != clip) src.clip = clip;
            _fieldLoopSound[sourceSlot] = sound;
            src.volume = 0.7f * _mixer.Gain(AudioBus.Impact);
            if (!src.isPlaying)
            {
                src.Play();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStarted, AudioPlaybackPool.Field, sound,
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
            }
            return true;
        }

        private void StopFieldLoop(int slot, Vector3 at)
        {
            if (slot < 0 || slot > TidePrimarySlot) return;
            int sourceSlot = slot;
            if (slot == TidePrimarySlot && _fieldLoops[TideSecondarySlot].isPlaying)
            {
                float primaryDistance = _fieldLoops[TidePrimarySlot].isPlaying
                    ? (_fieldLoops[TidePrimarySlot].transform.position - at).sqrMagnitude
                    : float.MaxValue;
                float secondaryDistance = (_fieldLoops[TideSecondarySlot].transform.position - at).sqrMagnitude;
                sourceSlot = secondaryDistance < primaryDistance ? TideSecondarySlot : TidePrimarySlot;
            }

            bool wasPlaying = _fieldLoops[sourceSlot].isPlaying;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (wasPlaying)
            {
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStopRequested, AudioPlaybackPool.Field, _fieldLoopSound[sourceSlot],
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
            }
#endif
            _fieldLoops[sourceSlot].Stop();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (wasPlaying)
            {
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopEnded, AudioPlaybackPool.Field, _fieldLoopSound[sourceSlot],
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
            }
#endif
        }

        private void StopAllFieldLoops()
        {
            for (int i = 0; i < _fieldLoops.Length; i++)
            {
                if (_fieldLoops[i] == null || !_fieldLoops[i].isPlaying) continue;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStopRequested, AudioPlaybackPool.Field, _fieldLoopSound[i],
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
                _fieldLoops[i].Stop();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopEnded, AudioPlaybackPool.Field, _fieldLoopSound[i],
                    active: FieldLoopsPlaying, capacity: _fieldLoops.Length);
#endif
            }
        }

        // ================================================================== WP-068 s11 · controllable UI voices

        /// <summary>Delay of the death ink texture after the death stinger (E22: ink 0.6 → 2.0 s).</summary>
        public const float DeathInkDelay = 0.6f;
        private float _deathInkAt = -1f;

        private const int UiVoiceCount = 2;
        private readonly AudioSource[] _uiVoices = new AudioSource[UiVoiceCount];
        private readonly AudioSoundId[] _uiVoiceSound = new AudioSoundId[UiVoiceCount];
        private readonly float[] _uiVoiceLevel = new float[UiVoiceCount];
        private readonly float[] _uiVoiceBase = new float[UiVoiceCount];
        private readonly float[] _uiVoiceStopAt = new float[UiVoiceCount];
        private readonly float[] _uiVoiceFade = new float[UiVoiceCount];   // > 0: fading out at this rate (level / s)

        /// <summary>UI voices sounding (roll / hold / boot seal). Test readout.</summary>
        public int UiVoicesPlaying { get { int n = 0; for (int i = 0; i < UiVoiceCount; i++) if (_uiVoices[i] != null && _uiVoices[i].isPlaying) n++; return n; } }
        public bool UiVoicePlaying(AudioSoundId sound) { for (int i = 0; i < UiVoiceCount; i++) if (_uiVoiceSound[i] == sound && _uiVoices[i] != null && _uiVoices[i].isPlaying) return true; return false; }

        private void BuildUiVoices()
        {
            for (int i = 0; i < UiVoiceCount; i++)
            {
                var go = new GameObject("UI Voice " + i);
                go.transform.SetParent(transform, false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f; src.dopplerLevel = 0f; src.priority = 16; src.volume = 0f;
                src.ignoreListenerPause = true; src.bypassReverbZones = true;
                _bridge.Route(src, AudioBus.Interface);
                _uiVoices[i] = src;
            }
        }

        /// <summary>Starts (or restarts) a controllable UI voice; the same sound reuses its voice, otherwise a free one.</summary>
        private bool StartUiVoice(AudioSoundId sound, bool loop, float maxSeconds)
        {
            AudioClip clip = _library.Next(sound, out int _);
            if (clip == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.MissingClip, AudioPlaybackPool.Signal, sound, active: UiVoicesPlaying, capacity: UiVoiceCount);
#endif
                return false;
            }

            int slot = -1;
            for (int i = 0; i < UiVoiceCount && slot < 0; i++) if (_uiVoiceSound[i] == sound) slot = i;
            for (int i = 0; i < UiVoiceCount && slot < 0; i++) if (!_uiVoices[i].isPlaying) slot = i;
            if (slot < 0) slot = 0;   // both busy with other sounds: the older voice gives way

            AudioPlacement placement = AudioMixPlan.UiEvent(sound);
            AudioSource src = _uiVoices[slot];
            src.Stop();
            src.clip = clip;
            src.loop = loop;
            src.pitch = 1f;
            _uiVoiceSound[slot] = sound;
            _uiVoiceBase[slot] = placement.Gain;
            _uiVoiceLevel[slot] = 1f;
            _uiVoiceFade[slot] = 0f;
            _uiVoiceStopAt[slot] = Time.unscaledTime + (loop ? maxSeconds : Mathf.Min(maxSeconds, clip.length + 0.05f));
            src.volume = placement.Gain * _mixer.Gain(AudioBus.Interface);
            src.Play();
            PlayedCount++;
            LastPlayed = sound;
            return true;
        }

        /// <summary>Fades a UI voice out (None = all). A voice already silent is left alone.</summary>
        private void StopUiVoice(AudioSoundId sound, float fadeSeconds)
        {
            for (int i = 0; i < UiVoiceCount; i++)
            {
                if ((sound == AudioSoundId.None || _uiVoiceSound[i] == sound) && _uiVoices[i] != null && _uiVoices[i].isPlaying)
                {
                    _uiVoiceFade[i] = 1f / Mathf.Max(0.005f, fadeSeconds);
                }
            }
        }

        private void TickUiVoices(float dt)
        {
            float g = _mixer.Gain(AudioBus.Interface);
            float now = Time.unscaledTime;
            for (int i = 0; i < UiVoiceCount; i++)
            {
                AudioSource src = _uiVoices[i];
                if (src == null || !src.isPlaying)
                {
                    continue;
                }

                if (_uiVoiceFade[i] <= 0f && now >= _uiVoiceStopAt[i])
                {
                    _uiVoiceFade[i] = 1f / 0.03f;   // the roll / hold ends in 30 ms at its cap
                }

                if (_uiVoiceFade[i] > 0f)
                {
                    _uiVoiceLevel[i] -= _uiVoiceFade[i] * dt;
                    if (_uiVoiceLevel[i] <= 0f)
                    {
                        src.Stop();
                        src.clip = null;
                        _uiVoiceSound[i] = AudioSoundId.None;
                        _uiVoiceFade[i] = 0f;
                        continue;
                    }
                }

                src.volume = _uiVoiceBase[i] * _uiVoiceLevel[i] * g;
            }
        }

        private void TickFieldLoops()
        {
            float g = _mixer.Gain(AudioBus.Impact);
            for (int i = 0; i < _fieldLoops.Length; i++)
            {
                if (_fieldLoops[i] != null && _fieldLoops[i].isPlaying) _fieldLoops[i].volume = 0.7f * g;
            }
        }

        // ================================================================== loops

        private sealed class LoopVoice
        {
            public AudioSource Source;
            public AudioSoundId Sound;
            public float Volume;
            public float TargetVolume;
            public float Pitch;
            public float TargetPitch;
            public float RampSeconds;
            public bool Stopping;
            public int Claims;
            public Vector3 Claim0;
            public Vector3 Claim1;
            public Vector3 Claim2;
            public Vector3 Claim3;

            public void AddClaim(Vector3 position)
            {
                int index = Mathf.Min(Claims, 3);
                SetClaim(index, position);
                Claims = Mathf.Min(Claims + 1, 4);
            }

            public bool RemoveNearestClaim(Vector3 position)
            {
                if (Claims <= 0) return false;

                int nearest = 0;
                float nearestDistance = (ClaimAt(0) - position).sqrMagnitude;
                for (int i = 1; i < Claims; i++)
                {
                    float distance = (ClaimAt(i) - position).sqrMagnitude;
                    if (distance < nearestDistance)
                    {
                        nearest = i;
                        nearestDistance = distance;
                    }
                }

                for (int i = nearest; i < Claims - 1; i++) SetClaim(i, ClaimAt(i + 1));
                Claims--;
                return true;
            }

            public Vector3 ClaimAt(int index)
            {
                switch (index)
                {
                    case 0: return Claim0;
                    case 1: return Claim1;
                    case 2: return Claim2;
                    default: return Claim3;
                }
            }

            private void SetClaim(int index, Vector3 value)
            {
                switch (index)
                {
                    case 0: Claim0 = value; break;
                    case 1: Claim1 = value; break;
                    case 2: Claim2 = value; break;
                    default: Claim3 = value; break;
                }
            }
        }

        /// <summary>
        /// Starts a loop.
        ///
        /// The rotary cannon's SPIN-UP is not a clip. The motor loop is baked at full
        /// speed and this ramps pitch 0.45 -> 1.0 and volume 0 -> target over 0.55 s, which
        /// is what a flywheel does. Two reasons it is done here rather than in the recipe:
        /// the spin-up length can then follow the weapon's real timing without re-baking
        /// anything, and the motor is genuinely one continuous sound for the whole
        /// eleven-second window instead of a spin-up clip and a loop meeting at a seam.
        /// </summary>
        private void StartLoop(in CombatAudioCue cue)
        {
            AudioPlacement placement = AudioMixPlan.Resolve(cue, false);
            if (!placement.IsValid)
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.Requested, AudioPlaybackPool.Loop, placement.Sound,
                active: _loops.Count, capacity: LoopCapacity, combatEvent: cue.Event);
#endif

            if (_loops.TryGetValue(cue.Event, out LoopVoice existing))
            {
                existing.AddClaim(cue.Position);
                existing.Stopping = false;
                existing.TargetVolume = Mathf.Max(existing.TargetVolume,
                    placement.Gain * Mathf.Clamp(cue.Intensity <= 0f ? 1f : cue.Intensity, 0.2f, 1.2f));
                existing.Source.transform.position = cue.Position;
                if (!existing.Source.isPlaying) existing.Source.Play();
                return;
            }

            AudioClip clip = _library.First(placement.Sound);
            if (clip == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.MissingClip, AudioPlaybackPool.Loop, placement.Sound,
                    active: _loops.Count, capacity: LoopCapacity, combatEvent: cue.Event);
#endif
                return;
            }

            if (_loops.Count >= LoopCapacity)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.BudgetDropped, AudioPlaybackPool.Loop, placement.Sound,
                    active: _loops.Count, capacity: LoopCapacity, combatEvent: cue.Event);
#endif
                return;
            }

            var go = new GameObject("Loop " + cue.Event);
            go.transform.SetParent(_loopRoot, false);
            go.transform.position = cue.Position;

            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = cue.Position == Vector3.zero ? 0f : placement.Spatial;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 6f;
            source.maxDistance = 45f;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.priority = 40;
            _bridge.Route(source, BusForLoop(placement.Sound));

            bool spinUp = placement.Sound == AudioSoundId.HeavyMotorLoop;

            var voice = new LoopVoice
            {
                Source = source,
                Sound = placement.Sound,
                Volume = 0f,
                TargetVolume = placement.Gain * Mathf.Clamp(cue.Intensity <= 0f ? 1f : cue.Intensity, 0.2f, 1.2f),
                Pitch = spinUp ? 0.45f : 1f,
                TargetPitch = 1f,
                RampSeconds = spinUp ? 0.55f : 0.25f,
                Claims = 0
            };

            voice.AddClaim(cue.Position);

            source.pitch = voice.Pitch;
            source.volume = 0f;
            source.Play();

            _loops.Add(cue.Event, voice);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStarted, AudioPlaybackPool.Loop, placement.Sound,
                active: _loops.Count, capacity: LoopCapacity, combatEvent: cue.Event);
#endif
        }

        private void OnLoopStopped(CombatAudioEvent evt, Vector3 position)
        {
            if (_loops.TryGetValue(evt, out LoopVoice voice))
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStopRequested, AudioPlaybackPool.Loop, voice.Sound,
                    active: _loops.Count, capacity: LoopCapacity, combatEvent: evt);
#endif
                voice.RemoveNearestClaim(position);
                if (voice.Claims > 0)
                {
                    voice.Source.transform.position = voice.Claim0;
                    return;
                }

                // Fade rather than stop. A looping bed cut at an arbitrary sample is a
                // click, and the beacon loop stops every time a player picks a weapon up -
                // which is the exact frame they are paying attention to.
                voice.Stopping = true;
                voice.TargetVolume = 0f;
                voice.RampSeconds = 0.18f;
            }
        }

        private void TickLoops(float unscaledDeltaTime)
        {
            if (_loops.Count == 0 || _paused)
            {
                return;
            }

            List<CombatAudioEvent> finished = null;

            foreach (KeyValuePair<CombatAudioEvent, LoopVoice> pair in _loops)
            {
                LoopVoice v = pair.Value;
                float step = unscaledDeltaTime / Mathf.Max(0.02f, v.RampSeconds);

                v.Volume = Mathf.MoveTowards(v.Volume, v.TargetVolume, step);
                v.Pitch = Mathf.MoveTowards(v.Pitch, v.TargetPitch, step);

                v.Source.volume = v.Volume * _mixer.Gain(BusForLoop(v.Sound));
                v.Source.pitch = v.Pitch;

                if (v.Stopping && v.Volume <= 0.001f)
                {
                    (finished ?? (finished = new List<CombatAudioEvent>(2))).Add(pair.Key);
                }
            }

            if (finished == null)
            {
                return;
            }

            for (int i = 0; i < finished.Count; i++)
            {
                DestroyLoop(finished[i]);
            }
        }

        private static AudioBus BusForLoop(AudioSoundId sound)
        {
            return sound == AudioSoundId.HeavyMotorLoop ? AudioBus.Weapon : AudioBus.Interface;
        }

        private void DestroyLoop(CombatAudioEvent evt)
        {
            if (!_loops.TryGetValue(evt, out LoopVoice voice))
            {
                return;
            }

            _loops.Remove(evt);

            if (voice.Source != null)
            {
                voice.Source.Stop();
                Destroy(voice.Source.gameObject);
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopEnded, AudioPlaybackPool.Loop, voice.Sound,
                active: _loops.Count, capacity: LoopCapacity, combatEvent: evt);
#endif
        }

        private void StopAllLoops()
        {
            _weaponFire?.StopAll();   // WP-072 B1
            _casings?.Clear();        // WP-072 B2
            _flight?.StopAll();       // WP-072 B5
            if (_loops.Count == 0)
            {
                return;
            }

            var keys = new List<CombatAudioEvent>(_loops.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_loops.TryGetValue(keys[i], out LoopVoice voice))
                {
                    AudioPlaybackTelemetry.Record(AudioPlaybackOutcome.LoopStopRequested, AudioPlaybackPool.Loop, voice.Sound,
                        active: _loops.Count, capacity: LoopCapacity, combatEvent: keys[i]);
                }
#endif
                DestroyLoop(keys[i]);
            }
        }

        // ================================================================== F1 panel

        private void RegisterDebugPanel()
        {
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Voices", DescribeVoices, -100));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Dropped", DescribeDropped, -99));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Cues", DescribeCues, -98));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Ambience", () => _ambience.Describe(), -97));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Music", () => _music.Describe(), -97));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Zombie vocals", () => _router.Describe(), -97));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Mixer", () => _bridge.Describe() + " · " + _listener.Describe(), -96));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Library", () => _library.DescribeMemory(), -96));
            _debugEntries.Add(DebugPanelRegistry.AddReadout(AudioTab, "Buses", DescribeBuses, -95));

            _debugEntries.Add(DebugPanelRegistry.AddSlider(AudioTab, "Master volume",
                () => PrototypeAudio.MasterVolume, v => PrototypeAudio.MasterVolume = v, 0f, 1f, false, 0));

            AddBusSlider(AudioBus.Weapon, "Weapon bus", 1);
            AddBusSlider(AudioBus.Impact, "Impact bus", 2);
            AddBusSlider(AudioBus.Target, "Target bus", 3);
            AddBusSlider(AudioBus.Interface, "Interface bus", 4);
            AddBusSlider(AudioBus.Ambience, "Ambience bus", 5);
            AddBusSlider(AudioBus.Music, "Music bus", 6);

            _debugEntries.Add(DebugPanelRegistry.AddSlider(AudioTab, "Voice budget (0 = config)",
                () => _budgetOverride,
                v =>
                {
                    _budgetOverride = Mathf.RoundToInt(v);
                    _world.Capacity = _budgetOverride > 0 ? _budgetOverride : _configuredBudget;
                },
                0f, WorldHardCapacity, true, 6));

            _debugEntries.Add(DebugPanelRegistry.AddButton(AudioTab, "Reset counters", () =>
            {
                _world.ResetCounters();
                _signal.ResetCounters();
                AudioEvents.ResetCounters();
                _suppressedSeen = 0;
            }, 7));

            // The request's fifteen separable events, in one row each, so the "are they
            // actually distinguishable" test can be run without a zombie in sight.
            AddTestButton("1 Classic fire - SMG", AudioSoundId.SmgFire, 20);
            AddTestButton("1 Classic fire - Rifle", AudioSoundId.RifleFire, 21);
            AddTestButton("1 Classic fire - Shotgun", AudioSoundId.ShotgunFire, 22);
            AddTestButton("2 Heavy fire", AudioSoundId.HeavyFire, 23);
            AddTestButton("3 Flesh hit", AudioSoundId.FleshHit, 24);
            AddTestButton("4 Armor hit", AudioSoundId.ArmorImpact, 25);
            AddTestButton("4b Armor crack", AudioSoundId.ArmorCrack, 26);
            AddTestButton("5 Armor break", AudioSoundId.ArmorBreak, 27);
            AddTestButton("6 Enemy death", AudioSoundId.EnemyDeath, 28);
            AddTestButton("7 Elite/Ultra death", AudioSoundId.HeavyEnemyDeath, 29);
            AddTestButton("8 Player damage", AudioSoundId.PlayerDamage, 30);
            AddTestButton("9 Gold collection", AudioSoundId.GoldPickup, 31);
            AddTestButton("10 SW spawn gong", AudioSoundId.SwSpawnGong, 32);
            AddTestButton("11 SW final seconds", AudioSoundId.SwFinalSeconds, 33);
            AddTestButton("12 Countdown tick", AudioSoundId.RoundCountdownTick, 34);
            AddTestButton("12b Round start", AudioSoundId.RoundStart, 35);
            AddTestButton("13 Round complete", AudioSoundId.RoundComplete, 36);
            AddTestButton("14 UI navigate", AudioSoundId.UiNavigate, 37);
            AddTestButton("15 Explosion", AudioSoundId.Explosion, 38);
            AddTestButton("16 Zombie windup (Std)", AudioSoundId.ZombieWindupStandard, 39);
            AddTestButton("16b Zombie windup (Ultra)", AudioSoundId.ZombieWindupUltra, 40);
            AddTestButton("17 Zombie death (Std)", AudioSoundId.ZombieDeathStandard, 41);
            AddTestButton("18 Gold spawn", AudioSoundId.GoldSpawn, 42);
            AddTestButton("19 Armor break", AudioSoundId.ArmorBreak, 43);
        }

        private void AddBusSlider(AudioBus bus, string label, int order)
        {
            _debugEntries.Add(DebugPanelRegistry.AddSlider(AudioTab, label,
                () => _mixer.GetUserVolume(bus), v => _mixer.SetUserVolume(bus, v), 0f, 1f, false, order));
        }

        private void AddTestButton(string label, AudioSoundId sound, int order)
        {
            _debugEntries.Add(DebugPanelRegistry.AddButton(AudioTab, label, () =>
            {
                AudioPlacement placement = AudioMixPlan.Ui(sound);
                placement.Spatial = 0f;
                placement.MinRetrigger = 0f;
                placement.Priority = 100f;
                PlayResolved(placement, Vector3.zero, 1f, 100);
            }, order));
        }

        private void UnregisterDebugPanel()
        {
            for (int i = 0; i < _debugEntries.Count; i++)
            {
                DebugPanelRegistry.Remove(_debugEntries[i]);
            }

            _debugEntries.Clear();
        }

        private string DescribeVoices()
        {
            return _world.Describe() + "\n" + _signal.Describe() + "\n"
                   + string.Format("Loops: {0}/{1} · beds 6 · music 4 · {2}", _loops.Count, LoopCapacity,
                       _paused ? "PAUSED" : "running");
        }

        private string DescribeDropped()
        {
            return string.Format(
                "budget dropped {0} (world {1}, signal {2}) · retrigger-blocked {3} · stolen {4}",
                DroppedCues, _world.DroppedCount, _signal.DroppedCount,
                _world.RetriggerBlockedCount + _signal.RetriggerBlockedCount,
                _world.StolenCount + _signal.StolenCount);
        }

        private string DescribeCues()
        {
            return string.Format(
                "delivered {0} · proximity-suppressed {1} (G-03) · seen here {2}",
                AudioEvents.DeliveredCount, AudioEvents.SuppressedCount, _suppressedSeen);
        }

        private string DescribeBuses()
        {
            return string.Format(
                "duck  W {0:0.00}  I {1:0.00}  T {2:0.00}  UI {3:0.00}  Amb {4:0.00}",
                _mixer.DuckLevel(AudioBus.Weapon), _mixer.DuckLevel(AudioBus.Impact),
                _mixer.DuckLevel(AudioBus.Target), _mixer.DuckLevel(AudioBus.Interface),
                _mixer.DuckLevel(AudioBus.Ambience));
        }
    }
}
