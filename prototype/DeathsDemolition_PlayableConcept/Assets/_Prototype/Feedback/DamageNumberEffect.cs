using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// A floating damage readout.
    ///
    /// It merges rather than multiplies: a shotgun blast that lands eight pellets on
    /// one torso produces one number that counts up to 56, not eight numbers fighting
    /// for the same pixel. <see cref="Add"/> is what the feedback director calls for
    /// every later hit inside the merge window.
    ///
    /// The whole thing self-disables when no font can be resolved on the machine, which
    /// keeps the "no console errors during normal use" criterion safe on a build with
    /// no font assets.
    /// </summary>
    public sealed class DamageNumberEffect : PooledEffect
    {
        private TextMesh _text;
        private string _lastAssigned;
        private Renderer _renderer;
        private Transform _tf;

        private float _amount;
        private Color _color;
        private Vector3 _anchor;
        private Vector3 _drift;
        private float _popTimer;
        private float _mergeAnchorBlend;
        private float _mergePopScale;
        private float _mergePopSeconds;
        private float _endScale;
        private float _fadeStartNormalized;

        /// <summary>Identity of the target this number belongs to, for merging. 0 means unowned.</summary>
        public int TargetId { get; private set; }
        public ulong SnapshotRevision { get; private set; }

        /// <summary>Seconds since the last <see cref="Add"/>. The director uses it to decide on merging.</summary>
        public float TimeSinceLastAdd { get; private set; }

        private void Cache()
        {
            if (_tf != null)
            {
                return;
            }

            _tf = transform;
            _text = GetComponentInChildren<TextMesh>();
            _renderer = _text != null ? _text.GetComponent<Renderer>() : null;
        }

        public void Play(int targetId, Vector3 worldPosition, float amount, Color color, float lifetime)
        {
            Cache();

            TargetId = targetId;
            _anchor = worldPosition;
            _amount = amount;
            _color = color;
            _popTimer = 0f;
            TimeSinceLastAdd = 0f;
            DamageNumberFeel feel = FeelRuntime.Current.DamageNumbers;
            SnapshotRevision = FeelRuntime.Current.Revision;
            Lifetime = feel.lifetimeSeconds;
            _mergeAnchorBlend = feel.mergeAnchorBlend;
            _mergePopScale = feel.mergePopScale;
            _mergePopSeconds = feel.mergePopSeconds;
            _endScale = feel.endScale;
            _fadeStartNormalized = feel.fadeStartNormalized;

            // PROTOTYPE-ONLY drift: mostly up, slightly sideways so two numbers on
            // neighbouring enemies do not overlap exactly.
            _drift = new Vector3(Random.Range(-feel.driftExtentXMeters, feel.driftExtentXMeters),
                feel.driftRiseMeters, Random.Range(-feel.driftExtentZMeters, feel.driftExtentZMeters));

            Refresh();
        }

        /// <summary>Fold another hit on the same target into this number and re-pop it.</summary>
        public void Add(float amount, Color color, Vector3 worldPosition)
        {
            _amount += amount;
            _color = color;
            _anchor = Vector3.Lerp(_anchor, worldPosition, _mergeAnchorBlend);
            _popTimer = _mergePopSeconds;
            TimeSinceLastAdd = 0f;

            // Adding resets the clock so a sustained burst keeps one number alive.
            ResetAge();
            Refresh();
        }

        private void ResetAge()
        {
            RestartLife();
        }

        // WP-062: bounded table filled at type load — the lazy/unbounded cache put each unseen value's first string into a hit frame
        // (S-02 WP-053 read that as "1,75 allocations per number"; the contract observation showed 97 per first window).
        private const int NumberTableSize = 10000;
        private static readonly string[] s_numberTable = BuildNumberTable();
        private const string OverflowNumberText = "9999+";

        private static string[] BuildNumberTable()
        {
            var table = new string[NumberTableSize];
            for (int i = 0; i < table.Length; i++) table[i] = i.ToString();
            return table;
        }
        /// <summary>Integer text without hit-path allocations or an unbounded cache.</summary>
        public static string NumberText(int value)
        {
            if (value >= 0 && value < s_numberTable.Length)
            {
                return s_numberTable[value];
            }

            return OverflowNumberText;
        }

        private void Refresh()
        {
            if (_text == null)
            {
                return;
            }

            int rounded = Mathf.Max(1, Mathf.RoundToInt(_amount));
            // WP-037 finding (ADR-037-1 zero-alloc hot path): the per-number string was the whole 12 KB/window regression.
            // Cached table for 0..999, a one-time string per larger value; the text is only assigned when it changes.
            // WP-047 (S-02 WP-053: ≈ 1,75 allocations per number with the table): `TextMesh.text`'s getter marshals a new
            // managed string on every read, so the old "assign only when it changes" check itself allocated twice per
            // refresh. The last string handed to the mesh is remembered here and compared by reference instead.
            string text = NumberText(rounded);
            if (!ReferenceEquals(_lastAssigned, text))
            {
                _text.text = text;
                _lastAssigned = text;
            }

            _text.color = _color;
        }

        protected override void Tick(float t)
        {
            if (_tf == null)
            {
                return;
            }

            TimeSinceLastAdd += Time.deltaTime;

            _tf.position = _anchor + _drift * t;

            Camera cam = FeedbackCameraRef.Current;
            if (cam != null)
            {
                _tf.rotation = cam.transform.rotation;
            }

            float pop = 1f;
            if (_popTimer > 0f)
            {
                _popTimer -= Time.deltaTime;
                pop = _mergePopScale;
            }

            // Fade out over the last 40% of the life.
            float alpha = t < _fadeStartNormalized ? 1f : 1f - Mathf.InverseLerp(_fadeStartNormalized, 1f, t);
            _tf.localScale = Vector3.one * (pop * Mathf.Lerp(1f, _endScale, t));

            if (_text != null)
            {
                Color c = _color;
                c.a = alpha;
                _text.color = c;
            }

            if (_renderer != null)
            {
                _renderer.enabled = alpha > 0.01f;
            }
        }

        protected override void OnDespawned()
        {
            TargetId = 0;
            SnapshotRevision = 0;
            base.OnDespawned();
        }
    }
}
