using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 · the juice pass (R3, C-00 07.10; the user's note §4–5: "hud'un game juice ve game feel hissiyatı artsın.
    /// silah şarjör ve mühimmat göstergesi bu kaliteyi korusun ama dinamizm ve motion tasarımı geliştirilsin"), built to the
    /// binding catalogue K/wp-094/HAREKET-HUD.md (ids below are its rows):
    /// - HEALTH: H1 a chunk of the brush breaks off and falls (pool 3, the fourth widens the newest), H2 the trail (UiBar's
    ///   ghost, now behind a snapped fill), H3 the brush knocks back 3 px, H4 a moss glow over the range a heal gave and a wet
    ///   band at the tip while regenerating, H5 the low-health heartbeat - lub / dub on the heart, a coral rim breathing.
    /// - WEAPON: A1 a spark at the emptied slot and the ladder leaning out while firing (≤ 10 accents a second; the numeral
    ///   pops only on a slow single-fire weapon), A2 a casing thrown toward the ring (pool 6), A3 the empty slots preview the
    ///   reload left to right, A4 the refilled magazine slides up, A5 the ring's track and the rounds breathe at LOW, A6 an
    ///   empty trigger shakes the ring and glyph (never text) with a colour beat and stamps EMPTY once, A7 a weapon change
    ///   swaps the glyph through a stamp and re-sweeps the ladder, A8 the classic cluster steps down under a Special.
    /// RULES the whole file keeps: every clock is unscaled (a hit-stop never slows the HUD); text never shakes and never
    /// pulses its alpha; every motion has its reduced-motion variant (the Decor class lands at once, the light plays);
    /// FlashScale multiplies every flash; ornaments (P4) give way in the danger posture and over the feel budget
    /// (<see cref="HudFeelBudget"/>); nothing allocates per frame or per event (pools built once, SetText with numbers,
    /// struct tweens). The health front plane and the weapon's Content are nested canvases (BuildBrushHealth /
    /// BuildRingWeapon): a shot or a beat re-batches its own cluster, never the main HUD canvas.
    /// The player-hit source is <see cref="HudFeelSource"/> (FeelEvents.PlayerDamaged since 08.10).
    ///
    /// 08.10 · S-02's FeelEvents and feel contract (K/wp-095/FEELEVENTS-LISTE.md, HIS-SOZLESMESI.md; S-09's HUD rows - where a
    /// value differs from HAREKET-HUD, the contract wins): the HUD hears these moments from FeelEvents only - DryFire,
    /// ReloadEnded, EnemyHit (crit), EnemyKilled, MultiKill, LowHealth, RoundStarted, LastZombie, PlayerDamaged (via the seam).
    /// §4.2 crit: the reticle's crit notch 0.1 s (the number's 1.3 pop is UiWorldNumbers'); §4.3 kill: the reticle pops
    /// 1.22 → 1 over 0.12 s easeOutBack; §4.4 multi-kill: a brush "×N" by the reticle, 1.3 → 1 over 0.15 s easeOutBack, held
    /// 0.8 s, gone over 0.3 s; §4.7 low health: the FeelEvents band (≤ 30 % in, ≥ 38 % out), the beat at 70 → 110 bpm (the
    /// heartbeat clock's curve until FeelEvents.Heartbeat lands - <see cref="HudHeartbeat.External"/> takes it then), one white
    /// pulse on the way out; §4.8 the reload's landing: the count pops 1.15 → 1 over 0.10 s easeOutBack with a brush wipe; an
    /// empty trigger: two vermilion pulses × 0.08 s on the ring and the RELOAD / DOLDUR hint.
    /// </summary>
    public sealed partial class RunHudScreen
    {
        public const int HealthChunkPool = 3;
        public const int CasingPool = 6;
        private const float CasingWidth = 4f;
        private const float CasingHeight = 12f;
        private const float SparkWidth = 8f;
        private const float SparkHeight = 12f;
        private const float SparkAlpha = 0.6f;
        private const float RingKickAlpha = 0.9f;
        private const float PreviewAlpha = 0.5f;
        private const float MagazineDimAlpha = 0.35f;
        private const float AmmoPulseFloor = 0.6f;
        private const float LowRimOutset = 3f;
        private const float HealGlowSeconds = 1f;
        private const float RegenGlowShare = 0.05f;
        private const float SwitchGlyphScale = 0.88f;
        private const float CasingDriftPx = 10f;
        private const float ReloadWipeAlpha = 0.45f;

        private readonly HudFeelBudget _feelBudget = new HudFeelBudget();

        /// <summary>The ink posture is danger (a chip, LOW, EMPTY, a hit in the last 3 s, the Special's final seconds).</summary>
        public bool InDangerPosture => _posture == 1;

        /// <summary>The feel budget (tests: what the ornaments see).</summary>
        public HudFeelBudget FeelBudget => _feelBudget;

        /// <summary>The juice's clock: unscaled, and the motion core's test clock when one is set.</summary>
        private static float JuiceNow => UiFxGlobals.Now;

        // ================================================================ health (H1 – H5)

        private RectTransform _healthStroke;
        private Vector2 _healthStrokeHome;
        private float _healthStrokeLength;
        private readonly RawImage[] _chunks = new RawImage[HealthChunkPool];
        private readonly float[] _chunkFrom = new float[HealthChunkPool];
        private readonly float[] _chunkTo = new float[HealthChunkPool];
        private readonly HudSlotPool _chunkPool = new HudSlotPool(HealthChunkPool);
        private RawImage _healGlow;
        private Image _wetBand;
        private Image _lowRim;
        private Vector4 _brushUv = new Vector4(0f, 0f, 1f, 1f);
        private readonly HudRateGate _knockGate = new HudRateGate(UiMotionTokens.Seconds(UiDuration.Fast));
        private readonly HudRateGate _healGate = new HudRateGate(HealGlowSeconds);
        private readonly HudHeartbeat _heartbeat = new HudHeartbeat();
        private float _healthSeen = -1f;
        private bool _regenSeen;
        private int _wetBandPx = int.MinValue;
        private bool _feelLow;   // the FeelEvents low-health band (§4.7)

        /// <summary>H1: chunks falling now (tests).</summary>
        public int HealthChunksLive => _chunkPool.LiveCount(JuiceNow);

        /// <summary>H1: chunk <paramref name="index"/> (tests, captures).</summary>
        public RawImage HealthChunk(int index) => _chunks[index];

        /// <summary>H4: heal glows played since the build (tests).</summary>
        public int HealGlows { get; private set; }

        /// <summary>H4: the glow over the gained range (tests).</summary>
        public Graphic HealGlowGraphic => _healGlow;

        /// <summary>H5: the heartbeat runs (low health), and the lubs it has played (tests).</summary>
        public bool HeartbeatRunning => _heartbeat.Running;
        public int HeartBeats { get; private set; }

        /// <summary>H5: the coral rim that breathes with the beat (tests).</summary>
        public Graphic LowHealthRim => _lowRim;

        /// <summary>H3: brush knocks played since the build (tests).</summary>
        public int HealthKnocks { get; private set; }

        /// <summary>
        /// Built by BuildBrushHealth on the stroke (the back plane's nested canvas): the chunk pool over the fill, the heal glow
        /// and the wet band in its slice of the same brush, the coral low rim just outside the ink rim. All at alpha 0 and
        /// culled while they rest (the coverage cap counts only what shows).
        /// </summary>
        private void BuildHealthJuice(RectTransform stroke, Image fill, Image rim, float length, float height)
        {
            _healthStroke = stroke;
            _healthStrokeHome = stroke.anchoredPosition;
            _healthStrokeLength = length;
            Sprite brush = fill.sprite;
            Texture texture = brush != null ? brush.texture : null;
            if (brush != null)
            {
                _brushUv = UnityEngine.Sprites.DataUtility.GetOuterUV(brush);
            }

            // H5: the coral rim, under the ink rim (outset past it), breathing only while health is LOW.
            _lowRim = UiFactory.NewImage("LowRim", stroke, UiColor.BarHealthLow, brush);
            UiFactory.Outset(_lowRim.rectTransform, LowRimOutset);
            _lowRim.transform.SetSiblingIndex(rim.transform.GetSiblingIndex());
            Rest(_lowRim);

            RectTransform host = UiFactory.NewRect("Juice", stroke);
            UiFactory.Fill(host);
            for (int i = 0; i < HealthChunkPool; i++)
            {
                _chunks[i] = Slice("Chunk." + i, host, texture, UiColor.BarHealth, height);
            }

            _healGlow = Slice("HealGlow", host, texture, UiColor.AccentMoss, height);

            _wetBand = UiFactory.NewImage("WetBand", host, UiColor.AccentMoss, brush);
            RectTransform band = _wetBand.rectTransform;
            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(0f, 1f);
            band.pivot = new Vector2(1f, 0.5f);
            band.sizeDelta = new Vector2(UiMotionTokens.HealWetBandPx, 0f);
            Fade(_wetBand, UiMotionTokens.HealGlowAlpha);
            _wetBand.enabled = false;
        }

        /// <summary>A RawImage cutting its slice of the brush texture (uvRect written once per use), resting at alpha 0.</summary>
        private static RawImage Slice(string name, RectTransform host, Texture texture, UiColor colour, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.layer = host.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            RawImage image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.color = UiTokens.Get(colour);
            image.raycastTarget = false;
            Rest(image);
            return image;
        }

        /// <summary>At rest: alpha 0 and culled (a resting graphic draws nothing and is not counted by the coverage meter).</summary>
        private static void Rest(Graphic graphic)
        {
            graphic.canvasRenderer.cullTransparentMesh = true;
            graphic.canvasRenderer.SetAlpha(0f);
        }

        /// <summary>Lays <paramref name="image"/> over the stroke's [from..to] (0..1 of the length), its uvRect the brush's same slice.</summary>
        private void LaySlice(RawImage image, float from, float to)
        {
            float a = Mathf.Clamp01(Mathf.Min(from, to));
            float b = Mathf.Clamp01(Mathf.Max(from, to));
            RectTransform rect = image.rectTransform;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = new Vector2((a + b) * 0.5f * _healthStrokeLength, 0f);
            rect.sizeDelta = new Vector2((b - a) * _healthStrokeLength, rect.sizeDelta.y);
            float u0 = _brushUv.x + (_brushUv.z - _brushUv.x) * a;
            float u1 = _brushUv.x + (_brushUv.z - _brushUv.x) * b;
            image.uvRect = new Rect(u0, _brushUv.y, u1 - u0, _brushUv.w - _brushUv.y);
        }

        /// <summary>
        /// A player hit (HudFeelSource, after health is written): H1 a chunk of [after..before] breaks off the stroke and falls
        /// (Screen, Exit - it accelerates like a weight), the fill snaps under it and the ghost holds the trail (H2); H3 the brush
        /// knocks back. Reduced motion: no chunk (the trail carries the loss), no knock.
        /// </summary>
        private void OnHealthHit(in HudDamage info)
        {
            if (_healthStroke == null || info.AppliedAmount <= 0f)
            {
                return;
            }

            float now = JuiceNow;
            float before = info.NormalizedBefore;
            float after = info.NormalizedAfter;
            bool wide = (before - after) * _healthStrokeLength >= UiMotionTokens.ChunkMinPx;
            if (!UiMotion.ReducedMotion && wide)
            {
                BreakChunk(now, after, before);
                _healthBar.Break(after);
            }

            if (!UiMotion.ReducedMotion && info.Fraction >= UiMotionTokens.HitKnockMinFraction && _knockGate.Ready(now) &&
                _feelBudget.OrnamentAllowed(now, InDangerPosture))
            {
                // H3 (P4 ornament): the brush only - the numbers never move.
                _knockGate.Try(now);
                HealthKnocks++;
                UiMotion.MoveSeconds(_healthStroke, _healthStrokeHome - new Vector2(UiMotionTokens.HitKnockPx, 0f), _healthStrokeHome,
                    UiMotionTokens.Seconds(UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor);
            }
        }

        private void BreakChunk(float now, float after, float before)
        {
            float seconds = UiMotionTokens.Seconds(UiDuration.Screen);
            bool extended;
            int slot = _chunkPool.TakeOrExtendNewest(now, seconds, out extended);
            if (extended)
            {
                // Every chunk is falling: the fourth blow widens the newest, which falls again from the stroke (no fifth quad).
                after = Mathf.Min(_chunkFrom[slot], after);
                before = Mathf.Max(_chunkTo[slot], before);
                _chunkPool.Restart(slot, now, seconds);
            }

            _chunkFrom[slot] = after;
            _chunkTo[slot] = before;
            RawImage chunk = _chunks[slot];
            if (_healthBar.FillImage != null && chunk.color != _healthBar.FillImage.color)
            {
                chunk.color = _healthBar.FillImage.color;   // the fill's tone now (LOW turns it coral)
            }

            LaySlice(chunk, after, before);
            RectTransform rect = chunk.rectTransform;
            Vector2 from = rect.anchoredPosition;
            chunk.canvasRenderer.SetAlpha(1f);
            UiMotion.MoveSeconds(rect, from, from + new Vector2(UiMotionTokens.ChunkDriftPx, -UiMotionTokens.ChunkDropPx), seconds, UiEase.Exit, UiMotionClass.Decor);
            UiMotion.RotateSeconds(rect, 0f, -UiMotionTokens.ChunkSpinDeg, seconds, UiEase.Exit, UiMotionClass.Decor);
            UiMotion.TintAlphaSeconds(chunk, 1f, 0f, seconds, UiEase.Exit, UiMotionClass.Decor);
            _feelBudget.Note(now, seconds);
        }

        /// <summary>
        /// After every health snapshot (ApplyHealth): H4 - one step up of ≥ 5 % glows moss over the gained range (≤ 1 a second;
        /// a regen tick never does), the start of regeneration glows the tip, and while it lasts a 6 px wet band rides the fill's
        /// end (data: placed when its pixel moves); H5 - the heartbeat starts and stops with LOW.
        /// </summary>
        private void OnHealthApplied(in PlayerHealthSnapshot snapshot)
        {
            if (_healthStroke == null)
            {
                return;
            }

            float now = JuiceNow;
            float health = _model.HealthNormalized;
            bool regen = snapshot.IsRegenerating && !snapshot.IsDead;
            if (_healthSeen >= 0f && health - _healthSeen >= UiMotionTokens.HealGlowMinFraction - 1e-4f && _healGate.Try(now))
            {
                PlayHealGlow(_healthSeen, health);
            }
            else if (regen && !_regenSeen && _healGate.Try(now))
            {
                PlayHealGlow(Mathf.Max(0f, health - RegenGlowShare), health);
            }

            _healthSeen = health;
            _regenSeen = regen;

            // The wet band: data, no loop - on while the regeneration runs, at the fill's end.
            if (_wetBand.enabled != regen)
            {
                _wetBand.enabled = regen;
                _wetBandPx = int.MinValue;
            }

            if (regen)
            {
                int px = Mathf.RoundToInt(health * _healthStrokeLength);
                if (px != _wetBandPx)
                {
                    _wetBandPx = px;
                    _wetBand.rectTransform.anchoredPosition = new Vector2(px, 0f);
                }
            }
        }

        private void PlayHealGlow(float from, float to)
        {
            HealGlows++;
            LaySlice(_healGlow, from, to);
            UiPresets.Flare(_healGlow, UiMotionTokens.HealGlowAlpha * UiTokens.FlashScale, UiDuration.Fast, UiMotionTokens.Seconds(UiDuration.Fast), UiDuration.Screen);
        }

        /// <summary>
        /// §4.7: leaving the low band (not by death) - one white pulse on the bar: the rim flares cream once (Fast in, Screen out;
        /// light, so it plays under reduced motion too, × FlashScale). The next LOW takes the rim back to coral.
        /// </summary>
        private void PlayLowHealthExitPulse()
        {
            if (_lowRim == null)
            {
                return;
            }

            LowHealthExitPulses++;
            _lowRim.color = UiTokens.Get(UiColor.TextPrimary);
            UiPresets.Flare(_lowRim, UiMotionTokens.BeatRimPeak * UiTokens.FlashScale, UiDuration.Fast, 0f, UiDuration.Screen);
        }

        /// <summary>§4.7 white pulses played on leaving LOW (tests).</summary>
        public int LowHealthExitPulses { get; private set; }

        /// <summary>H5: LOW began / ended (ApplyHealth, on the flag's change).</summary>
        private void SetHeartbeat(bool low)
        {
            if (_lowRim == null)
            {
                return;
            }

            UiMotion.KillTarget(_lowRim, false);
            if (low)
            {
                _lowRim.color = UiTokens.Get(UiColor.BarHealthLow);   // after a white exit pulse
                // The cracked heart stamps first (E10); the first lub lands on its hold.
                _heartbeat.Start(JuiceNow, UiMotionTokens.StampInSeconds + UiMotionTokens.StampHoldSeconds);
                _lowRim.canvasRenderer.SetAlpha(UiMotion.ReducedMotion ? UiMotionTokens.BeatRimPeak : UiMotionTokens.BeatRimRest);
                return;
            }

            _heartbeat.Stop();
            _lowRim.canvasRenderer.SetAlpha(0f);
        }

        /// <summary>
        /// One beat on the heart glyph (lub 1.10 over Base, dub 1.05 over Fast; × 1.3 under 15 % unless the damage edges show)
        /// and, on the lub, the rim's breath (0.35 → 0.75 × FlashScale → 0.35 over Emphasis). Reduced motion (Attention): the
        /// heart rests at 1, the rim holds at 0.75. The words never pulse.
        /// </summary>
        private void PlayBeat(HudBeat beat)
        {
            if (beat == HudBeat.Lub)
            {
                HeartBeats++;
            }

            if (UiMotion.ReducedMotion)
            {
                _lowRim.canvasRenderer.SetAlpha(UiMotionTokens.BeatRimPeak);
                return;
            }

            float gain = HudHeartbeat.Gain(_model.HealthNormalized, _damageEdges != null && _damageEdges.IsShowing);
            if (_healthIcon != null)
            {
                UiMotion.ScaleSeconds(_healthIcon.rectTransform, HudHeartbeat.Peak(beat, gain), 1f,
                    UiMotionTokens.Seconds(beat == HudBeat.Lub ? UiDuration.Base : UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor);
            }

            if (beat == HudBeat.Lub)
            {
                _lowRim.canvasRenderer.SetAlpha(HudHeartbeat.RimPeak(gain, UiTokens.FlashScale));
                UiMotion.TintAlpha(_lowRim, UiMotionTokens.BeatRimRest, UiDuration.Emphasis, UiEase.Settle, UiMotionClass.Decor);
            }
        }

        // ================================================================ weapon (A1 – A8)

        private readonly HudShotCadence _shotCadence = new HudShotCadence();
        private readonly HudRateGate _dryFireGate = new HudRateGate(UiMotionTokens.DryFireMinInterval);
        private readonly HudSlotPool _casingPool = new HudSlotPool(CasingPool);
        private readonly RectTransform[] _casingArcs = new RectTransform[CasingPool];
        private readonly Image[] _casings = new Image[CasingPool];
        private Image _ladderSpark;
        private Image _ladderPreview;
        private CanvasGroup _ladderGroup;
        private Image _ringKick;
        private Vector2 _ringHome;
        private Vector2 _glyphHome;
        private int _ammoSeen = int.MinValue;
        private bool _leaning;
        private bool _weaponNamed;
        private float _switchSwapAt = -1f;
        private bool _ladderSweepPending;
        private UiTweenHandle _ladderSweep;
        private bool _emptyStamped;
        private int _previewShown = -1;
        private bool _ammoPulsing;
        private UiTweenHandle _ammoPulseRing;
        private UiTweenHandle _ammoPulseLadder;

        /// <summary>A1: accented shots since the build; A2: casings in flight; A6: empty-trigger shakes; A7: weapon swaps (tests).</summary>
        public int ShotAccents { get; private set; }
        public int CasingsLive => _casingPool.LiveCount(JuiceNow);
        public int DryFireShakes { get; private set; }
        public int WeaponSwaps { get; private set; }

        /// <summary>A3: the reload preview's rounds (−1 when hidden); A5: the LOW pulse runs (tests).</summary>
        public int ReloadPreviewRounds => _previewShown;
        public bool AmmoPulsing => _ammoPulsing;

        /// <summary>A1 / A2 / A3: the spark, the preview ladder and casing <paramref name="index"/> (tests, captures).</summary>
        public Graphic LadderSpark => _ladderSpark;
        public Image Casing(int index) => _casings[index];

        /// <summary>
        /// Built by BuildInkAmmo / BuildRingWeapon: the preview ladder between the spent rounds and the fill (A3), a group on
        /// the ladder for the slide (A4), the spark (A1) and the casing pool (A2) inside it, and the ring's colour kick (A6) on
        /// the back plane next to the ring.
        /// </summary>
        private void BuildAmmoJuice(RectTransform ladder, RectTransform back)
        {
            _ladderGroup = UiFactory.Group(ladder.gameObject);
            _ladderPreview = LadderImage("Preview", ladder, UiColor.BarReload);
            _ladderPreview.transform.SetSiblingIndex(_ladderFill.transform.GetSiblingIndex());
            _ladderPreview.enabled = false;

            _ladderSpark = UiFactory.NewImage("Spark", ladder, UiColor.TextPrimary, UiSprites.Solid);
            RectTransform spark = _ladderSpark.rectTransform;
            spark.anchorMin = Vector2.zero;
            spark.anchorMax = Vector2.zero;
            spark.pivot = new Vector2(0.5f, 0.5f);
            spark.sizeDelta = new Vector2(SparkWidth, SparkHeight);
            Rest(_ladderSpark);

            for (int i = 0; i < CasingPool; i++)
            {
                RectTransform arc = UiFactory.NewRect("Casing." + i, ladder);
                arc.anchorMin = Vector2.zero;
                arc.anchorMax = Vector2.zero;
                arc.pivot = new Vector2(0.5f, 0.5f);
                arc.sizeDelta = Vector2.zero;
                Image casing = UiFactory.NewImage("Body", arc, UiColor.TextPrimary, UiSprites.Solid);
                casing.rectTransform.sizeDelta = new Vector2(CasingWidth, CasingHeight);
                Rest(casing);
                _casingArcs[i] = arc;
                _casings[i] = casing;
            }

            // §4.8: the reload landing's brush wipe across the magazine - a cream stroke over the ladder's ink brush, inked left
            // to right (InkReveal over its own alpha), resting at 0.
            _reloadWipe = UiFactory.NewImage("ReloadWipe", (RectTransform)ladder.parent, UiColor.TextPrimary, BrushSprite());
            RectTransform wipe = _reloadWipe.rectTransform;
            wipe.anchorMin = ladder.anchorMin;
            wipe.anchorMax = ladder.anchorMax;
            wipe.pivot = new Vector2(0f, 0.5f);
            wipe.anchoredPosition = ladder.anchoredPosition + new Vector2(-8f, LadderHeight * 0.5f + 2f);
            wipe.sizeDelta = new Vector2(ladder.sizeDelta.x + 16f, 18f);
            wipe.localRotation = ladder.localRotation;
            Fade(_reloadWipe, ReloadWipeAlpha);
            Rest(_reloadWipe);
            _reloadWipeFx = _reloadWipe.gameObject.AddComponent<UiFx>();
            _reloadWipeFx.Configure(UiFxMode.InkReveal, UiDuration.Screen, new Vector3(0f, 1f, 0f), BrushChannel);
            _reloadHintWord = UiStrings.Get("hud.ammo.reload_hint", "RELOAD");

            Sprite ringArt = _reloadRing.Ghost != null ? _reloadRing.Ghost.sprite : UiSprites.RingThin;
            // §4.8: the empty trigger's two pulses are vermilion (the damage token - the accent proper is the seal's alone).
            _ringKick = UiFactory.NewImage("RingKick", back, UiColor.DamageEdge, ringArt);
            Rest(_ringKick);
        }

        /// <summary>The ring's and the glyph's homes for the empty-trigger shake (LayWeaponCluster calls it on a layout change).</summary>
        private void LayAmmoJuice(Vector2 anchor, Vector2 ringCentre)
        {
            _ringHome = ringCentre;
            _glyphHome = ringCentre;
            if (_ringKick != null)
            {
                Place(_ringKick.rectTransform, anchor, new Vector2(0.5f, 0.5f), ringCentre, new Vector2(WeaponRing, WeaponRing));
            }
        }

        /// <summary>
        /// The magazine moved (ApplyWeaponFields, Ammo): a drop with the same weapon outside a reload is a shot (A1 / A2).
        /// The count itself is set as before - data, never rolled.
        /// </summary>
        private void NoteAmmo(int ammo, bool sameWeapon)
        {
            int before = _ammoSeen;
            _ammoSeen = ammo;
            if (before == int.MinValue || !sameWeapon || ammo >= before || _model.ReloadState == RunHudReloadState.Reloading)
            {
                return;
            }

            float now = JuiceNow;
            bool single = WeaponEvents.Classic.FireMode == WeaponFireMode.Single;
            HudShotAccent accent = _shotCadence.Note(now, single);
            bool rm = UiMotion.ReducedMotion;
            if ((accent & HudShotAccent.PopNumeral) != 0 && !rm && _ammoValue != null)
            {
                UiMotion.ScaleSeconds(_ammoValue.rectTransform, HudShotCadence.PopScale, 1f, UiMotionTokens.Seconds(UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor);
            }

            if ((accent & HudShotAccent.Accent) == 0 || !_ladderFill.enabled)
            {
                return;
            }

            ShotAccents++;
            float pitch = LadderPitch();
            var slot = new Vector2(pitch * (Mathf.Clamp(ammo, 0, _ladderMagazine) + 0.5f), LadderHeight * 0.5f);

            // The spark where the round was (light: it plays under reduced motion too).
            _ladderSpark.rectTransform.anchoredPosition = slot;
            _ladderSpark.canvasRenderer.SetAlpha(SparkAlpha * UiTokens.FlashScale);
            UiMotion.TintAlpha(_ladderSpark, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Flash);
            if (rm)
            {
                return;
            }

            // The casing, thrown toward the ring (outward: right in top-down, left when the cluster is mirrored).
            float outward = WeaponClusterMirrored ? -1f : 1f;
            int c = _casingPool.Take(now, UiMotionTokens.Seconds(UiDuration.Base));
            _casings[c].sprite = _ladderPellets > 1 ? UiSprites.AmmoPip : UiSprites.Solid;
            UiPresets.Eject(_casingArcs[c], slot, CasingDriftPx * outward, _casings[c]);

            // The ladder leans out while the trigger works (Follow short), and returns in TickWeaponJuice.
            RectTransform ladder = (RectTransform)_ladderBack.transform.parent;
            UiMotion.MoveSeconds(ladder, ladder.anchoredPosition, _ladderHome + new Vector2(UiMotionTokens.LadderLeanPx * outward, 0f),
                UiMotionTokens.FollowShort, UiEase.Settle, UiMotionClass.Decor);
            _leaning = true;
        }

        private float LadderPitch()
        {
            return ((RectTransform)_ladderBack.transform).sizeDelta.x / Mathf.Max(1, _ladderMagazine);
        }

        /// <summary>
        /// The reload as the ladder tells it (ApplyWeaponFields, on a Reload / LOW change): A3 the empty slots preview the
        /// rounds coming in, left to right, tick by tick (data - exactly the reload's progress); A4 on the landing the magazine
        /// slides up 10 px and comes to full ink; A5 at LOW the ring's track and the rounds breathe (0.6 ↔ 1 at the pulse
        /// period, Attention - held at full under reduced motion), stopped by the reload or EMPTY.
        /// </summary>
        private void NoteReload(RunHudReloadState reload, RunHudReloadState shown, bool low)
        {
            int preview = -1;
            if (reload == RunHudReloadState.Reloading && _ladderPreview != null && _ladderFill.enabled)
            {
                int missing = Mathf.Max(0, _ladderMagazine - _model.Ammo);
                preview = Mathf.Clamp(_model.Ammo + Mathf.FloorToInt(_model.ReloadProgress01 * missing), 0, _ladderMagazine);
            }

            if (preview != _previewShown && _ladderPreview != null)
            {
                _previewShown = preview;
                bool on = preview > 0;
                if (_ladderPreview.enabled != on)
                {
                    _ladderPreview.enabled = on;
                }

                if (on)
                {
                    Color c = UiTokens.Get(UiColor.BarReload);
                    c.a = PreviewAlpha;
                    _ladderPreview.color = c;
                    ((RectTransform)_ladderPreview.transform).sizeDelta = new Vector2(LadderPitch() * preview, 0f);
                }
            }

            if (reload != RunHudReloadState.MagazineEmpty)
            {
                _emptyStamped = false;
            }

            SetAmmoPulse(low && reload == RunHudReloadState.Ready);
        }

        private void SetAmmoPulse(bool on)
        {
            if (on == _ammoPulsing || _reloadRing == null)
            {
                return;
            }

            _ammoPulsing = on;
            Image ghost = _reloadRing.Ghost;
            if (on)
            {
                _ammoPulseRing = UiMotion.Pulse(ghost, AmmoPulseFloor, 1f, UiTokens.PulsePeriod, GetInstanceID());
                _ammoPulseLadder = UiMotion.Pulse(_ladderFill, AmmoPulseFloor, 1f, UiTokens.PulsePeriod, GetInstanceID());
                return;
            }

            UiMotion.Kill(_ammoPulseRing, false);
            UiMotion.Kill(_ammoPulseLadder, false);
            if (ghost != null)
            {
                ghost.canvasRenderer.SetAlpha(1f);
            }

            _ladderFill.canvasRenderer.SetAlpha(1f);
        }

        /// <summary>
        /// §4.8 · the reload landed (FeelEvents.ReloadEnded, Completed, classic): the count pops 1.15 → 1 over 0.10 s easeOutBack
        /// (the contract's - E11's stamp gave way), a cream brush wipes across the magazine (InkReveal, left to right), and A4
        /// slides the ladder up 10 px into full ink. Reduced motion: no pop, no slide (Decor); the wipe shows its end state and fades.
        /// </summary>
        private void OnFeelReloadEnded(FeelReload reload)
        {
            if (reload.Special || !reload.Completed || _ammoValue == null)
            {
                return;
            }

            float now = JuiceNow;
            ReloadLandings++;
            UiMotion.PopSeconds(_ammoValue.rectTransform, ReloadPopScale, 1f, ReloadPopSeconds);
            if (_reloadWipe != null)
            {
                UiMotion.KillTarget(_reloadWipe, false);
                _reloadWipe.canvasRenderer.SetAlpha(1f);
                _reloadWipeFx.Play();
                UiMotion.TintAlpha(_reloadWipe, 0f, UiDuration.Base, UiEase.Exit, UiMotionClass.Data, UiMotionTokens.Seconds(UiDuration.Screen));
            }

            if (_ladderGroup != null && _ladderFill.enabled)
            {
                RectTransform ladder = (RectTransform)_ladderBack.transform.parent;
                _ladderGroup.alpha = UiMotion.ReducedMotion ? 1f : MagazineDimAlpha;
                UiMotion.Alpha(_ladderGroup, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.MoveSeconds(ladder, _ladderHome - new Vector2(0f, UiMotionTokens.MagSlidePx), _ladderHome,
                    UiMotionTokens.Seconds(UiDuration.Base), UiEase.Settle, UiMotionClass.Decor);
                _leaning = false;
            }

            _feelBudget.Note(now, UiMotionTokens.Seconds(UiDuration.Base));
        }

        /// <summary>§4.8: the count's pop at the reload's landing.</summary>
        public const float ReloadPopScale = 1.15f;
        public const float ReloadPopSeconds = 0.10f;

        /// <summary>§4.8: the empty trigger's vermilion pulses - two of 0.08 s, 0.08 s apart.</summary>
        public const float DryPulseSeconds = 0.08f;

        /// <summary>Reload landings the HUD played (tests).</summary>
        public int ReloadLandings { get; private set; }

        private Image _reloadWipe;
        private UiFx _reloadWipeFx;
        private float _dryPulseAt = -1f;
        private float _dryPulseShown = -1f;
        private string _reloadHintWord;

        /// <summary>
        /// A6 · the trigger on an empty magazine (FeelEvents.DryFire - §4.8 "≤ 4/s"), at most once per 250 ms: the ring and the
        /// glyph shake ±4 px twice over 160 ms (DeniedNudge, about their homes - never the text), the ring pulses vermilion twice
        /// × 0.08 s (TickJuice), the caption becomes the RELOAD / DOLDUR hint beside the reload key, and the first empty pull
        /// stamps it (≤ 1.06, text-bearing). Reduced motion: the pulses and the hint alone.
        /// </summary>
        private void OnFeelDryFire(FeelDryFire dry)
        {
            float now = JuiceNow;
            if (_reloadRing == null || _ringKick == null || !_dryFireGate.Try(now))
            {
                return;
            }

            DryFireShakes++;
            _dryPulseAt = now;
            _dryPulseShown = -1f;
            TickDryPulse(now);
            if (_model.ReloadState == RunHudReloadState.MagazineEmpty && _reloadCaption != null && _reloadHintWord != null)
            {
                _reloadCaption.text = _reloadHintWord;
            }

            if (UiMotion.ReducedMotion)
            {
                return;
            }

            UiPresets.DeniedNudge((RectTransform)_reloadRing.transform, _ringHome);
            UiPresets.DeniedNudge(_weaponIcon.rectTransform, _glyphHome);
            if (!_emptyStamped && _model.ReloadState == RunHudReloadState.MagazineEmpty)
            {
                _emptyStamped = true;
                UiMotion.ScaleSeconds(_reloadCaption.rectTransform, UiMotionTokens.TextPopMaxScale, 1f, UiMotionTokens.StampInSeconds, UiEase.Exit, UiMotionClass.Decor);
            }

            _feelBudget.Note(now, UiMotionTokens.DeniedNudgeSeconds);
        }

        /// <summary>
        /// A7 · the weapon in hand changed (not the run's first name): the old glyph shrinks to 0.88 and fades (Fast, Exit), the
        /// new one stamps in 140 ms later (TickWeaponJuice), and the re-laid ladder sweeps its rounds in (Base, Brush). Reduced
        /// motion: the swap at once. Returns false when the glyph is to be applied now.
        /// </summary>
        private bool BeginWeaponSwap()
        {
            bool first = !_weaponNamed;
            _weaponNamed = true;
            if (first || UiMotion.ReducedMotion || _weaponIcon == null)
            {
                return false;
            }

            WeaponSwaps++;
            RectTransform glyph = _weaponIcon.rectTransform;
            UiMotion.Scale(glyph, SwitchGlyphScale, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
            UiMotion.TintAlpha(_weaponIcon, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
            _switchSwapAt = JuiceNow + UiMotionTokens.Seconds(UiDuration.Fast);
            _ladderSweepPending = true;
            _feelBudget.Note(JuiceNow, UiMotionTokens.Seconds(UiDuration.Fast) + UiMotionTokens.StampInSeconds + UiMotionTokens.StampHoldSeconds);
            return true;
        }

        /// <summary>
        /// A7: after the swap's re-lay, the fill sweeps from nothing to its rounds (the sweep yields to the next shot). A
        /// magazine change that is not the swap's (<paramref name="swapLay"/> false: a shot after a same-size swap) drops it.
        /// </summary>
        private void SweepLadderIfPending(bool swapLay)
        {
            if (!_ladderSweepPending)
            {
                return;
            }

            _ladderSweepPending = false;
            if (!swapLay || !_ladderFill.enabled)
            {
                return;
            }

            var fill = (RectTransform)_ladderFill.transform;
            Vector2 full = fill.sizeDelta;
            fill.sizeDelta = new Vector2(0f, full.y);
            _ladderSweep = UiMotion.Resize(fill, full, UiDuration.Base, UiEase.Brush, UiMotionClass.Decor);
        }

        /// <summary>A8 · a Special took the hand (or gave it back): the classic cluster steps 6 px down (Base), and the glyph stamps on its return.</summary>
        private void NoteSuspended(bool suspended)
        {
            if (_classicGroup == null)
            {
                return;
            }

            var content = (RectTransform)_classicGroup.transform;
            UiMotion.Move(content, suspended ? new Vector2(0f, -UiMotionTokens.SuspendSlidePx) : Vector2.zero, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            if (!suspended && _weaponIcon != null)
            {
                UiPresets.StampIn(_weaponIcon.rectTransform, UiMotionTokens.Seconds(UiDuration.Base));
            }
        }

        // ================================================================ the reticle's feel (feel contract §4.2 – §4.4)

        public const float MultiKillPopScale = 1.3f;
        public const float MultiKillPopSeconds = 0.15f;
        public const float MultiKillHoldSeconds = 0.8f;
        public const float MultiKillFadeSeconds = 0.3f;
        private static readonly Vector2 MultiKillOffset = new Vector2(52f, 36f);
        private static readonly Vector2 MultiKillHome = new Vector2(0f, 180f);

        private RectTransform _reticleLayer;
        private RectTransform _multiRoot;
        private TMPro.TMP_Text _multiText;
        private Image _multiBrush;
        private float _multiShownAt = -1f;
        private int _multiFading;
        private readonly HudRateGate _killPopGate = new HudRateGate(KillBeatSeconds);

        /// <summary>§4.3 (S-02's rule): kill beats at least this far apart - the pops of a sweep read as a beat.</summary>
        public const float KillBeatSeconds = 0.12f;

        /// <summary>§4.4: "×N" counters shown (tests), and the last count.</summary>
        public int MultiKillsShown { get; private set; }
        public int MultiKillCount { get; private set; }

        /// <summary>§4.4: the counter's root (tests, captures).</summary>
        public RectTransform MultiKillRoot => _multiRoot;

        /// <summary>
        /// §4.4: a brush "×N" (only the numeral and the sign - no word) on the reticle's canvas: the HUD ink brush under a cream
        /// display-face count, resting at alpha 0.
        /// </summary>
        private void BuildMultiKill(RectTransform layer)
        {
            _multiRoot = UiFactory.NewRect("MultiKill", layer);
            _multiRoot.sizeDelta = new Vector2(120f, 48f);
            _multiRoot.anchoredPosition = OffScreen;
            _multiBrush = UiFactory.NewImage("Brush", _multiRoot, UiColor.HudInk, BrushSprite());
            UiFactory.Fill(_multiBrush.rectTransform);
            _multiBrush.rectTransform.localRotation = Quaternion.Euler(0f, 0f, StrokeLean);
            Fade(_multiBrush, RoundSmearAuthoredAlpha);
            Rest(_multiBrush);
            _multiText = InkText("Count", _multiRoot, string.Empty, UiType.H1, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            UiFactory.Fill(_multiText.rectTransform);
            Rest(_multiText);
        }

        /// <summary>
        /// §4.4 (FeelEvents.MultiKill - one per kill at most, on a tier's promotion): "×N" by the aim, popping 1.3 → 1 over 0.15 s
        /// easeOutBack, held 0.8 s, gone over 0.3 s. A new tier re-pops it with the new count. Reduced motion: no pop (Decor), it
        /// stands and goes on the same clock (the fade is light, Data).
        /// </summary>
        private void OnFeelMultiKill(FeelMultiKill multi)
        {
            if (_multiRoot == null)
            {
                return;
            }

            float now = JuiceNow;
            MultiKillsShown++;
            MultiKillCount = multi.Count;
            _multiRoot.anchoredPosition = _reticleLocal.x > -50000f && !float.IsNaN(_reticleLocal.x) ? _reticleLocal + MultiKillOffset : MultiKillHome;
            _multiText.SetText("\u00D7{0}", multi.Count);
            UiMotion.KillTarget(_multiText, false);
            UiMotion.KillTarget(_multiBrush, false);
            _multiText.canvasRenderer.SetAlpha(1f);
            _multiBrush.canvasRenderer.SetAlpha(1f);
            UiMotion.PopSeconds(_multiRoot, MultiKillPopScale, 1f, MultiKillPopSeconds);
            _multiShownAt = now;
            _multiFading = 0;
            _feelBudget.Note(now, MultiKillPopSeconds);
        }

        /// <summary>The ×N's hold, then its fade (one tween each graphic, started once).</summary>
        private void TickMultiKill(float now)
        {
            if (_multiShownAt < 0f || _multiFading != 0 || now - _multiShownAt < MultiKillHoldSeconds)
            {
                return;
            }

            _multiFading = 1;
            _multiShownAt = -1f;
            UiMotion.TintAlphaSeconds(_multiText, 1f, 0f, MultiKillFadeSeconds, UiEase.Exit, UiMotionClass.Data);
            UiMotion.TintAlphaSeconds(_multiBrush, 1f, 0f, MultiKillFadeSeconds, UiEase.Exit, UiMotionClass.Data);
        }

        /// <summary>§4.2 (FeelEvents.EnemyHit): a critical hit shows the reticle's crit notch; ground-fire ticks never do.</summary>
        private void OnFeelHit(FeelHit hit)
        {
            if (!hit.Critical || hit.GroundFire || _reticle == null)
            {
                return;
            }

            _reticle.ShowCritNotch();
        }

        /// <summary>§4.3 (FeelEvents.EnemyKilled): the reticle pops 1.22 → 1 over 0.12 s easeOutBack - kill beats ≥ 0.12 s apart.</summary>
        private void OnFeelKill(FeelKill kill)
        {
            if (_reticle == null || !_killPopGate.Try(JuiceNow))
            {
                return;
            }

            _reticle.PlayKillPop();
        }

        // ================================================================ per frame (from Update): clocks and returns only

        /// <summary>§4.8: the empty trigger's two vermilion pulses - on 0–0.08 s and 0.16–0.24 s after the pull, × FlashScale.</summary>
        private void TickDryPulse(float now)
        {
            if (_dryPulseAt < 0f)
            {
                return;
            }

            float t = now - _dryPulseAt;
            bool on = t < DryPulseSeconds || (t >= 2f * DryPulseSeconds && t < 3f * DryPulseSeconds);
            float alpha = on ? RingKickAlpha * UiTokens.FlashScale : 0f;
            if (alpha != _dryPulseShown)
            {
                _dryPulseShown = alpha;
                _ringKick.canvasRenderer.SetAlpha(alpha);
            }

            if (t >= 3f * DryPulseSeconds)
            {
                _dryPulseAt = -1f;
            }
        }

        /// <summary>The heartbeat's clock, the ladder's return from its lean, the weapon swap's stamp. Float compares on a quiet frame.</summary>
        private void TickJuice()
        {
            float now = JuiceNow;
            if (_heartbeat.Running && RunEvents.Current.Phase == RunPhase.Dead)
            {
                _heartbeat.Stop();   // E22: the death line holds the block; the heart does not beat under it
            }

            _heartbeat.SetRate(_model.HealthNormalized);   // §4.7: 70 bpm at 30 % → 110 bpm at 10 %
            HudBeat beat = _heartbeat.Tick(now);
            if (beat != HudBeat.None && _lowRim != null)
            {
                PlayBeat(beat);
            }

            TickDryPulse(now);
            TickMultiKill(now);
            if (_playerArcs != null)
            {
                _playerArcs.Tick(now);   // §4.6: the wedges' envelope
            }

            if (_leaning && now - _shotCadence.LastShotAt >= UiMotionTokens.FollowMid && _ladderBack != null)
            {
                _leaning = false;
                var ladder = (RectTransform)_ladderBack.transform.parent;
                UiMotion.MoveSeconds(ladder, ladder.anchoredPosition, _ladderHome, UiMotionTokens.FollowMid, UiEase.Settle, UiMotionClass.Decor);
            }

            if (_switchSwapAt > 0f && now >= _switchSwapAt)
            {
                _switchSwapAt = -1f;
                ApplyWeaponIcon();
                UiMotion.KillTarget(_weaponIcon, false);
                _weaponIcon.canvasRenderer.SetAlpha(1f);
                UiPresets.StampIn(_weaponIcon.rectTransform);
            }
        }

        /// <summary>A new run on this HUD: the juice forgets the last run (no swap, no chunk, no beat carries over).</summary>
        private void ResetJuice()
        {
            _ammoSeen = int.MinValue;
            _weaponNamed = false;
            _switchSwapAt = -1f;
            _ladderSweepPending = false;
            _leaning = false;
            _emptyStamped = false;
            _healthSeen = -1f;
            _regenSeen = false;
            _feelLow = FeelEvents.InLowHealth;
            _dryPulseAt = -1f;
            _shotCadence.Reset();
            _chunkPool.Clear();
            _casingPool.Clear();
            _feelBudget.Clear();
            _heartbeat.Stop();
            SetAmmoPulse(false);
            if (_lowRim != null)
            {
                _lowRim.canvasRenderer.SetAlpha(0f);
            }
        }
    }
}
