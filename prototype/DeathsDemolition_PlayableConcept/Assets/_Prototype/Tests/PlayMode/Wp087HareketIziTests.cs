using System.Collections;
using System.Globalization;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-087 R3 (C-00 07.10) · -ddMoveTrace on the real run path, top-down: armed into a temporary file, W held through the run's input
    /// source for 40 frames then let go — the file has the header, one row per top-down frame with the header's column count, Move
    /// (0, 1) on the held frames, a planar speed above 5 m/s once the motor is up, control on, and no rows while the camera is third
    /// person (top-down only, user 06.10).
    /// </summary>
    [TestFixture]
    public sealed class Wp087HareketIziTests
    {
        private SpecialWeaponSahne _s;
        private ClassicWeaponId _oncekiSecim;
        private MoveTraceRecorder _iz;
        private string _yol;

        [SetUp]
        public void Hazirla() => _oncekiSecim = WeaponRegistry.SelectedWeapon;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_iz != null) Object.Destroy(_iz.gameObject);
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            CameraModeService.ResetForTests();
            WeaponRegistry.SelectedWeapon = _oncekiSecim;
            if (_yol != null && File.Exists(_yol)) File.Delete(_yol);
        }

        [UnityTest]
        [Category("WP087")]
        [Timeout(180000)]
        public IEnumerator Iz_Ustten_WTusu_SatirlarVeHiz()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Time.captureDeltaTime = 1f / 60f;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            _s.Girdi.Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.85f);
            yield return SpecialWeaponSahne.Kareler(20);
            _yol = Path.Combine(Path.GetTempPath(), "dd-movetrace-test-" + System.Guid.NewGuid().ToString("N") + ".csv");
            _iz = MoveTraceRecorder.Arm(_yol);

            _s.Girdi.Yon = new Vector2(0f, 1f);
            yield return SpecialWeaponSahne.Kareler(40);
            _s.Girdi.Yon = Vector2.zero;
            yield return SpecialWeaponSahne.Kareler(20);
            _iz.Flush();
            int tdRows = _iz.RowsWritten;

            Object.Destroy(_iz.gameObject);
            _iz = null;
            yield return null;

            string[] lines = File.ReadAllLines(_yol);
            Assert.GreaterOrEqual(lines.Length, 2, "başlık yok");
            StringAssert.StartsWith("# DD move trace", lines[0]);
            Assert.AreEqual(MoveTraceRecorder.Header, lines[1]);
            int cols = MoveTraceRecorder.Header.Split(',').Length, moveRows = 0, fastRows = 0, controlOff = 0, stillRows = 0, padRows = 0;
            for (int i = 2; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(',');
                Assert.AreEqual(cols, c.Length, "satır " + i + " sütun sayısı");
                float moveY = float.Parse(c[11], CultureInfo.InvariantCulture);
                float vx = float.Parse(c[14], CultureInfo.InvariantCulture), vz = float.Parse(c[15], CultureInfo.InvariantCulture);
                if (moveY > 0.99f) moveRows++;
                else if (moveY == 0f) stillRows++;   // gt SHOULD-1: a released frame writes 0, not the last held Move
                float padX = float.Parse(c[24], CultureInfo.InvariantCulture), padY = float.Parse(c[25], CultureInfo.InvariantCulture);
                if (!float.IsNaN(padX) && !float.IsNaN(padY)) padRows++;   // gt SHOULD-3: the stick columns parse on every row
                if (Mathf.Sqrt(vx * vx + vz * vz) > 5f) fastRows++;
                if (c[19] != "1") controlOff++;
            }

            Debug.Log("[DD-TEST] wp087.movetrace rows td " + tdRows + ", Move (0,1) rows " + moveRows + ", > 5 m/s rows " + fastRows + ", control off " + controlOff + "\n" +
                      string.Join("\n", lines, 0, Mathf.Min(lines.Length, 8)));
            // gt SHOULD-1 (07.10): ranges, not floors — a recorder writing two rows a frame or holding the last Move would pass a floor
            Assert.That(tdRows, Is.InRange(58, 62), "her üstten karede bir satır (≈ 60: 40 basılı + 20 bırakılmış; kurulum kareleri armdan önce)");
            Assert.That(moveRows, Is.InRange(38, 42), "W basılı karelerde Move (0, 1)");
            Assert.That(stillRows, Is.InRange(18, 22), "bırakılan karelerde Move y = 0 (son değer tutulmaz)");
            Assert.AreEqual(tdRows, moveRows + stillRows, "her satır ya basılı ya bırakılmış");
            Assert.AreEqual(tdRows, padRows, "gamepad sütunları her satırda");
            Assert.Greater(fastRows, 20, "motor hızlanınca düzlemsel hız > 5 m/s");
            Assert.AreEqual(0, controlOff, "kontrol açık");
        }

        // C-00 TD-only: preserve the mixed test's TP assertions in a separately excluded method.
        [UnityTest]
        [Category("WP087")]
        [Category("FPTP")]
        [Timeout(180000)]
        public IEnumerator Iz_UcuncuSahis_SatirYazmaz()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Time.captureDeltaTime = 1f / 60f;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            _yol = Path.Combine(Path.GetTempPath(), "dd-movetrace-test-" + System.Guid.NewGuid().ToString("N") + ".csv");
            _iz = MoveTraceRecorder.Arm(_yol);
            int tdRows = _iz.RowsWritten;

            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            yield return SpecialWeaponSahne.Kareler(15);
            _iz.Flush();
            int tpRows = _iz.RowsWritten - tdRows;
            Assert.AreEqual(0, tpRows, "üçüncü şahısta satır yazılmaz (yalnız TD)");
        }
    }
}
