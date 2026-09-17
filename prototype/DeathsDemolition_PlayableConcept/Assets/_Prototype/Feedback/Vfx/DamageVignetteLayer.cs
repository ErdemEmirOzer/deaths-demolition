using DD.Prototype.PlayerSystem;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · REQ-044 game feel on the player: the AP-010 damage vignette (alpha-only
    /// edge gradient, tinted Vermilion600 at runtime) flashed on every hit — in over
    /// <see cref="GameFeelSettings.VignetteFlashInFrames"/> frames, out over
    /// <see cref="GameFeelSettings.VignetteFlashOutFrames"/> — and held at the low-health
    /// alpha while health is under <see cref="GameFeelSettings.LowHealthFraction"/>. The
    /// directional shake (≤ 0.15 m / 0.2 s, ADR-019-2) is played by PlayerDamageFeedback
    /// from the same settings (WP-059: one source, one bump).
    ///
    /// Its own overlay canvas (sorting order under the hit markers), so the HUD (S-09)
    /// is not touched. Created by <see cref="EnsureExists"/> from the feedback director.
    /// </summary>
    public sealed class DamageVignetteLayer : MonoBehaviour
    {
        public const string ObjectName = "[Damage Vignette]";
        public const int SortingOrder = 40;

        private RawImage _image;
        private float _flash;          // 0..1 envelope
        private int _flashFrame;
        private int _flashInFrames;
        private int _flashOutFrames;
        private bool _flashing;
        private float _healthFraction = 1f;
        private float _shownAlpha;
        // WP-019b · AP-010B player_death_overlay: the mask darkens the frame edges over 2 s after the death (manifest: darkening 0.6 × mask × t;
        // the desaturation half of the recipe needs a post-fx shader — noted as a devir), then the pulse sheet crossfades on top.
        private RawImage _deathImage;
        private float _deathT = -1f;
        public const float DeathOverlaySeconds = 2f;
        public const float DeathOverlayDarkening = 0.6f;

        /// <summary>0 while alive, 0..1 as the death overlay comes in (test readout).</summary>
        public float DeathOverlayT => Mathf.Clamp01(_deathT);

        /// <summary>Alpha currently drawn (test readout).</summary>
        public float ShownAlpha => _shownAlpha;
        public bool IsFlashing => _flashing;
        public int Hits { get; private set; }

        public static DamageVignetteLayer EnsureExists()
        {
            DamageVignetteLayer existing = FindObjectOfType<DamageVignetteLayer>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(ObjectName, typeof(RectTransform));
            DontDestroyOnLoad(go);
            return go.AddComponent<DamageVignetteLayer>();
        }

        private void Awake()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.pixelPerfect = false;

            var imageGo = new GameObject("Vignette", typeof(RectTransform));
            imageGo.transform.SetParent(transform, false);
            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _image = imageGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            VfxCatalogEntry row = VfxLibrary.Get(VfxIds.VignetteDamage);
            _image.texture = row != null ? row.Texture : null;
            _image.color = new Color(VfxLibrary.VermilionTint.r, VfxLibrary.VermilionTint.g, VfxLibrary.VermilionTint.b, 0f);
            _image.enabled = false;

            VfxCatalogEntry overlay = VfxLibrary.Get(VfxIds.PlayerDeathOverlay);
            if (overlay != null && overlay.Texture != null)
            {
                var deathGo = new GameObject("DeathOverlay", typeof(RectTransform));
                deathGo.transform.SetParent(transform, false);
                var drect = (RectTransform)deathGo.transform;
                drect.anchorMin = Vector2.zero; drect.anchorMax = Vector2.one; drect.offsetMin = Vector2.zero; drect.offsetMax = Vector2.zero;
                _deathImage = deathGo.AddComponent<RawImage>();
                _deathImage.raycastTarget = false;
                _deathImage.texture = overlay.Texture;
                _deathImage.color = new Color(0f, 0f, 0f, 0f);
                _deathImage.enabled = false;
            }
        }

        private void OnEnable()
        {
            PlayerHealth.AnyDamaged += OnDamaged;
            PlayerHealth.AnyHealthChanged += OnHealthChanged;
            PlayerHealth.AnyPlayerDied += OnPlayerDied;
            DD.Prototype.Rounds.RunEvents.RunStarted += OnRunStarted;
        }

        private void OnDisable()
        {
            PlayerHealth.AnyDamaged -= OnDamaged;
            PlayerHealth.AnyHealthChanged -= OnHealthChanged;
            PlayerHealth.AnyPlayerDied -= OnPlayerDied;
            DD.Prototype.Rounds.RunEvents.RunStarted -= OnRunStarted;
        }

        private void OnPlayerDied(PlayerDeathInfo info)
        {
            _deathT = 0f;
        }

        private void OnRunStarted(int round)
        {
            _deathT = -1f;
            if (_deathImage != null)
            {
                _deathImage.enabled = false;
            }
        }

        private void OnDamaged(PlayerDamageInfo info)
        {
            if (info.AppliedAmount <= 0f)
            {
                return;
            }

            GameFeelSettings feel = GameFeelSettings.Active;
            Hits++;
            _flashing = true;
            _flashFrame = 0;
            _flashInFrames = feel.VignetteFlashInFrames;
            _flashOutFrames = feel.VignetteFlashOutFrames;
            _healthFraction = info.MaxHealth > 0f ? info.HealthAfter / info.MaxHealth : 1f;

            // WP-059: the directional damage bump is PlayerDamageFeedback's (it reads the same
            // GameFeelSettings.ShakePlayerDamage* numbers); this layer only draws the vignette.
        }

        private void OnHealthChanged(PlayerHealthSnapshot snapshot)
        {
            _healthFraction = snapshot.Max > 0f ? snapshot.Current / snapshot.Max : 1f;
        }

        private void Update()
        {
            GameFeelSettings feel = GameFeelSettings.Active;
            if (_flashing)
            {
                _flashFrame++;
                if (_flashFrame <= _flashInFrames)
                {
                    _flash = (float)_flashFrame / _flashInFrames;
                }
                else
                {
                    int outFrame = _flashFrame - _flashInFrames;
                    _flash = 1f - Mathf.Clamp01((float)outFrame / _flashOutFrames);
                    if (outFrame >= _flashOutFrames)
                    {
                        _flashing = false;
                        _flash = 0f;
                    }
                }
            }

            if (_deathT >= 0f && _deathImage != null)
            {
                _deathT = Mathf.Min(1f, _deathT + Time.unscaledDeltaTime / DeathOverlaySeconds);
                Color d = _deathImage.color;
                d.a = DeathOverlayDarkening * _deathT;
                _deathImage.color = d;
                _deathImage.enabled = true;
            }

            float hold = _healthFraction < feel.LowHealthFraction && _healthFraction > 0f ? feel.VignetteLowHealthAlpha : 0f;
            float alpha = Mathf.Min(GameFeelSettings.MaxVignetteAlpha, Mathf.Max(hold, _flash * feel.VignetteFlashAlpha));
            if (!Mathf.Approximately(alpha, _shownAlpha))
            {
                _shownAlpha = alpha;
                if (_image != null)
                {
                    Color c = _image.color;
                    c.a = alpha;
                    _image.color = c;
                    _image.enabled = alpha > 0.001f && _image.texture != null;
                }
            }
        }
    }
}
