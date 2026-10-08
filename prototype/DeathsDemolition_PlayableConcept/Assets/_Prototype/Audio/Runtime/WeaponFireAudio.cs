using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>WP-072: the recorded-tail family a weapon rings in (STANDARDS §5: class × space).</summary>
    public enum WeaponTailClass
    {
        Ar = 0,
        Smg = 1,
        Sg = 2,
        Lmg = 3,
        Sr = 4,
        Rl = 5
    }

    /// <summary>WP-072 / WP-074: the acoustic space a shot rings in (S-05 Enclosure).</summary>
    public enum AcousticSpace
    {
        Open = 0,
        Corridor = 1,
        Hall = 2,

        /// <summary>
        /// WP-097 K1 (C-00 K3, 07.10): a closed interior of 20–90 m² under a ceiling ≤ 4 m. Its own class tail (WeaponTail*Room,
        /// 900 + class·10 + 2) plays only in top-down once produced; until then — and in third-person / first-person — it rings
        /// as a corridor (the weapon's TailCorridor at the corridor's wet). Never maps onto a <see cref="WeaponLayer"/>
        /// (<see cref="WeaponAudioIdentity.TailLayer"/>). No slapback, no echo (open air only).
        /// </summary>
        Room = 3
    }

    /// <summary>
    /// WP-072: where the listener is, acoustically. S-05's WP-074 layout (acoustic-spaces-v6.json)
    /// installs <see cref="Resolver"/>; until then, and outside every listed space, it is Open.
    /// </summary>
    public static class AcousticSpaces
    {
        /// <summary>Position → space. Null = everything is Open.</summary>
        public static System.Func<Vector3, AcousticSpace> Resolver;

        public static AcousticSpace At(Vector3 position)
        {
            System.Func<Vector3, AcousticSpace> resolver = Resolver;
            return resolver != null ? resolver(position) : AcousticSpace.Open;
        }
    }

    /// <summary>
    /// WP-097 K1 (03 §4) · the director's <see cref="AcousticSpaces.Resolver"/>, the one seam unchanged (tests still force it). In
    /// order: an <see cref="AcousticVolume"/> containing the point (every camera mode) → S-05's acoustic-spaces JSON map (V6 only)
    /// → in top-down with <see cref="WeaponReflections.Enabled"/>, the probe's enclosure guess (roof + ≥ 6/8 walls within 10 m,
    /// two refreshes of hysteresis) for a point next to the player — until WP-092's buildings get JSON rows or volumes, so a shot
    /// inside one never rings the open court's slapback / echo → Open. FP / TP never consult the guess (R2 behaviour).
    /// Allocation-free.
    /// </summary>
    public sealed class AcousticSpaceResolver
    {
        private readonly AcousticSpaceMap _map;
        private readonly AcousticProbe _probe;

        public AcousticSpaceResolver(AcousticSpaceMap map, AcousticProbe probe)
        {
            _map = map;
            _probe = probe;
        }

        public AcousticSpace Resolve(Vector3 position)
        {
            if (AcousticVolumes.TryResolve(position, out AcousticSpace volume))
            {
                return volume;
            }

            if (_map != null)
            {
                AcousticSpace mapped = _map.Resolve(position);
                if (mapped != AcousticSpace.Open)
                {
                    return mapped;
                }
            }

            if (_probe != null && WeaponReflections.Enabled && CameraModeService.Current == CameraMode.TopDown
                && _probe.TryGuess(position, Time.unscaledTime, out AcousticSpace guessed))
            {
                return guessed;
            }

            return AcousticSpace.Open;
        }
    }

    /// <summary>
    /// WP-085 B3: the AP-027 v4 layers a weapon may carry beside its dry Fire (CX-AP027 spec §3–§5). Each resolves by name —
    /// AudioSoundId "Weapon" + Ar1 / Smg1 / … + layer (WeaponAr1Mechanical, WeaponSg1TailOpen, WeaponAr1DistanceMid) — and is
    /// silent until that id exists and is produced, so the v016 set plays exactly as before.
    /// </summary>
    public enum WeaponLayer
    {
        Mechanical = 0,
        TailOpen = 1,
        TailCorridor = 2,
        TailHall = 3,
        DistanceMid = 4,
        DistanceFar = 5
    }

    /// <summary>WP-085 B3 (spec §3.2): source-to-listener band. Near 0–12 m, Mid 12–35 m, Far beyond.</summary>
    public enum WeaponDistanceBand
    {
        Near = 0,
        Mid = 1,
        Far = 2
    }

    /// <summary>One weapon's fire voice (WP-069 audioId → AP-011 identities).</summary>
    public readonly struct WeaponVoiceSet
    {
        public readonly string AudioId;
        public readonly AudioSoundId Fire;
        public readonly WeaponTailClass TailClass;

        /// <summary>
        /// The weapon's cyclic rate (WD fire.rpm; burst: the in-burst rate) when it fires strings — Auto or Burst — else 0.
        /// A string rings its space tail once, when the trigger lets go; a single-shot weapon rings it on every shot.
        /// </summary>
        public readonly float StringRpm;

        /// <summary>
        /// Per-weapon trim on the fire voice (dB). WP-072 B7 (user 01.10, option B): SFX-Player +3 / SFX-World −1 moved the shot
        /// 4 dB against everything on the world bus; the launcher's own explosion (Target, gain 1.0 — no headroom under the voice clamp)
        /// keeps its STANDARDS §2 +2 dB over the launcher shot only if RL1's fire comes down 4 dB.
        /// WP-085 B1: an automatic now plays the full shot on every round; its trim brings a held string at its own RPM back to the
        /// level of the 8 shots/s kit the user chose the mix by (wp-072/tools/fire_thin.py, 4-voice cap: AR1 −0.9, SMG1 −2.6,
        /// SMG2 −0.4, LMG1 −1.6; LMG2 and the 3-round AR2 burst sit at or under it).
        /// </summary>
        public readonly float FireTrimDb;

        public WeaponVoiceSet(string audioId, AudioSoundId fire, WeaponTailClass tail, float stringRpm = 0f, float fireTrimDb = 0f)
        {
            AudioId = audioId;
            Fire = fire;
            TailClass = tail;
            StringRpm = stringRpm;
            FireTrimDb = fireTrimDb;
        }

        /// <summary>Fires strings (Auto / Burst): tail on release, one more live copy of the shot.</summary>
        public bool FiresStrings => StringRpm > 0f;
    }

    /// <summary>
    /// WP-072: the weapon identity table. audioIds are S-02's (WP-069 A1/A4); the string RPM mirrors the weapon's WD fire block
    /// (EditMode test keeps them in step) and only sets how long a pause reads as a trigger release.
    /// WP-085 B1: the v3 hybrid (per-shot transient over an RPM-locked body loop) is gone — every round is the full shot.
    /// </summary>
    public static class WeaponAudioIdentity
    {
        public static readonly WeaponVoiceSet[] All =
        {
            new WeaponVoiceSet("AR1", AudioSoundId.WeaponAr1Fire, WeaponTailClass.Ar, 600f, -0.9f),
            new WeaponVoiceSet("AR2", AudioSoundId.WeaponAr2Fire, WeaponTailClass.Ar, 1200f),   // 3-round burst, 0.05 s apart
            new WeaponVoiceSet("SMG1", AudioSoundId.WeaponSmg1Fire, WeaponTailClass.Smg, 900f, -2.6f),
            new WeaponVoiceSet("SMG2", AudioSoundId.WeaponSmg2Fire, WeaponTailClass.Smg, 540f, -0.4f),
            new WeaponVoiceSet("SG1", AudioSoundId.WeaponSg1Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("SG2", AudioSoundId.WeaponSg2Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("LMG1", AudioSoundId.WeaponLmg1Fire, WeaponTailClass.Lmg, 720f, -1.6f),
            new WeaponVoiceSet("LMG2", AudioSoundId.WeaponLmg2Fire, WeaponTailClass.Lmg, 420f),
            new WeaponVoiceSet("SR1", AudioSoundId.WeaponSr1Fire, WeaponTailClass.Sr),
            new WeaponVoiceSet("RL1", AudioSoundId.WeaponRl1Fire, WeaponTailClass.Rl, fireTrimDb: -4f)   // WP-072 B7: explosion keeps +2 dB over the shot
        };

        public const float MidMeters = 12f;
        public const float FarMeters = 35f;

        /// <summary>Spec §3.1: Mechanical against Body in top-down (TP / FP add the perspective's MechanicalDb: −7 / −3).</summary>
        public const float MechanicalRefDb = -10f;

        /// <summary>Spec §3.2: in the Mid band the mechanism sits 12 dB further down; in Far it is off.</summary>
        public const float MechanicalMidDb = -12f;

        /// <summary>WP-085 B5: the most a layer is lifted or cut to meet its loudness reference (a quiet stem is not dragged up whole).</summary>
        public const float LoudnessCompMaxDb = 12f;

        /// <summary>
        /// WP-085 B5: the gain that puts <paramref name="layerLufs"/> at <paramref name="targetRelLu"/> against the fire voice's
        /// loudness (catalog, house method), capped at ±<see cref="LoudnessCompMaxDb"/>; <paramref name="fallbackDb"/> when either is
        /// unknown. Distance voices: target 0 (spec §3.2 equal energy at the band edge — the band changes the timbre, the rolloff the
        /// level). Mechanical: target −10 / −7 / −3 (spec §3.1), the delivered stems sit at very different levels (−28 … −55 LUFS).
        /// </summary>
        public static float LoudnessCompDb(float fireLufs, float layerLufs, float targetRelLu, float fallbackDb)
        {
            if (float.IsNaN(fireLufs) || float.IsNaN(layerLufs)) return fallbackDb;
            return Mathf.Clamp(fireLufs + targetRelLu - layerLufs, -LoudnessCompMaxDb, LoudnessCompMaxDb);
        }

        private static readonly int LayerCount = System.Enum.GetValues(typeof(WeaponLayer)).Length;
        private static readonly AudioSoundId[] s_layers = BuildLayers();
        private static readonly AudioSoundId[] s_layerOverrides = new AudioSoundId[All.Length * LayerCount];

        /// <summary>
        /// WP-085 G4 (B-03 GC review): builds the layer table now instead of in the first shot's frame. The table is this type's
        /// initializer — 60 <see cref="LayerName"/> concatenations and Enum.TryParse calls (the first also fills Enum's AudioSoundId
        /// name cache) — and the runtime runs it before the first read of any static field here, so reading the table is the
        /// warm-up. It depends only on <see cref="All"/> and the enum: built earlier, it is the same table — only the timing moves.
        /// The lookup is called once too: Mono's JIT (the editor, and this project's Player backend) compiles it here, not in the
        /// first shot; under IL2CPP the call is only a read.
        /// Idempotent and allocation-free after the first call. Returns the layer ids AP-027 has delivered (the non-None entries).
        /// <see cref="PrototypeAudioDirector"/> calls it once, in Awake.
        /// </summary>
        public static int Warm()
        {
            int delivered = 0;
            for (int k = 0; k < s_layers.Length; k++)
            {
                if (s_layers[k] != AudioSoundId.None) delivered++;
            }

            Layer(All[0].AudioId, WeaponLayer.Mechanical);
            return delivered;
        }

        /// <summary>The AudioSoundId name a layer resolves by: "Weapon" + "Ar1" + "TailOpen".</summary>
        public static string LayerName(string audioId, WeaponLayer layer)
        {
            return "Weapon" + char.ToUpperInvariant(audioId[0]) + audioId.Substring(1).ToLowerInvariant() + layer;
        }

        /// <summary>The weapon's layer id, None while AP-027 has not delivered it (or a test override).</summary>
        public static AudioSoundId Layer(string audioId, WeaponLayer layer)
        {
            int i = IndexOf(audioId);
            if (i < 0) return AudioSoundId.None;
            int k = i * LayerCount + (int)layer;
            return s_layerOverrides[k] != AudioSoundId.None ? s_layerOverrides[k] : s_layers[k];
        }

        /// <summary>Tests only: point a weapon's layer at a produced id (None clears it).</summary>
        public static void SetLayerForTests(string audioId, WeaponLayer layer, AudioSoundId id)
        {
            int i = IndexOf(audioId);
            if (i >= 0) s_layerOverrides[i * LayerCount + (int)layer] = id;
        }

        public static void ClearLayersForTests() => System.Array.Clear(s_layerOverrides, 0, s_layerOverrides.Length);

        public static WeaponDistanceBand Band(float meters)
        {
            return meters >= FarMeters ? WeaponDistanceBand.Far : meters >= MidMeters ? WeaponDistanceBand.Mid : WeaponDistanceBand.Near;
        }

        /// <summary>
        /// Spec §5: the first reflection's arrival (open court 25–35, corridor 8–14, hall 18–24 ms). AP-027 tail files start at
        /// the reflection — the offset is the runtime's — so only the per-weapon v4 tails are delayed; the v016 class tails keep
        /// the timing they were rendered with.
        /// </summary>
        public static float EarlyOffsetSeconds(AcousticSpace space)
        {
            switch (space)
            {
                case AcousticSpace.Corridor: return 0.011f;
                case AcousticSpace.Hall: return 0.021f;
                case AcousticSpace.Room: return 0.005f;   // WP-097 K1 (C-00 K3): a small room's first wall is near
                default: return 0.030f;
            }
        }

        public static int IndexOf(string audioId)
        {
            if (string.IsNullOrEmpty(audioId)) return -1;
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].AudioId, audioId, System.StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        private static AudioSoundId[] BuildLayers()
        {
            var ids = new AudioSoundId[All.Length * LayerCount];
            for (int i = 0; i < All.Length; i++)
            {
                for (int l = 0; l < LayerCount; l++)
                {
                    ids[i * LayerCount + l] = System.Enum.TryParse(LayerName(All[i].AudioId, (WeaponLayer)l), false, out AudioSoundId id) ? id : AudioSoundId.None;
                }
            }

            return ids;
        }

        public static bool TryGet(string audioId, out WeaponVoiceSet set)
        {
            if (!string.IsNullOrEmpty(audioId))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i].AudioId, audioId, System.StringComparison.Ordinal))
                    {
                        set = All[i];
                        return true;
                    }
                }
            }

            set = default;
            return false;
        }

        /// <summary>WP-069 A4 migration: A → ar_1, B → smg_1, C → sg_1 until WeaponDefinition v3 carries audioId.</summary>
        public static string ForClassic(ClassicWeaponId id)
        {
            switch (id)
            {
                case ClassicWeaponId.AutoSmg: return "SMG1";
                case ClassicWeaponId.CombatShotgun: return "SG1";
                default: return "AR1";
            }
        }

        /// <summary>
        /// The v016 class tail of a space (420 + class·3 + space for Open / Corridor / Hall). WP-097 K1: Room has no v016 row — its
        /// class tail is the 900-block <see cref="Room"/> id (never the arithmetic, which would land on the next class's Open).
        /// </summary>
        public static AudioSoundId Tail(WeaponTailClass tailClass, AcousticSpace space)
        {
            if (space == AcousticSpace.Room)
            {
                return Room(tailClass);
            }

            return (AudioSoundId)((int)AudioSoundId.WeaponTailArOpen + (int)tailClass * 3 + (int)space);
        }

        /// <summary>
        /// WP-097 K1 (C-00 K3): the per-weapon AP-027 tail layer a space resolves to — an explicit map, not WeaponLayer.TailOpen +
        /// (int)space (that would send Room onto DistanceMid). Room has no per-weapon layer: it uses TailCorridor (the fallback).
        /// </summary>
        public static WeaponLayer TailLayer(AcousticSpace space)
        {
            switch (space)
            {
                case AcousticSpace.Corridor: return WeaponLayer.TailCorridor;
                case AcousticSpace.Hall: return WeaponLayer.TailHall;
                case AcousticSpace.Room: return WeaponLayer.TailCorridor;
                default: return WeaponLayer.TailOpen;
            }
        }

        /// <summary>WP-097 K1 (C-00 K3): the class's small-room tail, 902 + class·10 (WeaponTailArRoom … WeaponTailRlRoom). Silent until produced.</summary>
        public static AudioSoundId Room(WeaponTailClass tailClass)
        {
            return (AudioSoundId)((int)AudioSoundId.WeaponTailArRoom + (int)tailClass * 10);
        }

        /// <summary>
        /// WP-097 K1 (03 §2): the class's open-air far echo, 900 + class·10 (WeaponTailArEcho … WeaponTailRlEcho). Silent until a
        /// catalog row produces it (the player checks <see cref="AudioClipLibrary.IsProduced"/>); WeaponLayer gains no value.
        /// </summary>
        public static AudioSoundId Echo(WeaponTailClass tailClass)
        {
            return (AudioSoundId)((int)AudioSoundId.WeaponTailArEcho + (int)tailClass * 10);
        }

        /// <summary>WP-097 K1 (03 §2): the class's long last-shot tail of a string release, 901 + class·10. Silent until produced.</summary>
        public static AudioSoundId ReleaseTail(WeaponTailClass tailClass)
        {
            return (AudioSoundId)((int)AudioSoundId.WeaponTailArRelease + (int)tailClass * 10);
        }
    }

    /// <summary>WP-072 STANDARDS §4: the player-weapon mix per camera mode (same files, different placement).</summary>
    public readonly struct WeaponPerspectiveMix
    {
        public readonly float Spatial;
        public readonly float NearFieldDb;
        /// <summary>Mechanical layer lift by perspective (STANDARDS §4: 0 / +3 / +7 over top-down's −10 dB Mechanical/Body, WP-085 B3).</summary>
        public readonly float MechanicalDb;
        public readonly float TailDb;

        public WeaponPerspectiveMix(float spatial, float nearFieldDb, float mechanicalDb, float tailDb)
        {
            Spatial = spatial;
            NearFieldDb = nearFieldDb;
            MechanicalDb = mechanicalDb;
            TailDb = tailDb;
        }

        /// <summary>WP-097 K1 decision 5b: the top-down player-weapon spatial blend (R2: 0.35).</summary>
        public const float TopDownSpatial = 0.20f;

        /// <summary>Relative to top-down, the mode the v016 pre-mixes were balanced in.</summary>
        public static WeaponPerspectiveMix For(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.ThirdPerson: return new WeaponPerspectiveMix(0.25f, 1f, 3f, 1f);
                case CameraMode.FirstPerson: return new WeaponPerspectiveMix(0f, 2f, 7f, 2f);
                // R2 S3 (C-00 06.10 decision): top-down weapon +0.8 dB — the weapon sat only +0.40 LU over the rest of the mix
                // (gate >= +1; FP +2.02) with true-peak / momentary headroom of 1.23 / 1.38; +0.8 leaves both gates headroom.
                // The tail rises with it (TailDb, as in third-person 1/1 and FP 2/2), so the space's wet/dry stays as S4 tuned it.
                // WP-097 K1 decision 5b (SE 01 M6): top-down Spatial 0.35 → 0.20 — the player's own weapon centred and full (its
                // mechanism and tail ride the same placement); NearFieldDb is not raised (0.85 voice clamp + true peak).
                default: return new WeaponPerspectiveMix(TopDownSpatial, 0.8f, 0f, 0.8f);
            }
        }
    }

    /// <summary>
    /// WP-072 B1 / WP-085 B1: the player's fire voice — the full dry shot on every round (shuffled variations, pitch jitter,
    /// same-sound copies capped and recycled oldest-first by the pool). Single-shot weapons ring the space tail on every shot;
    /// string weapons (Auto / Burst) ring it once, when the trigger lets go (STANDARDS §3).
    /// Owned and ticked by <see cref="PrototypeAudioDirector"/>; takes a cue only when the weapon's clips are produced,
    /// so a missing catalog falls back to the legacy RifleFire/SmgFire path.
    ///
    /// WP-097 K1 — TOP-DOWN ONLY (third-person / first-person play exactly the R2 path above):
    ///   * reflections (<see cref="WeaponReflections.Enabled"/>): in the open, slapback copies of the dry clip off the walls the
    ///     <see cref="AcousticProbe"/> sees (3 per single shot, 2 per string shot at most every 0.12 s, Reflect pool); the class's
    ///     far echo with a single shot / a string release and the class's release tail with a string release (both silent until
    ///     K2 produces them); a held string rings its space tail as a bed (<see cref="WeaponTailBed"/>, −6 dB, ≤ 2 voices) and the
    ///     release ramps that bed to the full wet level instead of starting a second tail;
    ///   * mix (always in top-down): open-court wet −6 dB (<see cref="WetDb(AcousticSpace, CameraMode)"/>), spatial 0.20
    ///     (<see cref="WeaponPerspectiveMix.TopDownSpatial"/>), and every shot ducks Impact + Target 1 dB, held 150 ms after the last.
    /// </summary>
    public sealed class WeaponFireAudio
    {
        public delegate bool PlayFunc(in AudioPlacement placement, Vector3 position, float gainScale, int priority);

        /// <summary>WP-097 K1: plays an explicit clip on the Reflect pool (slapback copies, open-air echoes).</summary>
        public delegate bool PlayClipFunc(in AudioPlacement placement, AudioClip clip, Vector3 position, float gainScale, int priority);

        /// <summary>No shot for this many periods = the trigger let go.</summary>
        private const float ReleasePeriods = 1.6f;

        /// <summary>Live copies of one automatic's shot: a 0.26–0.36 s shot at 900 RPM overlaps four deep (WP-085 B1).</summary>
        public const int StringVoices = 4;

        /// <summary>Live copies of a weapon's mechanical layer (spec §4 Faz B budget: Mechanical 2).</summary>
        public const int MechanicalVoices = 2;

        /// <summary>WP-097 K1 decision 4 (SE 01 M3): the top-down open-court wet level, dB (R2, third-person and first-person: −9).</summary>
        public const float TopDownOpenWetDb = -6f;

        /// <summary>
        /// WP-097 K1 decision 5 (SE 01 M1, margin lever L-A): in top-down the player's shot pulls the Impact and Target buses down
        /// this much (dB) through <see cref="AudioBusMixer.Duck"/> (its own 30 ms attack / 350 ms release), held this long after the
        /// last shot (s). Third-person / first-person: no duck.
        /// </summary>
        public const float TopDownWorldDuckDb = 1f;
        public const float TopDownWorldDuckHoldSeconds = 0.15f;

        /// <summary>The duck floor, linear (10^(−1/20) ≈ 0.891).</summary>
        public static readonly float TopDownWorldDuckFloor = Mathf.Pow(10f, -TopDownWorldDuckDb / 20f);

        /// <summary>WP-097 K1: the dry shot's ±3 % pitch spread when this class deals it (top-down reflections), the placement's own spread.</summary>
        private const float DryPitchJitter = 0.03f;

        private readonly AudioClipLibrary _library;
        private readonly PlayFunc _play;

        private bool _active;
        private WeaponVoiceSet _set;
        private float _lastShot;
        private int _lastShotFrame = -10;
        private Vector3 _position;

        // WP-097 K1
        private AcousticProbe _probe;
        private WeaponTailBed _bed;
        private PlayClipFunc _playClip;
        private AudioBusMixer _mixer;
        private readonly SlapTap[] _taps = new SlapTap[WeaponReflections.MaxTapsSingle];
        private SynthRandom _pitchRng = new SynthRandom(0x5A1B7E57);
        private float _lastSlapAt = float.NegativeInfinity;
        private bool _bedString;
        private AcousticSpace _bedSpace;
        private AudioSoundId _lastDryVoice;
        private float _lastDryDb;
        private float _lastPitch = 1f;

        public WeaponFireAudio(AudioClipLibrary library, PlayFunc play)
        {
            _library = library;
            _play = play;
        }

        /// <summary>
        /// WP-097 K1: the top-down reflection kit — the director's probe, the string tail bed, the Reflect pool and the bus mixer
        /// (world duck). Any may be null: that part stays off (EditMode tests wire only what they measure).
        /// </summary>
        public void SetReflections(AcousticProbe probe, WeaponTailBed bed, PlayClipFunc playClip, AudioBusMixer mixer)
        {
            _probe = probe;
            _bed = bed;
            _playClip = playClip;
            _mixer = mixer;
        }

        /// <summary>Full fire voices asked for — one per handled shot, never a lighter layer. Test / F1 readout (R2 S2).</summary>
        public int FireVoices { get; private set; }

        /// <summary>Fire voices the voice pool refused (retrigger window / budget). Test / F1 readout.</summary>
        public int FireRefused { get; private set; }

        /// <summary>Trigger releases heard (tail rung once). Test / F1 readout.</summary>
        public int Releases { get; private set; }

        /// <summary>The tail rung last (class × space), None before the first. WP-097 K1: a top-down string's bed counts (set when it starts).</summary>
        public AudioSoundId LastTail { get; private set; }

        /// <summary>The fire voice played last, None before the first.</summary>
        public AudioSoundId LastVoice { get; private set; }

        /// <summary>WP-085 B3: the mechanical layer played last (None until AP-027 delivers one), its band, the last tail's offset.</summary>
        public AudioSoundId LastMechanical { get; private set; }

        /// <summary>WP-085 B5: the loudness compensation the last distance voice got (0 for Fire) and the mechanism's gain over Fire's.</summary>
        public float LastVoiceCompDb { get; private set; }
        public float LastMechanicalDb { get; private set; }
        public WeaponDistanceBand LastBand { get; private set; }
        public float LastTailDelay { get; private set; }

        /// <summary>WP-097 K1: the space the last tail / bed was chosen for.</summary>
        public AcousticSpace LastSpace { get; private set; }

        /// <summary>WP-097 K1: taps of the last slap set (0 = none), the first (nearest) tap's delay (s) and level (dB against the dry shot).</summary>
        public int LastSlapCount { get; private set; }
        public float LastSlapDelay0 { get; private set; }
        public float LastSlapGainDb0 { get; private set; }

        /// <summary>WP-097 K1: the pitch the last dry shot (and its slaps) played at.</summary>
        public float LastPitch => _lastPitch;

        /// <summary>WP-097 K1: slap copies started / refused by the Reflect pool.</summary>
        public int SlapsPlayed { get; private set; }
        public int SlapsRefused { get; private set; }

        /// <summary>WP-097 K1: the open-air echo played last (None until K2 produces one), how many, and its delay (s).</summary>
        public AudioSoundId LastEcho { get; private set; }
        public int EchoesPlayed { get; private set; }
        public float LastEchoDelay { get; private set; }

        /// <summary>WP-097 K1: the string release tail played last (None until K2 produces one).</summary>
        public AudioSoundId LastReleaseTail { get; private set; }

        /// <summary>WP-097 K1: top-down world ducks requested (one per handled shot).</summary>
        public int WorldDucks { get; private set; }

        /// <summary>WP-097 K1: string tail beds started (0 without a bed).</summary>
        public int BedStarts => _bed != null ? _bed.Starts : 0;

        public AcousticProbe Probe => _probe;
        public WeaponTailBed Bed => _bed;

        private System.Func<Vector3> _listener;

        /// <summary>The player-anchored listener (ADR-021-2): the player's own shots are Near; a far source picks its distance layer.</summary>
        public void SetListener(System.Func<Vector3> listener) => _listener = listener;

        /// <summary>
        /// WP-085 B4 listening kit only (variant B): true → no fire cue is taken here, the director plays the pre-v3 voice
        /// (RifleFire / SmgFire / ShotgunFire by intensity, room baked in, no space tail). The kit test sets and clears it.
        /// </summary>
        public static bool LegacyVoicesForKit;

        public bool TryHandle(in CombatAudioCue cue)
        {
            if (LegacyVoicesForKit) return false;
            if (!WeaponAudioIdentity.TryGet(cue.WeaponAudioId, out WeaponVoiceSet set) || !_library.IsProduced(set.Fire))
            {
                return false;
            }

            CameraMode mode = CameraModeService.Current;
            WeaponPerspectiveMix mix = WeaponPerspectiveMix.For(mode);
            bool topDown = mode == CameraMode.TopDown;
            bool reflect = topDown && WeaponReflections.Enabled;
            float clock = Time.unscaledTime;
            if (reflect)
            {
                RefreshProbe(clock);
            }

            if (!set.FiresStrings)
            {
                if (_active) Release();
                bool dry = PlayVoice(set, cue, mix, reflect);
                AcousticSpace space = PlayTail(set, cue.Position, mix, mode);
                if (reflect && space == AcousticSpace.Open)
                {
                    if (dry) PlaySlaps(WeaponReflections.MaxTapsSingle, clock);
                    PlayEcho(set, cue.Position);
                }

                if (topDown) DuckWorld();
                return true;
            }

            float now = Time.time;
            bool continuing = _active && string.Equals(_set.AudioId, set.AudioId, System.StringComparison.Ordinal)
                && now - _lastShot <= (60f / set.StringRpm) * ReleasePeriods + Time.deltaTime;
            if (!continuing)
            {
                if (_active) Release();
                _active = true;
                _set = set;
                _bedString = false;
            }

            bool played = PlayVoice(set, cue, mix, reflect);
            if (reflect)
            {
                // WP-097 K1 decision 2: the held string rings its space as a bed; decision 1: slaps every ≥ 0.12 s, two taps.
                AcousticSpace space = AcousticSpaces.At(cue.Position);
                LastSpace = space;
                if (_bed != null)
                {
                    BedShot(set, cue.Position, mix, mode, space, clock);
                }

                if (played && space == AcousticSpace.Open && clock - _lastSlapAt >= WeaponReflections.SlapMinInterval)
                {
                    PlaySlaps(WeaponReflections.MaxTapsString, clock);
                }
            }

            if (topDown) DuckWorld();
            _lastShot = now;
            _lastShotFrame = Time.frameCount;
            _position = cue.Position;
            return true;
        }

        /// <summary>Per frame, unpaused: a string's trigger release rings its tail.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            // + this frame's delta: a hitch longer than the release window must not end a string the weapon is still firing
            // (the next shot lands on the frame after the hitch; WP-051C flake). R1-B A1: and at least two frames without a
            // shot — the director and the weapon have no execution order, so one slow frame (GC hitch, < 9 fps) must never
            // read as a trigger release mid-string.
            if (_active && Time.frameCount - _lastShotFrame >= 2
                && Time.time - _lastShot > (60f / _set.StringRpm) * ReleasePeriods + Time.deltaTime)
            {
                Release();
            }
        }

        /// <summary>Run end, death, disable: silence without a tail (WP-097 K1: the string bed ramps out over 0.05 s).</summary>
        public void StopAll()
        {
            _active = false;
            _bedString = false;
            _bed?.StopAll();
        }

        private void Release()
        {
            _active = false;
            CameraMode mode = CameraModeService.Current;
            WeaponPerspectiveMix mix = WeaponPerspectiveMix.For(mode);
            float clock = Time.unscaledTime;

            // WP-097 K1 decision 2: a top-down string's bed is still ringing — it ramps to the full wet level (the last shot's long
            // tail); no second tail. Otherwise (third-person / first-person, reflections off, bed run out) the R2 release tail.
            bool ramped = _bedString && _bed != null && _bed.OnRelease(clock);
            _bedString = false;
            AcousticSpace space = ramped ? _bedSpace : PlayTail(_set, _position, mix, mode);
            if (mode == CameraMode.TopDown && WeaponReflections.Enabled && space == AcousticSpace.Open)
            {
                PlayReleaseTail(_set, _position, mix, mode);
                PlayEcho(_set, _position);
            }

            Releases++;
        }

        /// <summary>The dry shot (+ its mechanism). Returns true when the fire voice got a voice.</summary>
        private bool PlayVoice(in WeaponVoiceSet set, in CombatAudioCue cue, in WeaponPerspectiveMix mix, bool ownPitch)
        {
            // Spec §3.2: the band picks the voice at the event (a far source is its distance recording, never a second shot on top).
            WeaponDistanceBand band = cue.Position == Vector3.zero || _listener == null
                ? WeaponDistanceBand.Near
                : WeaponAudioIdentity.Band(Vector3.Distance(cue.Position, _listener()));
            LastBand = band;
            AudioSoundId voice = set.Fire;
            if (band == WeaponDistanceBand.Far) voice = Produced(WeaponAudioIdentity.Layer(set.AudioId, WeaponLayer.DistanceFar), Produced(WeaponAudioIdentity.Layer(set.AudioId, WeaponLayer.DistanceMid), set.Fire));
            else if (band == WeaponDistanceBand.Mid) voice = Produced(WeaponAudioIdentity.Layer(set.AudioId, WeaponLayer.DistanceMid), set.Fire);

            AudioPlacement p = AudioMixPlan.PlayerWeapon(voice, set.FiresStrings ? StringVoices : AudioMixPlan.PlayerWeaponVoices);
            p.Spatial = cue.Position == Vector3.zero ? 0f : mix.Spatial;
            if (ownPitch)
            {
                // WP-097 K1 (03 §1): top-down reflections replay this exact pitch, so the jitter is dealt here (same ±3 % spread the
                // placement would give the pool) and the pool adds none. Elsewhere the pool deals it, as in R2.
                p.Pitch = 1f + _pitchRng.Range(-DryPitchJitter, DryPitchJitter);
                p.PitchJitter = 0f;
                _lastPitch = p.Pitch;
            }
            else
            {
                _lastPitch = 1f;
            }

            FireVoices++;
            float fireLufs = _library.LufsOf(set.Fire);
            float voiceComp = voice == set.Fire ? 0f : WeaponAudioIdentity.LoudnessCompDb(fireLufs, _library.LufsOf(voice), 0f, 0f);
            LastVoiceCompDb = voiceComp;
            float dryDb = mix.NearFieldDb + set.FireTrimDb + voiceComp;
            bool played = _play(p, cue.Position, Db(dryDb), cue.Priority);
            if (played)
            {
                LastVoice = voice;
                _lastDryVoice = voice;
                _lastDryDb = dryDb;
            }
            else
            {
                FireRefused++;
            }

            // Spec §3.1: the short mechanism as its own voice, Mechanical/Body −10 / −7 / −3 dB by perspective; off in Far.
            AudioSoundId mech = WeaponAudioIdentity.Layer(set.AudioId, WeaponLayer.Mechanical);
            if (band == WeaponDistanceBand.Far || mech == AudioSoundId.None || !_library.IsProduced(mech)) return played;
            AudioPlacement m = AudioMixPlan.PlayerWeapon(mech, MechanicalVoices);
            m.Spatial = p.Spatial;   // rides the body's place; FP's 0 is spec §3.1's 2D mechanism
            float rel = WeaponAudioIdentity.MechanicalRefDb + mix.MechanicalDb + (band == WeaponDistanceBand.Mid ? WeaponAudioIdentity.MechanicalMidDb : 0f);
            float db = mix.NearFieldDb + set.FireTrimDb + WeaponAudioIdentity.LoudnessCompDb(fireLufs, _library.LufsOf(mech), rel, rel);
            LastMechanicalDb = db - mix.NearFieldDb - set.FireTrimDb;
            if (_play(m, cue.Position, Db(db), cue.Priority)) LastMechanical = mech;
            return played;
        }

        private AudioSoundId Produced(AudioSoundId id, AudioSoundId fallback) => id != AudioSoundId.None && _library.IsProduced(id) ? id : fallback;

        /// <summary>
        /// The tail a space rings and its wet level. WP-097 K1 (C-00 K3): Room plays its class tail (900 + class·10 + 2) only in
        /// top-down and only once produced — offset 5 ms, wet <see cref="WetDb(AcousticSpace, CameraMode)"/> (−6); otherwise it
        /// rings as a corridor: the weapon's TailCorridor (top-down: at the room's 5 ms offset) at the corridor's wet (−8).
        /// Every other space: <see cref="ResolveSpaceTail"/> at <see cref="WetDb(AcousticSpace, CameraMode)"/>, as in R2.
        /// </summary>
        private AudioSoundId ResolveTail(in WeaponVoiceSet set, AcousticSpace space, CameraMode mode, out float delay, out float wetDb)
        {
            if (space != AcousticSpace.Room)
            {
                wetDb = WetDb(space, mode);
                return ResolveSpaceTail(set, space, out delay);
            }

            AudioSoundId room = WeaponAudioIdentity.Room(set.TailClass);
            if (mode == CameraMode.TopDown && _library.IsProduced(room))
            {
                delay = WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Room);
                wetDb = WetDb(AcousticSpace.Room, mode);
                return room;
            }

            wetDb = WetDb(AcousticSpace.Corridor);
            AudioSoundId corridor = ResolveSpaceTail(set, AcousticSpace.Corridor, out delay);
            if (mode == CameraMode.TopDown && delay > 0f)
            {
                delay = WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Room);   // the weapon's own corridor tail starts at the room's first wall
            }

            return corridor;
        }

        /// <summary>
        /// WP-085 B3: the weapon's own AP-027 tail (its excitation, spec §5) when delivered, offset by the space's first reflection;
        /// otherwise the v016 class tail, as rendered. None when neither is produced. (Open / Corridor / Hall; Room goes through ResolveTail.)
        /// </summary>
        private AudioSoundId ResolveSpaceTail(in WeaponVoiceSet set, AcousticSpace space, out float delay)
        {
            AudioSoundId tail = WeaponAudioIdentity.Layer(set.AudioId, WeaponAudioIdentity.TailLayer(space));
            delay = WeaponAudioIdentity.EarlyOffsetSeconds(space);
            if (tail == AudioSoundId.None || !_library.IsProduced(tail))
            {
                tail = WeaponAudioIdentity.Tail(set.TailClass, space);
                delay = 0f;
            }

            return _library.IsProduced(tail) ? tail : AudioSoundId.None;
        }

        /// <summary>The space tail of a single shot / a release (R2 path). Returns the space it resolved.</summary>
        private AcousticSpace PlayTail(in WeaponVoiceSet set, Vector3 position, in WeaponPerspectiveMix mix, CameraMode mode)
        {
            AcousticSpace space = AcousticSpaces.At(position);
            LastSpace = space;
            AudioSoundId tail = ResolveTail(set, space, mode, out float delay, out float wetDb);
            if (tail == AudioSoundId.None)
            {
                return space;
            }

            AudioPlacement p = AudioMixPlan.PlayerWeaponTail(tail);
            p.Spatial = position == Vector3.zero ? 0f : mix.Spatial;
            p.Delay = delay;
            if (_play(p, position, Db(mix.TailDb + wetDb), 30))
            {
                LastTail = tail;
                LastTailDelay = delay;
            }

            return space;
        }

        /// <summary>WP-097 K1 decision 2: a held string's shot — the space tail as a bed, at most every 0.15 s (<see cref="TailBedPolicy"/>).</summary>
        private void BedShot(in WeaponVoiceSet set, Vector3 position, in WeaponPerspectiveMix mix, CameraMode mode, AcousticSpace space, float clock)
        {
            if (!_bed.Due(clock))
            {
                return;
            }

            AudioSoundId tail = ResolveTail(set, space, mode, out float delay, out float wetDb);
            if (tail == AudioSoundId.None)
            {
                return;
            }

            AudioClip clip = _library.Next(tail, out int _);
            float full = AudioMixPlan.PlayerWeaponTail(tail).Gain * Db(mix.TailDb + wetDb);
            if (_bed.Start(clip, position, delay, position == Vector3.zero ? 0f : mix.Spatial, full, clock))
            {
                _bedString = true;
                _bedSpace = space;
                LastTail = tail;
                LastTailDelay = delay;
            }
        }

        /// <summary>
        /// WP-097 K1 decision 1 (03 §1): the slapback — the dry shot's own clip (<see cref="AudioClipLibrary.LastDealt"/>) at its own
        /// pitch, per chosen tap: delayed 2d/343, <see cref="SlapTap.GainDb"/> under the dry shot, darkened, from the wall's side.
        /// </summary>
        private void PlaySlaps(int maxTaps, float clock)
        {
            LastSlapCount = 0;
            if (_playClip == null || _probe == null || _listener == null || !_probe.HasData || _lastDryVoice == AudioSoundId.None)
            {
                return;
            }

            AudioClip clip = _library.LastDealt(_lastDryVoice);
            if (clip == null)
            {
                return;
            }

            int n = WeaponReflections.SelectTaps(_probe.Distances, _probe.Valid, maxTaps, _taps);
            LastSlapCount = n;
            LastSlapDelay0 = n > 0 ? _taps[0].Delay : 0f;
            LastSlapGainDb0 = n > 0 ? _taps[0].GainDb : 0f;
            if (n == 0)
            {
                return;
            }

            _lastSlapAt = clock;
            Vector3 ear = _listener();
            for (int i = 0; i < n; i++)
            {
                AudioPlacement p = AudioMixPlan.PlayerWeaponSlap();
                p.Pitch = _lastPitch;
                p.Delay = _taps[i].Delay;
                p.LowPassHz = _taps[i].CutoffHz;
                if (_playClip(p, clip, ear + _taps[i].Dir * WeaponReflections.SlapPanMeters, Db(_lastDryDb + _taps[i].GainDb), 50)) SlapsPlayed++;
                else SlapsRefused++;
            }
        }

        /// <summary>WP-097 K1 decision 6 (03 §2): the class's far echo in the open — silent until K2 produces it.</summary>
        private void PlayEcho(in WeaponVoiceSet set, Vector3 position)
        {
            if (_playClip == null || _probe == null)
            {
                return;
            }

            AudioSoundId id = WeaponAudioIdentity.Echo(set.TailClass);
            if (!_library.IsProduced(id))
            {
                return;
            }

            AudioClip clip = _library.Next(id, out int _);
            if (clip == null)
            {
                return;
            }

            float open = _probe.Openness;
            AudioPlacement p = AudioMixPlan.PlayerWeaponEcho(id);
            p.Delay = WeaponReflections.EchoDelay(_probe.FarMedian);
            p.LowPassHz = WeaponReflections.EchoCutoffHz(open);
            if (position == Vector3.zero) p.Spatial = 0f;
            Vector3 at = _listener != null ? _listener() : position;
            if (_playClip(p, clip, at, Db(_lastDryDb + WeaponReflections.EchoDb(open)), 30))
            {
                LastEcho = id;
                LastEchoDelay = p.Delay;
                EchoesPlayed++;
            }
        }

        /// <summary>WP-097 K1 decision 6 (03 §2): a top-down string release in the open adds the class's long release tail (silent until K2).</summary>
        private void PlayReleaseTail(in WeaponVoiceSet set, Vector3 position, in WeaponPerspectiveMix mix, CameraMode mode)
        {
            AudioSoundId id = WeaponAudioIdentity.ReleaseTail(set.TailClass);
            if (!_library.IsProduced(id))
            {
                return;
            }

            AudioPlacement p = AudioMixPlan.PlayerWeaponTail(id);
            p.Spatial = position == Vector3.zero ? 0f : mix.Spatial;
            p.Delay = WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Open);
            if (_play(p, position, Db(mix.TailDb + WetDb(AcousticSpace.Open, mode)), 30))
            {
                LastReleaseTail = id;
            }
        }

        /// <summary>WP-097 K1 decision 5: Impact + Target −1 dB, held 150 ms after this shot (AudioBusMixer's envelope).</summary>
        private void DuckWorld()
        {
            if (_mixer == null)
            {
                return;
            }

            _mixer.Duck(AudioBus.Impact, TopDownWorldDuckFloor, TopDownWorldDuckHoldSeconds);
            _mixer.Duck(AudioBus.Target, TopDownWorldDuckFloor, TopDownWorldDuckHoldSeconds);
            WorldDucks++;
        }

        /// <summary>WP-097 K1: the probe follows the player (listener) — cached, ≤ one cast set per 1 m / 0.5 s.</summary>
        private void RefreshProbe(float clock)
        {
            if (_probe != null && _listener != null)
            {
                _probe.Refresh(_listener(), clock, false);
            }
        }

        /// <summary>
        /// STANDARDS §5 wet level against the dry shot (−14 / −10 / −8 dB), less the 2 LU the tail files
        /// sit under the dry files (tails −20 LUFS-I, shots −18) — the files stay at a Vorbis-safe level.
        /// WP-085 B5 (C-00 05.10 mix decision, user N2-07 "yankılı"): the open court +3 dB over spec (−11 dB wet), corridor
        /// and hall at spec by the AP-027 v003f tails' BS.1770 loudness (stereo channel sum; hall RT60 ~1.7 s, corridor ~1.25 s):
        /// the hall tails play ~1 dB hotter than the 2 LU file offset assumes, so its wet is −7 (R2 S4 v2 judges RT60).
        /// The R2 values — third-person and first-person play exactly these (WP-097 K1: top-down is <see cref="WetDb(AcousticSpace, CameraMode)"/>).
        /// </summary>
        public static float WetDb(AcousticSpace space)
        {
            switch (space)
            {
                case AcousticSpace.Corridor: return -8f;
                case AcousticSpace.Room: return -8f;   // WP-097 K1 (C-00 K3): outside top-down a room rings as a corridor
                case AcousticSpace.Hall: return -7f;   // v003f hall tails, BS.1770 stereo sum: median −8.4 dB vs Fire (spec −8; −6 gave −7.4, up to −6.6)
                default: return -9f;
            }
        }

        /// <summary>
        /// WP-097 K1 decision 4 (SE 01 M3): the wet level the game plays — top-down open court −6 dB (the tail was masked by the
        /// bed 150–250 ms in); C-00 K3: a top-down Room's own tail −6 dB (when the Room file is missing it rings as a corridor at
        /// −8, see ResolveTail). Every other space and every other camera mode: <see cref="WetDb(AcousticSpace)"/> unchanged.
        /// </summary>
        public static float WetDb(AcousticSpace space, CameraMode mode)
        {
            if (mode != CameraMode.TopDown) return WetDb(space);
            if (space == AcousticSpace.Open) return TopDownOpenWetDb;
            if (space == AcousticSpace.Room) return TopDownRoomWetDb;
            return WetDb(space);
        }

        /// <summary>WP-097 K1 (C-00 K3): a top-down Room's own class tail, dB against the dry shot.</summary>
        public const float TopDownRoomWetDb = -6f;

        private static float Db(float db) => Mathf.Pow(10f, db / 20f);
    }
}
