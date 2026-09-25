using DD.Prototype.Core;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-075 C4 / C5 · A group of atmosphere pieces (shaft cards, fog sheets) sized by the quality tier (QUALITY-TIERS-V2 r2 §4.2): the
    /// first <see cref="CountOf"/> children stay active (the builder orders them by priority, signature pieces first), and every child
    /// renderer gets the tier's alpha multiplier (_TierAlpha). Re-applied on <see cref="QualityTiers.Changed"/>; the scene keeps all
    /// children active (edit time, tests), the cut happens in play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AtmosphereTierGroup : MonoBehaviour
    {
        private static readonly int s_tierAlpha = Shader.PropertyToID("_TierAlpha");

        [SerializeField] private int[] _counts = { 6, 10, 15 };
        [SerializeField] private float[] _alphas = { 0.7f, 1f, 1f };

        public void Configure(int performance, int high, int cinematic, float performanceAlpha, float highAlpha, float cinematicAlpha)
        {
            _counts = new[] { performance, high, cinematic };
            _alphas = new[] { performanceAlpha, highAlpha, cinematicAlpha };
        }

        public int CountOf(QualityTier tier) => _counts[(int)tier];
        public float AlphaOf(QualityTier tier) => _alphas[(int)tier];

        private void OnEnable()
        {
            QualityTiers.Changed += Apply;
            Apply(QualityTiers.Current);
        }

        private void OnDisable() => QualityTiers.Changed -= Apply;

        private void Apply(QualityTier tier)
        {
            int count = CountOf(tier);
            var block = new MaterialPropertyBlock();
            block.SetFloat(s_tierAlpha, AlphaOf(tier));
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                child.gameObject.SetActive(i < count);
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
            }
        }
    }
}
