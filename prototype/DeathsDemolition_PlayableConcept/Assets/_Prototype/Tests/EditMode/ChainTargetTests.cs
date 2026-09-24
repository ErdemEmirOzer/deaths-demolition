using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B4 · chain target order (S-02 WP-069 §7a contract) over the real DamageTargetRegistry (stub bodies far
    /// from the origin): the count cap (maxTargets, a game rule), the hop radius from data (6 m), greedy nearest with
    /// no first-target search limit, a wall ends the chain, rising / sliding bodies are skipped, the chain continues
    /// through any body (the Ultra included).
    /// </summary>
    [Category("WP076")]
    public sealed class ChainTargetTests
    {
        private sealed class Body : IDamageReceiver, IChainTarget
        {
            public bool CanChain { get; set; } = true;
            public DamageResult ApplyHit(in DamageHit hit) => default;
        }

        private static readonly Vector3 Origin = new Vector3(1000f, 0f, 1000f);
        private readonly List<GameObject> _made = new List<GameObject>();
        private readonly DamageTarget[] _out = new DamageTarget[ChainTargetResolver.BufferSize];

        private DamageTarget Add(float x, float z, Body body = null)
        {
            var go = new GameObject("chain_body");
            go.transform.position = Origin + new Vector3(x, 0f, z);
            _made.Add(go);
            DamageTargetRegistry.Register(go.transform, body ?? new Body(), null, 0.4f, 1.1f);
            return new DamageTarget(go.transform, body ?? new Body(), null, go.transform.position + Vector3.up * 1.1f, 0.4f);
        }

        private DamageTarget First(Body body = null)
        {
            var b = body ?? new Body();
            DamageTarget t = Add(0f, 0f, b);
            return new DamageTarget(t.Transform, b, null, t.Position, t.Radius);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _made) { if (go != null) { DamageTargetRegistry.Unregister(go.transform); Object.DestroyImmediate(go); } }
            _made.Clear();
        }

        private const float Hop = 6f;   // WeaponArcChainBlock.jumpRadius (S-02 data)

        [Test]
        public void Sira_EnYakin_HopYaricapi_Tavan_AramaSiniriYok()
        {
            DamageTarget first = First();
            Add(2f, 0f); Add(5f, 0f); Add(10.5f, 0f); Add(16f, 0f);   // hops 2, 3, 5.5, 5.5 — the chain runs 16 m out
            Add(0f, 6.5f);                                             // 6.5 m from the first → too long a hop
            int n = ChainTargetResolver.Resolve(in first, first.Position, 8, Hop, _out);
            Assert.AreEqual(5, n, "ilk + 4; ilk hedef etrafında arama sınırı yok");
            for (int i = 1; i < n; i++) Assert.Greater(_out[i].Transform.position.x, _out[i - 1].Transform.position.x - 1e-4f, "açgözlü en yakın");
            Assert.AreEqual(3, ChainTargetResolver.Resolve(in first, first.Position, 3, Hop, _out), "tavan (maxTargets, oyun kuralı)");
            Assert.AreEqual(2, ChainTargetResolver.LastLinecasts, "hop başına tek linecast");
            Assert.AreEqual(2, ChainTargetResolver.Resolve(in first, first.Position, 8, 2.5f, _out), "hop yarıçapı veriden");
        }

        [Test]
        public void KalkanKayanAtlanir_UltraZinciriDevamEttirir_Duvar()
        {
            DamageTarget first = First();
            Add(1.5f, 0f, new Body { CanChain = false });   // rising / sliding
            Add(3f, 0f);                                    // an Ultra is an ordinary link now
            Add(6f, 0f);
            int n = ChainTargetResolver.Resolve(in first, first.Position, 8, Hop, _out);
            Assert.AreEqual(3, n, "kalkan/kayan atlanır, zincir sürer");
            Assert.AreEqual(3f, _out[1].Transform.position.x - Origin.x, 1e-4f);
            TearDown();

            // a wall on Environment between the first and the next ends the chain
            first = First();
            Add(3f, 0f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _made.Add(wall);
            wall.layer = PrototypeLayers.Environment;
            wall.transform.position = Origin + new Vector3(1.5f, 1.2f, 0f);
            wall.transform.localScale = new Vector3(0.2f, 3f, 3f);
            Physics.SyncTransforms();
            Assert.AreEqual(1, ChainTargetResolver.Resolve(in first, first.Position, 8, Hop, _out), "duvar arkasına arklanmaz");
        }
    }
}
