using System.Collections.Generic;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B6c · §5.3 in a v6 room (scene NavRegionMarkers, S-05 B1b): in the room inner gates ×1.2 / edge ×0.9, each point
    /// takes its nearest exit; three spawns through one exit (> 70 %) favour the other exits ×1.5; a doorway counts as the
    /// room; outside the room or outside the v6 profile every factor is 1. The profile follows WorldLayout (S-05's signal).
    /// </summary>
    [Category("WP076")]
    public sealed class SpawnRoomBalanceTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        private WorldLayoutKind _layout;

        [SetUp] public void SetUp() { _layout = WorldLayout.Current; RegionNavProfile.ResetStatics(); SpawnRoomBalance.ResetStatics(); SpawnWorldProfile.ResetStatics(); }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _made) { if (go != null) { go.GetComponent<NavRegionMarker>()?.Unregister(); Object.DestroyImmediate(go); } }
            _made.Clear();
            RegionNavProfile.ResetStatics(); SpawnRoomBalance.ResetStatics(); SpawnWorldProfile.ResetStatics();
            WorldLayout.SetCurrent(_layout);
        }

        private void Hacim(Vector3 at, string id, NavRegionKind kind, Vector3 size, string roomId)
        {
            var go = new GameObject(id); go.transform.position = at; _made.Add(go);
            var m = go.AddComponent<NavRegionMarker>();
            m.Configure(id, kind, size, null, 1f, kind == NavRegionKind.Room ? 8 : 3, false, Vector3.zero, roomId);
            m.Register();
        }

        private static SpawnPointRuntime Nokta(string id, Vector3 at, SpawnPointKind kind) =>
            new SpawnPointRuntime(0, new SpawnPointDefinition { Id = id, Position = at, BaseWeight = 1f, Enabled = true, FaceMapCenter = true, Kind = kind });

        [Test]
        public void OdaIci_KapiDengesi_KapiEsigi_OdaDisi()
        {
            // room R1 at (200, 0, 200), 16 × 10; exits west (x 191) and east (x 209)
            Hacim(new Vector3(200f, 0f, 200f), "R1", NavRegionKind.Room, new Vector3(16f, 6f, 10f), "");
            Hacim(new Vector3(191f, 0f, 200f), "R1_W", NavRegionKind.Gate, new Vector3(2f, 6f, 2.5f), "R1");
            Hacim(new Vector3(209f, 0f, 200f), "R1_E", NavRegionKind.Gate, new Vector3(2f, 6f, 2.5f), "R1");
            var bati = Nokta("W_edge", new Vector3(160f, 0f, 200f), SpawnPointKind.Edge);
            var dogu = Nokta("E_edge", new Vector3(240f, 0f, 200f), SpawnPointKind.Edge);
            var orta = Nokta("E_inner", new Vector3(225f, 0f, 205f), SpawnPointKind.Inner);
            var noktalar = new List<SpawnPointRuntime> { bati, dogu, orta };

            Assert.IsTrue(SpawnRoomBalance.Prepare(noktalar, new Vector3(200f, 0f, 200f), true), "oyuncu odada");
            Assert.AreEqual("R1", SpawnRoomBalance.CurrentRoom); Assert.AreEqual(2, SpawnRoomBalance.ExitCount);
            Assert.AreEqual(0.9f, bati.RoomFactor, 1e-5f); Assert.AreEqual(1.2f, orta.RoomFactor, 1e-5f, "orta kapı basınç");
            Assert.AreNotEqual(bati.RoomExitIndex, dogu.RoomExitIndex, "en yakın çıkış");

            for (int i = 0; i < 3; i++) SpawnRoomBalance.Record(bati);   // three bodies through the west exit
            SpawnRoomBalance.Prepare(noktalar, new Vector3(200f, 0f, 200f), true);
            Assert.AreEqual(bati.RoomExitIndex, SpawnRoomBalance.DominantExit());
            Assert.AreEqual(0.9f, bati.RoomFactor, 1e-5f, "baskın çıkış ×1");
            Assert.AreEqual(0.9f * 1.5f, dogu.RoomFactor, 1e-5f, "diğer çıkış ×1,5");
            Assert.AreEqual(1.2f * 1.5f, orta.RoomFactor, 1e-5f);

            Assert.IsTrue(SpawnRoomBalance.Prepare(noktalar, new Vector3(191f, 0f, 200f), true), "kapı eşiği = oda");
            Assert.IsFalse(SpawnRoomBalance.Prepare(noktalar, new Vector3(230f, 0f, 230f), true), "oda dışı");
            Assert.AreEqual(1f, dogu.RoomFactor); Assert.AreEqual(-1, dogu.RoomExitIndex);
            Assert.IsFalse(SpawnRoomBalance.Prepare(noktalar, new Vector3(200f, 0f, 200f), false), "v6 profili kapalı → etkisiz");
            Assert.AreEqual(1f, orta.RoomFactor);
        }

        [Test]
        public void Profil_WorldLayoutSinyalini_Izler()
        {
            WorldLayout.SetCurrent(WorldLayoutKind.V6);
            SpawnWorldProfile.Refresh();
            Assert.IsTrue(SpawnWorldProfile.IsV6, "S-05 sinyali V6 (varsayılan dünya) → v6 profili");
            WorldLayout.SetCurrent(WorldLayoutKind.Ap005);
            SpawnWorldProfile.Refresh();
            Assert.AreEqual(SpawnWorldProfile.AnyV6SceneLoaded(), SpawnWorldProfile.IsV6, "AP005 zorlamalı: v6 yalnız yüklü v6 sahnesinin yedek yolundan");
        }
    }
}
