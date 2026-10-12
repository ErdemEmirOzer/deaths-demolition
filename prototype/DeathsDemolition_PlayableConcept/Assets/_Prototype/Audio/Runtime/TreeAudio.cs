using DD.Prototype.Rounds;
using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-104 (R4) · one sound's voice LOD. The B4 risk: a run tree turns frequent moments (crit, leech, dodge, shield hit,
    /// ricochet) into dozens a second. Read from <see cref="AudioMixPlan.TreeLod"/> and enforced by <see cref="TreeAudio"/> before
    /// the pool sees the request.
    /// </summary>
    public struct AudioVoiceLod
    {
        /// <summary>Copies of the sound alive at once (≤ 4). Past it the new one is refused; nothing ringing is cut.</summary>
        public int MaxLive;

        /// <summary>Unscaled seconds before the sound may start again (the pool's retrigger clock).</summary>
        public float MinInterval;

        /// <summary>Starts allowed in any one-second window (≤ 8; 0 = no rate cap).</summary>
        public int PerSecond;

        /// <summary>A landmark refused by its gate waits this long (unscaled) for the gate to open instead of being lost (0 = dropped).</summary>
        public float HoldSeconds;

        public AudioVoiceLod(int maxLive, float minInterval, int perSecond, float holdSeconds)
        {
            MaxLive = maxLive;
            MinInterval = minInterval;
            PerSecond = perSecond;
            HoldSeconds = holdSeconds;
        }
    }

    /// <summary>
    /// WP-104 (R4) · the run tree's voice: the B0 contract (<see cref="TreeEvents"/>) → AP-011 v024 (IDS.md 1020–1063).
    ///
    ///   Proc            one-shot per <see cref="TreeProcKind"/> (<see cref="SoundFor"/>) at <c>TreeProcInfo.Point</c>, through the
    ///                   per-sound LOD gate: the same sound once per frame, a retrigger interval, a per-second rate and a
    ///                   concurrent ceiling (<see cref="AudioMixPlan.TreeLod"/>). Mini Nuke ≤ 1 per 3 s and Doomsday ≤ 1 alive
    ///                   both duck briefly through the placement (the director's sidechain).
    ///   FireGround /    a zone (≤ <see cref="TreeGuardrails.AreaZones"/>) living for the event's <c>Seconds</c> (B1), else the
    ///   VenomCloud      stats' FireGroundSeconds / VenomCloudSeconds, else a default. Only the <see cref="AudioMixPlan.TreeFieldVoices"/> zones nearest
    ///                   the listener are heard, on looping sources this class owns.
    ///   BlackHoleOpen / one looping source: open → the event's Seconds, else HoleSeconds (2.5 s default) → BlackHoleCollapse plays the collapse and cuts
    ///   Collapse        the loop. Without a collapse event the loop fades out by itself after its life.
    ///   NodePurchased / the tree screen's stamp and synergy (interface); AuraChanged plays only when the level goes up.
    ///   SynergyUnlocked / AuraChanged
    ///
    /// Clocks (the RocketBlastAudio lesson, C-00 10.10): every gate runs on unscaled time, the clock of the pool's own retrigger
    /// gate, so a hit stop or a long frame can never let a burst past one gate and into the other. Zone and hole lifetimes are
    /// gameplay, so they run on game time, capped by an unscaled, unpaused age. A landmark refused by its gate (a Mini Nuke a little
    /// inside its 3 s on the unscaled clock, while the game's cooldown ran on game time) is held. It plays on the first Tick its
    /// gate opens, at most <see cref="AudioVoiceLod.HoldSeconds"/> late, and is never played after a pause.
    ///
    /// The loops are looping AudioSources under the director's Loops root, set by this class and never by the catalog's Loop flag
    /// (S-08 R4 data note). Nothing here allocates after the first use of a sound.
    /// </summary>
    public sealed class TreeAudio
    {
        /// <summary>The R4 tree block of <see cref="AudioSoundId"/> (IDS.md 1020–1063); the gate state is indexed from here.</summary>
        public const int FirstId = (int)AudioSoundId.TreeOpen;
        public const int IdCount = (int)AudioSoundId.TreeAuraUp - FirstId + 1;

        private const int LiveSlots = 4;
        private const int RateSlots = 8;
        private const int PendingSlots = 4;
        private const int GateOpen = 0, GateFrame = 1, GateInterval = 2, GateRate = 3, GateLive = 4;

        /// <summary>DD.Audio.* markers (R4 perf rule). A nested holder: the static tables (SoundFor, the ids) never create a marker.</summary>
        private static class Markers
        {
            public static readonly ProfilerMarker Proc = new ProfilerMarker("DD.Audio.Tree.Proc");
            public static readonly ProfilerMarker Screen = new ProfilerMarker("DD.Audio.Tree.Screen");
            public static readonly ProfilerMarker Tick = new ProfilerMarker("DD.Audio.Tree.Tick");
        }

        private struct Zone
        {
            public bool Live;
            public TreeProcKind Kind;
            public AudioSoundId Loop;
            public Vector3 Point;
            public float EndAt;   // game time: the zone is a gameplay object
            public float Age;     // unscaled, unpaused seconds: a stalled clock never keeps it
            public int Voice;     // field voice index, −1 = not heard
        }

        private struct FieldVoice
        {
            public AudioSource Source;
            public int Zone;      // −1 = free (or fading out)
            public AudioSoundId Sound;
            public AudioBus Bus;
            public float Level;   // 0–1 ramp
            public float Gain;    // the placement's
            public bool Fading;
        }

        private struct Pending
        {
            public bool Live;
            public AudioSoundId Sound;
            public Vector3 Point;
            public float ReadyAt;     // unscaled: when its gate opens
            public float ExpiresAt;   // unscaled: ReadyAt + its hold (a pause in between drops it)
        }

        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly WeaponFireAudio.PlayFunc _play;
        private readonly System.Func<Vector3> _listener;

        private readonly int[] _lastFrame = new int[IdCount];
        private readonly float[] _lastAt = new float[IdCount];
        private readonly float[] _rate = new float[IdCount * RateSlots];
        private readonly int[] _rateHead = new int[IdCount];
        private readonly float[] _liveUntil = new float[IdCount * LiveSlots];
        private readonly float[] _hold = new float[IdCount];
        private readonly int[] _played = new int[IdCount];
        private readonly Pending[] _pending = new Pending[PendingSlots];

        private readonly Zone[] _zones = new Zone[TreeGuardrails.AreaZones];
        private readonly FieldVoice[] _field = new FieldVoice[AudioMixPlan.TreeFieldVoices];
        private readonly int[] _nearest = new int[AudioMixPlan.TreeFieldVoices];
        private readonly float[] _nearestSqr = new float[AudioMixPlan.TreeFieldVoices];
        private int _nearestCount;

        private readonly AudioSource _hole;
        private bool _holeLive;
        private bool _holeEnding;
        private float _holeLevel;
        private float _holeGain;
        private AudioBus _holeBus = AudioBus.Target;
        private float _holeEndAt;
        private float _holeAge;
        private float _holeFade;

        private int _aura;
        private bool _subscribed;

        public TreeAudio(AudioClipLibrary library, AudioBusMixer mixer, AudioMixerBridge bridge, Transform root, WeaponFireAudio.PlayFunc play,
            System.Func<Vector3> listener)
        {
            _library = library;
            _mixer = mixer;
            _play = play;
            _listener = listener;

            for (int i = 0; i < IdCount; i++)
            {
                _lastFrame[i] = -1;
                _lastAt[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < _rate.Length; i++)
            {
                _rate[i] = float.NegativeInfinity;
            }

            for (int z = 0; z < _zones.Length; z++)
            {
                _zones[z].Voice = -1;
            }

            AudioPlacement field = AudioMixPlan.Tree(AudioSoundId.TreeFireLoop);
            for (int v = 0; v < _field.Length; v++)
            {
                _field[v].Source = NewLoopSource("TreeField " + v, field, bridge, root);
                _field[v].Zone = -1;
            }

            _hole = NewLoopSource("TreeBlackHole", AudioMixPlan.Tree(AudioSoundId.TreeBlackHoleLoop), bridge, root);
        }

        // ------------------------------------------------------------------ readouts (tests / F1)

        /// <summary>TreeEvents.Proc calls handled, counted before any gate: one per raise proves one subscription.</summary>
        public int ProcEvents { get; private set; }

        /// <summary>NodePurchased / SynergyUnlocked / AuraChanged calls handled (before any gate).</summary>
        public int ScreenEvents { get; private set; }

        public int Requested { get; private set; }
        public int Played { get; private set; }

        /// <summary>Admitted by the gate, refused by the pool (budget).</summary>
        public int Dropped { get; private set; }

        public int RejectedFrame { get; private set; }
        public int RejectedInterval { get; private set; }
        public int RejectedRate { get; private set; }
        public int RejectedLive { get; private set; }
        public int Held { get; private set; }
        public int HeldPlayed { get; private set; }
        public int HeldExpired { get; private set; }
        public int LoopStarts { get; private set; }
        public int ZonesOpened { get; private set; }
        public int ZoneRefreshes { get; private set; }
        public int ZonesReplaced { get; private set; }

        /// <summary>Black hole loops that ended by their own life (no BlackHoleCollapse arrived).</summary>
        public int HoleTimeouts { get; private set; }

        public int RearmCount { get; private set; }
        public AudioSoundId LastSound { get; private set; }
        public Vector3 LastAt { get; private set; }

        public bool Subscribed
        {
            get { return _subscribed; }
        }

        /// <summary>Starts of one tree sound (one-shots; the loops count in <see cref="LoopStarts"/>).</summary>
        public int PlayedOf(AudioSoundId sound)
        {
            int i = (int)sound - FirstId;
            return i >= 0 && i < IdCount ? _played[i] : 0;
        }

        /// <summary>Unscaled time the sound last started, −1 = never.</summary>
        public float LastPlayedAt(AudioSoundId sound)
        {
            int i = (int)sound - FirstId;
            return i >= 0 && i < IdCount && _lastAt[i] > float.NegativeInfinity ? _lastAt[i] : -1f;
        }

        /// <summary>The live-ceiling hold of a sound (its first clip's length); 0 before it first played.</summary>
        public float HoldSecondsOf(AudioSoundId sound)
        {
            int i = (int)sound - FirstId;
            return i >= 0 && i < IdCount ? _hold[i] : 0f;
        }

        public int ZoneCount
        {
            get
            {
                int n = 0;
                for (int z = 0; z < _zones.Length; z++) if (_zones[z].Live) n++;
                return n;
            }
        }

        /// <summary>Field loops assigned to a live zone (logical state; the source plays unless the listener is paused).</summary>
        public int FieldLoopsActive
        {
            get
            {
                int n = 0;
                for (int v = 0; v < _field.Length; v++) if (_field[v].Zone >= 0) n++;
                return n;
            }
        }

        public bool FieldLoopActive(AudioSoundId loop)
        {
            for (int v = 0; v < _field.Length; v++)
            {
                if (_field[v].Zone >= 0 && _field[v].Sound == loop) return true;
            }

            return false;
        }

        /// <summary>The farthest of the zones chosen to be heard (metres from the listener), 0 when none.</summary>
        public float HeardFarthestMeters
        {
            get { return _nearestCount > 0 ? Mathf.Sqrt(_nearestSqr[_nearestCount - 1]) : 0f; }
        }

        /// <summary>The black hole loop is up (open and not ending).</summary>
        public bool BlackHoleActive
        {
            get { return _holeLive && !_holeEnding; }
        }

        /// <summary>The black hole source is still sounding (including its fade).</summary>
        public bool BlackHoleSounding
        {
            get { return _holeLive; }
        }

        public int AuraLevel
        {
            get { return _aura; }
        }

        // ------------------------------------------------------------------ subscription (static bus rearm lesson)

        public void Enable()
        {
            if (_subscribed)
            {
                return;
            }

            TreeEvents.Proc += OnProc;
            TreeEvents.NodePurchased += OnNodePurchased;
            TreeEvents.SynergyUnlocked += OnSynergyUnlocked;
            TreeEvents.AuraChanged += OnAuraChanged;
            _subscribed = true;
        }

        /// <summary>
        /// The director calls this on every scene change: <c>TreeEvents.ResetForTests</c> (S-04 / S-07 test SetUps) drops every
        /// listener under a live director. Remove-then-add never doubles a handler.
        /// </summary>
        public void Rearm()
        {
            if (!_subscribed)
            {
                return;
            }

            TreeEvents.Proc -= OnProc;
            TreeEvents.Proc += OnProc;
            TreeEvents.NodePurchased -= OnNodePurchased;
            TreeEvents.NodePurchased += OnNodePurchased;
            TreeEvents.SynergyUnlocked -= OnSynergyUnlocked;
            TreeEvents.SynergyUnlocked += OnSynergyUnlocked;
            TreeEvents.AuraChanged -= OnAuraChanged;
            TreeEvents.AuraChanged += OnAuraChanged;
            RearmCount++;
        }

        public void Disable()
        {
            if (!_subscribed)
            {
                return;
            }

            TreeEvents.Proc -= OnProc;
            TreeEvents.NodePurchased -= OnNodePurchased;
            TreeEvents.SynergyUnlocked -= OnSynergyUnlocked;
            TreeEvents.AuraChanged -= OnAuraChanged;
            _subscribed = false;
            StopAll();
        }

        // ------------------------------------------------------------------ events

        /// <summary>TreeProcKind → its sound (loops included); None = silent here. The EditMode test keeps the map rows equal.</summary>
        public static AudioSoundId SoundFor(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.Crit:
                case TreeProcKind.CritRing: return AudioSoundId.TreeCrit;
                case TreeProcKind.MiniNuke: return AudioSoundId.TreeMiniNuke;
                case TreeProcKind.BlackHoleOpen: return AudioSoundId.TreeBlackHoleLoop;
                case TreeProcKind.BlackHoleCollapse: return AudioSoundId.TreeBlackHoleCollapse;
                case TreeProcKind.Ignite: return AudioSoundId.TreeFireIgnite;
                case TreeProcKind.FireGround: return AudioSoundId.TreeFireLoop;
                case TreeProcKind.Freeze: return AudioSoundId.TreeIceFreeze;
                case TreeProcKind.Shatter: return AudioSoundId.TreeIceShatter;
                case TreeProcKind.ShockChain: return AudioSoundId.TreeThunder;
                case TreeProcKind.VenomCloud: return AudioSoundId.TreePoisonLoop;
                case TreeProcKind.ElementNova: return AudioSoundId.TreeElementNova;
                case TreeProcKind.DoomsdayBell: return AudioSoundId.TreeDoomsdayBell;
                case TreeProcKind.ShieldBreak: return AudioSoundId.TreeShieldBreak;
                case TreeProcKind.Dodge: return AudioSoundId.TreeDodge;
                case TreeProcKind.Leech: return AudioSoundId.TreeLifesteal;
                case TreeProcKind.UndyingOath: return AudioSoundId.TreeUndying;
                case TreeProcKind.EndlessWindowOpen: return AudioSoundId.TreeEndlessOn;
                case TreeProcKind.ShieldUp: return AudioSoundId.TreeShieldUp;          // B1 (main): up or refilled to full, at the player
                case TreeProcKind.ShieldHit: return AudioSoundId.TreeShieldHit;        // absorbed without breaking
                case TreeProcKind.Ricochet: return AudioSoundId.TreeRicochet;          // one bounce, at the bounce point
                case TreeProcKind.BarrageRocket: return AudioSoundId.TreeMiniRocket;   // a Barrage (rl_1 + Full-Auto) rocket launched
                // ExplosiveRound / ChainBlast / ClusterSplit: the blast's own Explosion / BombletPop cue speaks (IDS.md);
                // EndlessWindowClose / OverclockPeak / SynergyFirstTrigger: silent by design (HUD / VFX beats).
                default: return AudioSoundId.None;
            }
        }

        private void OnProc(TreeProcInfo info)
        {
            using (Markers.Proc.Auto())
            {
                ProcEvents++;
                switch (info.Kind)
                {
                    case TreeProcKind.FireGround:
                    case TreeProcKind.VenomCloud:
                        OpenZone(info);
                        return;
                    case TreeProcKind.BlackHoleOpen:
                        OpenHole(info);
                        return;
                    case TreeProcKind.BlackHoleCollapse:
                        PlayGated(AudioSoundId.TreeBlackHoleCollapse, info.Point, 60);
                        EndHole(AudioMixPlan.TreeHoleCollapseFade);
                        return;
                }

                AudioSoundId sound = SoundFor(info.Kind);
                if (sound != AudioSoundId.None)
                {
                    PlayGated(sound, info.Point, 50);
                }
            }
        }

        private void OnNodePurchased(string nodeId, TreeBranch branch, int tier)
        {
            using (Markers.Screen.Auto())
            {
                ScreenEvents++;
                PlayGated(AudioSoundId.TreeStamp, Vector3.zero, 60);
            }
        }

        private void OnSynergyUnlocked(TreeSynergy synergy)
        {
            using (Markers.Screen.Auto())
            {
                ScreenEvents++;
                PlayGated(AudioSoundId.TreeSynergy, Vector3.zero, 70);
            }
        }

        /// <summary>The aura's level (0–3, AD B11): a sound only when it goes up; a run start resets it (<see cref="ResetRun"/>).</summary>
        private void OnAuraChanged(int level)
        {
            using (Markers.Screen.Auto())
            {
                ScreenEvents++;
                bool up = level > _aura;
                _aura = level;
                if (up)
                {
                    PlayGated(AudioSoundId.TreeAuraUp, Vector3.zero, 70);
                }
            }
        }

        // ------------------------------------------------------------------ the LOD gate

        private bool PlayGated(AudioSoundId sound, Vector3 at, int priority)
        {
            Requested++;
            int i = (int)sound - FirstId;
            if (i < 0 || i >= IdCount)
            {
                return false;
            }

            AudioVoiceLod lod = AudioMixPlan.TreeLod(sound);
            float now = Time.unscaledTime;
            int frame = Time.frameCount;
            int gate = Gate(i, lod, now, frame, out float readyAt);
            if (gate != GateOpen)
            {
                CountRejection(gate);
                // The same frame merges (one sound per frame is the point); a landmark the clock refused a little early waits.
                if (gate != GateFrame && lod.HoldSeconds > 0f && readyAt - now <= lod.HoldSeconds)
                {
                    Hold(sound, at, readyAt, lod.HoldSeconds);
                }

                return false;
            }

            return Start(i, sound, lod, at, priority, now, frame);
        }

        private int Gate(int i, in AudioVoiceLod lod, float now, int frame, out float readyAt)
        {
            readyAt = now;
            if (_lastFrame[i] == frame)
            {
                return GateFrame;
            }

            float intervalAt = _lastAt[i] + lod.MinInterval;
            if (now < intervalAt)
            {
                readyAt = intervalAt;
                return GateInterval;
            }

            int window = RateWindow(lod);
            if (window > 0)
            {
                float oldest = _rate[i * RateSlots + _rateHead[i] % window];   // the window-th most recent start
                if (now - oldest < 1f)
                {
                    readyAt = oldest + 1f;
                    return GateRate;
                }
            }

            int max = Mathf.Clamp(lod.MaxLive, 1, LiveSlots);
            int live = 0;
            float freeAt = float.MaxValue;
            for (int k = 0; k < LiveSlots; k++)
            {
                float until = _liveUntil[i * LiveSlots + k];
                if (until > now)
                {
                    live++;
                    if (until < freeAt) freeAt = until;
                }
            }

            if (live >= max)
            {
                readyAt = freeAt;
                return GateLive;
            }

            return GateOpen;
        }

        private bool Start(int i, AudioSoundId sound, in AudioVoiceLod lod, Vector3 at, int priority, float now, int frame)
        {
            AudioPlacement p = AudioMixPlan.Tree(sound);
            if (at == Vector3.zero)
            {
                p.Spatial = 0f;   // CombatAudioCue's convention: zero = non-positional
            }

            if (!_play(p, at, 1f, priority))
            {
                Dropped++;
                return false;
            }

            Commit(i, lod, now, frame, HoldOf(i, sound) + Mathf.Max(0f, p.Delay));
            Played++;
            LastSound = sound;
            LastAt = at;
            return true;
        }

        private void Commit(int i, in AudioVoiceLod lod, float now, int frame, float hold)
        {
            _lastFrame[i] = frame;
            _lastAt[i] = now;
            int window = RateWindow(lod);
            if (window > 0)
            {
                _rate[i * RateSlots + _rateHead[i] % window] = now;
                _rateHead[i]++;
            }

            int slot = 0;
            float earliest = float.MaxValue;
            for (int k = 0; k < LiveSlots; k++)
            {
                float until = _liveUntil[i * LiveSlots + k];
                if (until < earliest)
                {
                    earliest = until;
                    slot = k;
                }
            }

            _liveUntil[i * LiveSlots + slot] = now + hold;
            _played[i]++;
        }

        private static int RateWindow(in AudioVoiceLod lod)
        {
            return Mathf.Clamp(lod.PerSecond, 0, RateSlots);
        }

        /// <summary>How long one copy counts against the live ceiling: the first clip's length, read once per sound.</summary>
        private float HoldOf(int i, AudioSoundId sound)
        {
            if (_hold[i] <= 0f)
            {
                AudioClip clip = _library.First(sound);
                _hold[i] = clip != null ? Mathf.Max(0.05f, clip.length) : 0.5f;
            }

            return _hold[i];
        }

        private void CountRejection(int gate)
        {
            switch (gate)
            {
                case GateFrame: RejectedFrame++; break;
                case GateInterval: RejectedInterval++; break;
                case GateRate: RejectedRate++; break;
                default: RejectedLive++; break;
            }
        }

        private void Hold(AudioSoundId sound, Vector3 at, float readyAt, float hold)
        {
            int free = -1;
            for (int k = 0; k < PendingSlots; k++)
            {
                if (_pending[k].Live && _pending[k].Sound == sound)
                {
                    _pending[k].Point = at;   // one held copy per sound, at the latest place
                    return;
                }

                if (!_pending[k].Live && free < 0) free = k;
            }

            if (free < 0)
            {
                return;
            }

            _pending[free].Live = true;
            _pending[free].Sound = sound;
            _pending[free].Point = at;
            _pending[free].ReadyAt = readyAt;
            _pending[free].ExpiresAt = readyAt + hold;
            Held++;
        }

        private void TickPending(float now)
        {
            for (int k = 0; k < PendingSlots; k++)
            {
                if (!_pending[k].Live || now < _pending[k].ReadyAt)
                {
                    continue;
                }

                if (now > _pending[k].ExpiresAt)
                {
                    _pending[k].Live = false;   // a pause (no Tick) outlived the hold: a landmark is never played late
                    HeldExpired++;
                    continue;
                }

                AudioSoundId sound = _pending[k].Sound;
                int i = (int)sound - FirstId;
                AudioVoiceLod lod = AudioMixPlan.TreeLod(sound);
                int frame = Time.frameCount;
                if (Gate(i, lod, now, frame, out float readyAt) != GateOpen)
                {
                    if (readyAt <= _pending[k].ExpiresAt && readyAt > now)
                    {
                        _pending[k].ReadyAt = readyAt;
                    }
                    else if (readyAt > _pending[k].ExpiresAt)
                    {
                        _pending[k].Live = false;
                        HeldExpired++;
                    }

                    continue;
                }

                _pending[k].Live = false;
                if (Start(i, sound, lod, _pending[k].Point, 70, now, frame))
                {
                    HeldPlayed++;
                }
            }
        }

        // ------------------------------------------------------------------ zones (fire ground / venom cloud)

        private void OpenZone(in TreeProcInfo info)
        {
            float now = Time.time;
            float life = ZoneSeconds(info);
            float merge = Mathf.Max(AudioMixPlan.TreeZoneMergeMeters, info.Radius * 0.5f);
            int free = -1;
            int soonest = -1;
            for (int z = 0; z < _zones.Length; z++)
            {
                if (!_zones[z].Live)
                {
                    if (free < 0) free = z;
                    continue;
                }

                if (_zones[z].Kind == info.Kind && (_zones[z].Point - info.Point).sqrMagnitude <= merge * merge)
                {
                    _zones[z].EndAt = Mathf.Max(_zones[z].EndAt, now + life);   // the same zone republished: it lives on
                    _zones[z].Age = 0f;
                    ZoneRefreshes++;
                    return;
                }

                if (soonest < 0 || _zones[z].EndAt < _zones[soonest].EndAt) soonest = z;
            }

            int slot = free;
            if (slot < 0)
            {
                slot = soonest;   // B0: AreaZones ≤ 16 - the zone ending first gives way
                if (_zones[slot].Voice >= 0) Release(_zones[slot].Voice);
                ZonesReplaced++;
            }

            _zones[slot].Live = true;
            _zones[slot].Kind = info.Kind;
            _zones[slot].Loop = SoundFor(info.Kind);
            _zones[slot].Point = info.Point;
            _zones[slot].EndAt = now + life;
            _zones[slot].Age = 0f;
            _zones[slot].Voice = -1;
            ZonesOpened++;
        }

        private static float ZoneSeconds(in TreeProcInfo info)
        {
            ref readonly TreeStats stats = ref TreeRuntime.Current;
            bool fire = info.Kind == TreeProcKind.FireGround;
            float seconds = info.Seconds;   // B1: the zone's own length when the raiser knows it
            if (seconds <= 0f)
            {
                seconds = fire ? stats.FireGroundSeconds : stats.VenomCloudSeconds;
            }

            if (seconds <= 0f)
            {
                seconds = fire ? AudioMixPlan.TreeFireGroundSeconds : AudioMixPlan.TreeVenomCloudSeconds;
            }

            return Mathf.Min(seconds, AudioMixPlan.TreeZoneMaxSeconds);
        }

        private void ExpireZones(float game, float dt)
        {
            for (int z = 0; z < _zones.Length; z++)
            {
                if (!_zones[z].Live)
                {
                    continue;
                }

                _zones[z].Age += dt;
                if (game >= _zones[z].EndAt || _zones[z].Age >= AudioMixPlan.TreeZoneMaxSeconds)
                {
                    _zones[z].Live = false;
                    if (_zones[z].Voice >= 0) Release(_zones[z].Voice);
                }
            }
        }

        /// <summary>The live zones nearest the listener, at most <see cref="AudioMixPlan.TreeFieldVoices"/>, sorted (insertion into a fixed top-N).</summary>
        private void SelectNearest(Vector3 listener)
        {
            _nearestCount = 0;
            int n = _nearest.Length;
            for (int z = 0; z < _zones.Length; z++)
            {
                if (!_zones[z].Live)
                {
                    continue;
                }

                float d = (_zones[z].Point - listener).sqrMagnitude;
                int pos;
                if (_nearestCount < n) pos = _nearestCount++;
                else if (d < _nearestSqr[n - 1]) pos = n - 1;
                else continue;

                while (pos > 0 && _nearestSqr[pos - 1] > d)
                {
                    _nearest[pos] = _nearest[pos - 1];
                    _nearestSqr[pos] = _nearestSqr[pos - 1];
                    pos--;
                }

                _nearest[pos] = z;
                _nearestSqr[pos] = d;
            }
        }

        private bool IsNearest(int zone)
        {
            for (int k = 0; k < _nearestCount; k++)
            {
                if (_nearest[k] == zone) return true;
            }

            return false;
        }

        private void TickField(float dt)
        {
            // 1. A voice whose zone ended or fell out of the nearest set fades out.
            for (int v = 0; v < _field.Length; v++)
            {
                int z = _field[v].Zone;
                if (z >= 0 && (!_zones[z].Live || !IsNearest(z))) Release(v);
            }

            // 2. A nearest zone without a voice takes a free one (a voice still fading frees up on a later frame).
            for (int k = 0; k < _nearestCount; k++)
            {
                int z = _nearest[k];
                if (_zones[z].Voice >= 0)
                {
                    continue;
                }

                int v = FreeVoice();
                if (v < 0)
                {
                    break;
                }

                Assign(v, z);
            }

            // 3. Ramps on the bus gain (the sidechain and the pause mute reach the loops too).
            for (int v = 0; v < _field.Length; v++)
            {
                if (_field[v].Zone < 0 && !_field[v].Fading)
                {
                    continue;
                }

                float target = _field[v].Fading ? 0f : 1f;
                float seconds = _field[v].Fading ? AudioMixPlan.TreeLoopFadeOut : AudioMixPlan.TreeLoopFadeIn;
                _field[v].Level = Mathf.MoveTowards(_field[v].Level, target, dt / Mathf.Max(0.01f, seconds));
                if (_field[v].Fading && _field[v].Level <= 0.001f)
                {
                    _field[v].Source.Stop();
                    _field[v].Fading = false;
                    _field[v].Sound = AudioSoundId.None;
                    continue;
                }

                _field[v].Source.volume = _field[v].Level * _field[v].Gain * _mixer.Gain(_field[v].Bus);
            }
        }

        private int FreeVoice()
        {
            for (int v = 0; v < _field.Length; v++)
            {
                if (_field[v].Zone < 0 && !_field[v].Fading) return v;
            }

            return -1;
        }

        private void Assign(int v, int z)
        {
            AudioSoundId loop = _zones[z].Loop;
            AudioClip clip = _library.First(loop);
            if (clip == null)
            {
                return;
            }

            AudioPlacement p = AudioMixPlan.Tree(loop);
            AudioSource source = _field[v].Source;
            source.clip = clip;
            source.spatialBlend = p.Spatial;
            source.transform.position = _zones[z].Point;
            source.time = Random.Range(0f, clip.length * 0.9f);   // two fires side by side never crackle in phase
            source.volume = 0f;
            source.Play();
            _field[v].Zone = z;
            _field[v].Sound = loop;
            _field[v].Bus = p.Bus;
            _field[v].Gain = p.Gain;
            _field[v].Level = 0f;
            _field[v].Fading = false;
            _zones[z].Voice = v;
            LoopStarts++;
            LastSound = loop;
            LastAt = _zones[z].Point;
        }

        private void Release(int v)
        {
            int z = _field[v].Zone;
            if (z >= 0 && _zones[z].Voice == v)
            {
                _zones[z].Voice = -1;
            }

            _field[v].Zone = -1;
            _field[v].Fading = true;
        }

        // ------------------------------------------------------------------ the black hole

        private void OpenHole(in TreeProcInfo info)
        {
            float seconds = info.Seconds > 0f ? info.Seconds : TreeRuntime.Current.HoleSeconds;   // B1: the hole's own length first
            if (seconds <= 0f)
            {
                seconds = AudioMixPlan.TreeBlackHoleSeconds;
            }

            AudioClip clip = _library.First(AudioSoundId.TreeBlackHoleLoop);
            if (clip == null)
            {
                return;
            }

            _holeEndAt = Time.time + Mathf.Min(seconds, AudioMixPlan.TreeZoneMaxSeconds) + AudioMixPlan.TreeHoleGrace;
            _holeAge = 0f;
            _hole.transform.position = info.Point;
            if (!_holeLive)
            {
                AudioPlacement p = AudioMixPlan.Tree(AudioSoundId.TreeBlackHoleLoop);
                _hole.clip = clip;
                _hole.spatialBlend = info.Point == Vector3.zero ? 0f : p.Spatial;
                _hole.time = 0f;
                _hole.volume = 0f;
                _hole.Play();
                _holeGain = p.Gain;
                _holeBus = p.Bus;
                _holeLevel = 0f;
                LoopStarts++;
            }

            _holeLive = true;
            _holeEnding = false;   // B0: one hole at a time - a second open moves and extends it
            LastSound = AudioSoundId.TreeBlackHoleLoop;
            LastAt = info.Point;
        }

        private void EndHole(float fadeSeconds)
        {
            if (!_holeLive || _holeEnding)
            {
                return;
            }

            _holeEnding = true;
            _holeFade = fadeSeconds;
        }

        private void TickHole(float dt, float game)
        {
            if (!_holeLive)
            {
                return;
            }

            _holeAge += dt;
            if (!_holeEnding && (game >= _holeEndAt || _holeAge >= AudioMixPlan.TreeZoneMaxSeconds))
            {
                HoleTimeouts++;   // no BlackHoleCollapse came: the loop ends with its life, without a collapse it did not hear
                EndHole(AudioMixPlan.TreeLoopFadeOut);
            }

            float seconds = _holeEnding ? _holeFade : AudioMixPlan.TreeLoopFadeIn;
            _holeLevel = Mathf.MoveTowards(_holeLevel, _holeEnding ? 0f : 1f, dt / Mathf.Max(0.01f, seconds));
            if (_holeEnding && _holeLevel <= 0.001f)
            {
                _hole.Stop();
                _holeLive = false;
                _holeEnding = false;
                return;
            }

            _hole.volume = _holeLevel * _holeGain * _mixer.Gain(_holeBus);
        }

        // ------------------------------------------------------------------ frame / stop

        /// <summary>Once per unpaused frame from the director (unscaled delta).</summary>
        public void Tick(float dt)
        {
            using (Markers.Tick.Auto())
            {
                TickPending(Time.unscaledTime);
                ExpireZones(Time.time, dt);
                SelectNearest(_listener != null ? _listener() : Vector3.zero);
                TickField(dt);
                TickHole(dt, Time.time);
            }
        }

        /// <summary>Round end, death, leaving the run, the director's OnDisable: every loop and held landmark goes.</summary>
        public void StopAll()
        {
            for (int z = 0; z < _zones.Length; z++)
            {
                _zones[z].Live = false;
                _zones[z].Voice = -1;
            }

            for (int v = 0; v < _field.Length; v++)
            {
                if (_field[v].Source != null) _field[v].Source.Stop();
                _field[v].Zone = -1;
                _field[v].Fading = false;
                _field[v].Level = 0f;
                _field[v].Sound = AudioSoundId.None;
            }

            if (_hole != null) _hole.Stop();
            _holeLive = false;
            _holeEnding = false;
            _holeLevel = 0f;

            for (int k = 0; k < PendingSlots; k++)
            {
                _pending[k].Live = false;
            }

            _nearestCount = 0;
        }

        /// <summary>A new run: the aura starts from nothing again (AuraChanged plays only on a rise).</summary>
        public void ResetRun()
        {
            _aura = 0;
            StopAll();
        }

        /// <summary>Tests: forget every gate (frame, interval, rate, live ceiling, held landmark) so a fixture starts from a quiet voice.</summary>
        public void ResetGatesForTests()
        {
            for (int i = 0; i < IdCount; i++)
            {
                _lastFrame[i] = -1;
                _lastAt[i] = float.NegativeInfinity;
                _rateHead[i] = 0;
            }

            for (int i = 0; i < _rate.Length; i++)
            {
                _rate[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < _liveUntil.Length; i++)
            {
                _liveUntil[i] = 0f;
            }

            for (int k = 0; k < PendingSlots; k++)
            {
                _pending[k].Live = false;
            }
        }

        public string Describe()
        {
            return string.Format(
                "proc {0} · played {1} (dropped {2}) · refused frame {3} / interval {4} / rate {5} / live {6} · held {7} (played {8}, expired {9}) · zones {10} heard {11} · hole {12} · aura {13}",
                ProcEvents, Played, Dropped, RejectedFrame, RejectedInterval, RejectedRate, RejectedLive, Held, HeldPlayed, HeldExpired,
                ZoneCount, FieldLoopsActive, BlackHoleActive ? "open" : "-", _aura);
        }

        private static AudioSource NewLoopSource(string name, in AudioPlacement placement, AudioMixerBridge bridge, Transform root)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;   // the source loops, never the catalog's Loop flag (S-08 R4 data note)
            source.spatialBlend = placement.Spatial;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.priority = 24;
            source.volume = 0f;
            bridge?.Route(source, placement.Bus);
            return source;
        }
    }
}
