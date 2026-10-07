using DD.Prototype.Core.Weapons;
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
    /// The player-hit source is <see cref="HudFeelSource"/> - the one seam S-02's FeelEvents will replace.
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

            Sprite ringArt = _reloadRing.Ghost != null ? _reloadRing.Ghost.sprite : UiSprites.RingThin;
            _ringKick = UiFactory.NewImage("RingKick", back, UiColor.StateWarning, ringArt);
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

            if (shown == RunHudReloadState.Reloading && reload == RunHudReloadState.Ready && _ladderGroup != null)
            {
                // A4: the slide (Decor - at rest under reduced motion); the numeral's stamp is ApplyWeaponFields' (E11).
                float now = JuiceNow;
                RectTransform ladder = (RectTransform)_ladderBack.transform.parent;
                _ladderGroup.alpha = UiMotion.ReducedMotion ? 1f : MagazineDimAlpha;
                UiMotion.Alpha(_ladderGroup, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.MoveSeconds(ladder, _ladderHome - new Vector2(0f, UiMotionTokens.MagSlidePx), _ladderHome,
                    UiMotionTokens.Seconds(UiDuration.Base), UiEase.Settle, UiMotionClass.Decor);
                _leaning = false;
                _feelBudget.Note(now, UiMotionTokens.Seconds(UiDuration.Base));
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
        /// A6 · the trigger on an empty magazine (WeaponEvents.DryFire), at most once per 250 ms: the ring and the glyph shake
        /// ±4 px twice over 160 ms (DeniedNudge, about their homes - never the text), the ring kicks StateWarning for an Instant,
        /// and the first empty pull stamps EMPTY (≤ 1.06, text-bearing). Reduced motion: the colour beat alone.
        /// </summary>
        private void OnDryFire()
        {
            float now = JuiceNow;
            if (_reloadRing == null || _ringKick == null || !_dryFireGate.Try(now))
            {
                return;
            }

            DryFireShakes++;
            _ringKick.canvasRenderer.SetAlpha(RingKickAlpha * UiTokens.FlashScale);
            UiMotion.TintAlpha(_ringKick, 0f, UiDuration.Instant, UiEase.Settle, UiMotionClass.Flash);
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

        // ================================================================ per frame (from Update): clocks and returns only

        /// <summary>The heartbeat's clock, the ladder's return from its lean, the weapon swap's stamp. Float compares on a quiet frame.</summary>
        private void TickJuice()
        {
            float now = JuiceNow;
            if (_heartbeat.Running && RunEvents.Current.Phase == RunPhase.Dead)
            {
                _heartbeat.Stop();   // E22: the death line holds the block; the heart does not beat under it
            }

            HudBeat beat = _heartbeat.Tick(now);
            if (beat != HudBeat.None && _lowRim != null)
            {
                PlayBeat(beat);
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
