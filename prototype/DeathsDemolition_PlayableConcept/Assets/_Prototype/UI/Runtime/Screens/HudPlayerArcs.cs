using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8b · the arcs bound to the player's figure (direction §5 E13, E17), on the HUD's reticle canvas, around
    /// the player's screen point and outside the figure - nothing is ever drawn ON the model (an arc may cover an attacker).
    /// AD 8b MUST-2: the two read apart by radius and weight, not hue.
    /// - Rear arc (E13): while the BEHIND YOU chip lives, a crisp 3 px hairline (UiSprites.ArcHairline60) at r 76,
    ///   StateDanger at 0.6, facing the attackers (ThreatHudFeed.RearDirection, S-03 data); static under reduced motion.
    /// - Damage arc (E17): on a hit, a soft brush wedge (UiSprites.ArcSoft60, "Arc.Soft") on r 48–64, DamageEdge at the edge
    ///   band's peak × FlashScale, 60 ms hold then a 400 ms fade. 12 px clear of the hairline: a rear attack landing reads
    ///   as the wedge inside the hairline, a hit from elsewhere as a wedge alone.
    /// WP-094 HUD v4 (R3, C-00 07.10; HAREKET-HUD D2, the motion lead's main-branch finding): a POOL of wedges. A second hit in
    /// the same moment used to turn the one wedge away from the first attacker; now a hit from within
    /// <see cref="SameSideDegrees"/> of a live wedge re-flares that wedge, any other takes the free (or the oldest) one.
    /// 08.10 (K/wp-095/HIS-SOZLESMESI.md §4.6, S-02's feel contract - it wins over HAREKET-HUD's 2 and 60 + 400 ms): at most
    /// <see cref="DamageWedges"/> (4) arcs, each on the contract's envelope (HudDamageEnvelope: in 0.05 s, hold 0.25 s, out 0.3 s
    /// easeInQuad) driven by <see cref="Tick"/> from the HUD's frame - one renderer alpha a live wedge, written on a change.
    /// </summary>
    public sealed class HudPlayerArcs
    {
        public const float RearRadius = 76f;
        public const float DamageRadius = 64f;   // the wedge's outer edge; its band is 48–64
        public const float RearAlpha = 0.6f;
        public const float DamageHoldSeconds = 0.06f;

        /// <summary>WP-094 (D2; feel contract §4.6 "≤ 4 yay"): the wedges in the pool, and the bearing gap under which a hit re-flares a live wedge.</summary>
        public const int DamageWedges = 4;
        public const float SameSideDegrees = 30f;

        private readonly RectTransform _root;
        private readonly Image _rear;
        private readonly Image _damage;
        private readonly Image[] _wedges = new Image[DamageWedges];
        private readonly float[] _wedgeAngle = new float[DamageWedges];
        private readonly HudSlotPool _wedgePool = new HudSlotPool(DamageWedges);
        private readonly float[] _wedgeShown = { -1f, -1f, -1f, -1f };
        private bool _rearShown;
        private float _rearAngle = float.NaN;
        private Vector2 _shownAt = new Vector2(float.NaN, float.NaN);

        public RectTransform Root
        {
            get { return _root; }
        }

        public bool RearVisible
        {
            get { return _rearShown; }
        }

        /// <summary>The E17 damage wedge's image (tests: it keeps ArcSoft60 while the rear edge arc takes the shouldered one).</summary>
        public Image DamageArc
        {
            get { return _damage; }
        }

        /// <summary>WP-094 (D2): wedge <paramref name="index"/> of <see cref="DamageWedges"/> (tests, captures).</summary>
        public Image DamageWedge(int index)
        {
            return _wedges[index];
        }

        /// <summary>WP-094 (D2): wedges flaring at <paramref name="now"/> (unscaled; tests).</summary>
        public int LiveWedges(float now)
        {
            return _wedgePool.LiveCount(now);
        }

        public HudPlayerArcs(RectTransform layer)
        {
            _root = UiFactory.NewRect("PlayerArcs", layer);
            _root.sizeDelta = Vector2.zero;

            _rear = Arc("RearArc", RearRadius, UiColor.StateDanger, UiSprites.ArcHairline60);
            _rear.canvasRenderer.SetAlpha(0f);
            _damage = Arc("DamageArc", DamageRadius, UiColor.DamageEdge, UiSprites.ArcSoft60);
            _damage.canvasRenderer.SetAlpha(0f);
            _wedges[0] = _damage;
            for (int i = 1; i < DamageWedges; i++)
            {
                _wedges[i] = Arc("DamageArc." + i, DamageRadius, UiColor.DamageEdge, UiSprites.ArcSoft60);
                _wedges[i].canvasRenderer.SetAlpha(0f);
            }

            for (int i = 0; i < DamageWedges; i++)
            {
                _wedges[i].canvasRenderer.cullTransparentMesh = true;   // a resting wedge costs no overdraw
            }
        }

        private Image Arc(string name, float radius, UiColor colour, Sprite sprite)
        {
            Image arc = UiFactory.NewImage(name, _root, colour, sprite);
            var rect = (RectTransform)arc.transform;
            rect.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            rect.anchoredPosition = Vector2.zero;
            return arc;
        }

        /// <summary>Follow the player's screen point (canvas px), whole pixels only.</summary>
        public void Place(Vector2 local)
        {
            local = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
            if (local != _shownAt)
            {
                _shownAt = local;
                _root.anchoredPosition = local;
            }
        }

        /// <summary>E13: the rear arc while the chip lives, turned towards the attackers.</summary>
        public void SetRear(bool on, Vector3 towardsAttackersWorld)
        {
            if (on)
            {
                float angle = AngleOf(UiHitDirection.ToScreenDirection(towardsAttackersWorld));
                if (!float.IsNaN(angle) && (float.IsNaN(_rearAngle) || Mathf.Abs(Mathf.DeltaAngle(angle, _rearAngle)) > 0.5f))
                {
                    _rearAngle = angle;
                    _rear.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
            }

            if (on == _rearShown)
            {
                return;
            }

            _rearShown = on;
            UiMotion.TintAlpha(_rear, on ? RearAlpha : 0f, on ? UiDuration.Fast : UiDuration.Fast, on ? UiEase.Settle : UiEase.Exit, UiMotionClass.Decor);
        }

        /// <summary>E17: a hit from <paramref name="towardsAttackerWorld"/> - the arc flares at the edge peak and fades.</summary>
        public void Flash(Vector3 towardsAttackerWorld)
        {
            float angle = AngleOf(UiHitDirection.ToScreenDirection(towardsAttackerWorld));
            if (float.IsNaN(angle))
            {
                return; // no direction: the screen edges already say "everywhere"
            }

            // WP-094 (D2): the live wedge on this side re-flares (past its rise - no dip); otherwise the free one, or the oldest.
            float now = UiFxGlobals.Now;
            float life = HudDamageEnvelope.Life;
            int slot = -1;
            for (int i = 0; i < DamageWedges; i++)
            {
                if (_wedgePool.Live(i, now) && Mathf.Abs(Mathf.DeltaAngle(angle, _wedgeAngle[i])) < SameSideDegrees)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = _wedgePool.Take(now, life);
            }
            else
            {
                float start = now - HudDamageEnvelope.RestartAge(1f);
                _wedgePool.Restart(slot, start, life);
            }

            Image wedge = _wedges[slot];
            _wedgeAngle[slot] = angle;
            wedge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            UiMotion.KillTarget(wedge, false);
            Tick(now);
        }

        /// <summary>The live wedges' alpha on the contract envelope at <paramref name="now"/> (unscaled); written only on a visible change.</summary>
        public void Tick(float now)
        {
            float peak = UiTokens.DamageEdgePeakAlpha * UiTokens.FlashScale;
            for (int i = 0; i < DamageWedges; i++)
            {
                float alpha = _wedgePool.Live(i, now) ? peak * HudDamageEnvelope.At(now - _wedgePool.StartedAt(i)) : 0f;
                if (_wedgeShown[i] < 0f || Mathf.Abs(alpha - _wedgeShown[i]) >= 1f / 255f || (alpha == 0f && _wedgeShown[i] != 0f))
                {
                    _wedgeShown[i] = alpha;
                    _wedges[i].canvasRenderer.SetAlpha(alpha);
                }
            }
        }

        /// <summary>Screen direction → the z rotation that turns the 12 o'clock arc to face it; NaN for no direction.</summary>
        private static float AngleOf(Vector2 screenDirection)
        {
            if (screenDirection.sqrMagnitude < 1e-6f)
            {
                return float.NaN;
            }

            return -Mathf.Atan2(screenDirection.x, screenDirection.y) * Mathf.Rad2Deg;
        }
    }
}
