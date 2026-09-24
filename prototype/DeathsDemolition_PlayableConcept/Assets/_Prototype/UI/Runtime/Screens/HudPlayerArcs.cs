using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8b · the arcs bound to the player's figure (direction §5 E13, E17), on the HUD's reticle canvas. Both are
    /// 60° arcs (UiSprites.Arc60) around the player's screen point, outside the figure - nothing is ever drawn ON the model.
    /// - Rear arc (E13): while the BEHIND YOU chip lives, Coral (StateDanger) at ≤ 0.6, radius 72, facing the attackers
    ///   (ThreatHudFeed.RearDirection, S-03 data); static under reduced motion.
    /// - Damage arc (E17): on a hit, DamageEdge at the edge band's peak × FlashScale, radius 64, facing the attacker, held
    ///   60 ms then faded 400 ms - the same beat as the screen edges, where the eye already is.
    /// </summary>
    public sealed class HudPlayerArcs
    {
        public const float RearRadius = 72f;
        public const float DamageRadius = 64f;
        public const float RearAlpha = 0.6f;
        public const float DamageHoldSeconds = 0.06f;

        private readonly RectTransform _root;
        private readonly Image _rear;
        private readonly Image _damage;
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

        public HudPlayerArcs(RectTransform layer)
        {
            _root = UiFactory.NewRect("PlayerArcs", layer);
            _root.sizeDelta = Vector2.zero;

            _rear = Arc("RearArc", RearRadius, UiColor.StateDanger);
            _rear.canvasRenderer.SetAlpha(0f);
            _damage = Arc("DamageArc", DamageRadius, UiColor.DamageEdge);
            _damage.canvasRenderer.SetAlpha(0f);
        }

        private Image Arc(string name, float radius, UiColor colour)
        {
            Image arc = UiFactory.NewImage(name, _root, colour, UiSprites.Arc60);
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

            _damage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            UiMotion.KillTarget(_damage, false);
            _damage.canvasRenderer.SetAlpha(UiTokens.DamageEdgePeakAlpha * UiTokens.FlashScale);
            UiMotion.TintAlpha(_damage, 0f, UiDuration.Emphasis, UiEase.Exit, UiMotionClass.Flash, DamageHoldSeconds);
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
