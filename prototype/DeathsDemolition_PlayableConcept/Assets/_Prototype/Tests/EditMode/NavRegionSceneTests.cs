using DD.Prototype.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B6 §5.2 · scene region volumes (S-05's v6 builder): a NavRegionMarker registers while enabled; once any
    /// exists the profile answers from the scene (its AP005 list is not used); a gate inside a room wins over the room;
    /// a polygon outline is honoured; disabling removes it and the profile falls back to its own list.
    /// </summary>
    [Category("WP076")]
    public sealed class NavRegionSceneTests
    {
        private GameObject _room, _gate;

        [SetUp] public void SetUp() { RegionNavProfile.ResetStatics(); }
        [TearDown]
        public void TearDown()
        {
            if (_gate != null) { _gate.GetComponent<NavRegionMarker>().Unregister(); Object.DestroyImmediate(_gate); }
            if (_room != null) { _room.GetComponent<NavRegionMarker>().Unregister(); Object.DestroyImmediate(_room); }
            RegionNavProfile.ResetStatics();
        }

        private static NavRegionMarker Yap(ref GameObject go, Vector3 at, string id, NavRegionKind kind, Vector3 size, Vector2[] poly, int capacity, string roomId)
        {
            go = new GameObject(id);
            go.transform.position = at;
            var m = go.AddComponent<NavRegionMarker>();
            m.Configure(id, kind, size, poly, 1f, capacity, false, Vector3.zero, roomId);
            m.Register();   // EditMode: no OnEnable for a plain MonoBehaviour
            return m;
        }

        [Test]
        public void SahneHacimleri_ProfiliDegistirir_KapiOdayaBaskin_Poligon()
        {
            RegionNavProfile p = RegionNavProfile.Active;
            Assert.IsNotNull(p.Find(new Vector3(4f, 0f, -28f)), "AP005 S_Bridge (profil listesi)");

            var poly = new[] { new Vector2(100f, 100f), new Vector2(110f, 100f), new Vector2(110f, 108f), new Vector2(100f, 108f) };
            Yap(ref _room, new Vector3(105f, 0f, 104f), "R1_MainHall", NavRegionKind.Room, new Vector3(10f, 6f, 8f), poly, 8, "");
            Yap(ref _gate, new Vector3(105f, 0f, 100f), "R1_MainHall_S0", NavRegionKind.Gate, new Vector3(3f, 6f, 2f), null, 2, "R1_MainHall");

            Assert.AreEqual(2, RegionNavProfile.SceneVolumeCount);
            Assert.IsNull(p.Find(new Vector3(4f, 0f, -28f)), "sahne hacmi varken AP005 listesi kullanılmaz");
            NavRegionVolume r = p.Find(new Vector3(103f, 0f, 105f));
            Assert.AreEqual("R1_MainHall", r.id); Assert.AreEqual(NavRegionKind.Room, r.kind); Assert.AreEqual(8, r.capacity);
            NavRegionVolume g = p.Find(new Vector3(105f, 0f, 100.5f));
            Assert.AreEqual("R1_MainHall_S0", g.id, "odanın içindeki kapı odaya baskın"); Assert.AreEqual("R1_MainHall", g.roomId);
            Assert.IsNull(p.Find(new Vector3(111f, 0f, 104f)), "poligon dışı");

            _gate.GetComponent<NavRegionMarker>().Unregister(); _room.GetComponent<NavRegionMarker>().Unregister();
            Object.DestroyImmediate(_gate); Object.DestroyImmediate(_room); _gate = _room = null;
            Assert.AreEqual(0, RegionNavProfile.SceneVolumeCount);
            Assert.IsNotNull(p.Find(new Vector3(4f, 0f, -28f)), "sahne hacmi kalkınca profil listesine döner");
        }
    }
}
