using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B5 · the gel puddle registry (S-02 WP-069 §7b contract): inside = XZ radius and a ±0.6 m height band, the
    /// newest overlapping puddle wins, the cap (12, a game rule) fades the oldest within 0.5 s, touching puddles keep
    /// separate records with equalised remaining lifetimes, Expire raises the event, Configure takes S-02's data.
    /// </summary>
    [Category("WP076")]
    public sealed class GelStateTests
    {
        private int _created, _expired;
        private void OnCreated(GelPuddleInfo _) => _created++;
        private void OnExpired(int _) => _expired++;

        [SetUp] public void SetUp() { _created = 0; _expired = 0; GelPuddleRegistry.ResetStatics(); GelPuddleRegistry.PuddleCreated += OnCreated; GelPuddleRegistry.PuddleExpired += OnExpired; }
        [TearDown] public void TearDown() { GelPuddleRegistry.PuddleCreated -= OnCreated; GelPuddleRegistry.PuddleExpired -= OnExpired; GelPuddleRegistry.ResetStatics(); }

        [Test]
        public void Icerisi_YukseklikBandi_EnYeniKazanir()
        {
            int a = GelPuddleRegistry.Register(new Vector3(0f, 0f, 0f), 1.6f, 0f, 25f, 1);
            Assert.IsTrue(GelPuddleRegistry.TryFind(new Vector3(1.5f, 0.3f, 0f), out int id)); Assert.AreEqual(a, id);
            Assert.IsFalse(GelPuddleRegistry.TryFind(new Vector3(1.7f, 0f, 0f), out _), "yarıçap dışı");
            Assert.IsFalse(GelPuddleRegistry.TryFind(new Vector3(0f, 0.7f, 0f), out _), "yükseklik bandı ±0,6");
            int b = GelPuddleRegistry.Register(new Vector3(0.5f, 0f, 0f), 0.9f, 0f, 8f, 2);
            Assert.IsTrue(GelPuddleRegistry.TryFind(new Vector3(0.5f, 0f, 0f), out id)); Assert.AreEqual(b, id, "üst üste → en yeni");
            Assert.AreEqual(2, _created); Assert.AreEqual(2, GelPuddleRegistry.Count, "birleşmez, ayrı kayıt");
        }

        [Test]
        public void DeganlerinSuresiEsitlenir_Tavan_EnEskiSoner_Expire()
        {
            int a = GelPuddleRegistry.Register(new Vector3(0f, 0f, 0f), 1.6f, 0f, 25f, 1);
            int b = GelPuddleRegistry.Register(new Vector3(2.5f, 0f, 0f), 0.9f, 0f, 8f, 2);   // touches a (1.6 + 0.9 ≥ 2.5)
            GelPuddleRegistry.TryGet(a, out GelPuddleInfo ia); GelPuddleRegistry.TryGet(b, out GelPuddleInfo ib);
            Assert.AreEqual(ia.ExpiresAt, ib.ExpiresAt, 1e-4f, "kalan süre en uzuna eşitlenir");
            int far = GelPuddleRegistry.Register(new Vector3(50f, 0f, 0f), 1f, 0f, 3f, 3);
            GelPuddleRegistry.TryGet(far, out GelPuddleInfo ifar);
            Assert.Less(ifar.ExpiresAt, ia.ExpiresAt, "değmeyen kendi süresinde");

            GelPuddleRegistry.ResetStatics();
            GelPuddleRegistry.Configure(maxPuddles: 3, fadeSeconds: 0.5f);
            int first = GelPuddleRegistry.Register(new Vector3(100f, 0f, 0f), 1f, 0f, 25f, 10);
            GelPuddleRegistry.Register(new Vector3(110f, 0f, 0f), 1f, 0f, 25f, 11);
            GelPuddleRegistry.Register(new Vector3(120f, 0f, 0f), 1f, 0f, 25f, 12);
            GelPuddleRegistry.Register(new Vector3(130f, 0f, 0f), 1f, 0f, 25f, 13);   // over the cap → the first fades
            Assert.IsTrue(GelPuddleRegistry.TryGet(first, out GelPuddleInfo f));
            Assert.LessOrEqual(f.ExpiresAt, Time.time + 0.5f + 1e-4f, "en eski 0,5 s içinde söner");
            Assert.AreEqual(3, GelPuddleRegistry.MaxPuddles);

            _expired = 0;
            GelPuddleRegistry.Expire(first);
            Assert.AreEqual(1, _expired);
            Assert.IsFalse(GelPuddleRegistry.TryGet(first, out _));

            GelPuddleRegistry.ResetStatics();
            Assert.AreEqual(12, GelPuddleRegistry.MaxPuddles, "§7b varsayılanı (oyun kuralı)");
            Assert.AreEqual(0.9f, GelPuddleRegistry.SlipFallSeconds, 1e-6f);
        }
    }
}
