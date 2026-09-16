using System.IO;
using DD.Prototype.Qa;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DD.Prototype.Qa.Tests.EditMode
{
    /// <summary>WP-026 · Report schema round-trip, bot stick mapping, virtual gamepad state, argument parsing.</summary>
    [Category("WP026")]
    public sealed class QaReportAndBotTests
    {
        [Test]
        public void Rapor_JsonGidisDonus_VeOzet20SatirAlti()
        {
            var r = QaReport.Create("UnitTest", 7);
            r.window = "clean 60 s";
            r.load1Before = "2.1";
            r.load1After = "2.4";
            for (int i = 0; i < 30; i++)
            {
                r.Add("gate." + i, i != 5, i.ToString(), "<= 9", "note " + i, "file.json");
            }

            string json = r.ToJson();
            QaReport back = QaReport.FromJson(json);
            Assert.AreEqual(QaReport.Schema, back.schema);
            Assert.AreEqual("UnitTest", back.driver);
            Assert.AreEqual(7, back.seed);
            Assert.AreEqual(30, back.gates.Count);
            Assert.IsFalse(back.pass, "bir kapı FAIL iken rapor FAIL");
            Assert.AreEqual("gate.5", FirstFailing(back));

            string summary = back.Summarize();
            string[] lines = summary.Split('\n');
            int nonEmpty = 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i].Length > 0) nonEmpty++;
            Assert.LessOrEqual(nonEmpty, 20, summary);
            StringAssert.Contains("FAIL gate.5", summary);
        }

        [Test]
        public void Rapor_TumKapilarGecince_Pass_VeDosyaYazilir()
        {
            var r = QaReport.Create("UnitTest", 1);
            r.Add("a", true, "1", "== 1", "");
            r.Add("b", true, "2", "== 2", "");
            string dir = Path.Combine(Application.temporaryCachePath, "wp026-report-test");
            r.Write(dir, "qa-report");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "qa-report.json")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "qa-report.txt")));
            Assert.IsTrue(QaReport.FromJson(File.ReadAllText(Path.Combine(dir, "qa-report.json"))).pass);
            Directory.Delete(dir, true);
        }

        [Test]
        public void Bot_StickEslemesi_KameraTabaniIleGidisDonus()
        {
            Vector3 fwd = new Vector3(0.6f, 0f, 0.8f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, new Vector3(1f, 0f, 1f).normalized, fwd, -right };
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2 stick = QaBotBrain.WorldToStick(dirs[i], fwd, right);
                Assert.LessOrEqual(stick.magnitude, 1f + 1e-4f);
                Vector3 back = QaBotBrain.StickToWorld(stick, fwd, right);
                Assert.Greater(Vector3.Dot(back, dirs[i]), 0.999f, "yön " + i + " geri dönüşte sapıyor: " + back + " vs " + dirs[i]);
            }

            Assert.AreEqual(Vector2.zero, QaBotBrain.WorldToStick(Vector3.up, fwd, right), "dikey bileşen yok sayılır");
        }

        [Test]
        public void Bot_Karar_HedefYokken_DevriyeYonu_HedefVarken_NisanVeAtes()
        {
            var brain = new QaBotBrain(3);
            var o = new QaBotObservation
            {
                CameraForward = Vector3.forward,
                CameraRight = Vector3.right,
                SecondsSinceDamage = 999f,
                WeaponCanFire = true,
                HasWaypoint = true,
                WaypointDirection = Vector3.right,
                Time = 0.1f
            };
            QaBotCommand patrol = brain.Decide(o);
            Assert.AreEqual(QaBotMode.Patrol, patrol.Mode);
            Assert.Greater(patrol.Move.x, 0.9f, "devriye sağa");
            Assert.IsFalse(patrol.Fire);

            o.HasTarget = true;
            o.PlayerPosition = Vector3.zero;
            o.TargetPosition = new Vector3(0f, 0f, 9f);
            o.TargetDistance = 9f;
            o.TargetLineOfSight = true;
            QaBotCommand engage = brain.Decide(o);
            Assert.AreEqual(QaBotMode.Engage, engage.Mode);
            Assert.Greater(engage.Aim.y, 0.9f, "nişan ileri (kamera forward = +z)");
            Assert.IsTrue(engage.Fire, "menzilde + görüş hattı + ateş edilebilir → tetik basılı");
            Assert.Less(engage.Move.y, -0.1f, "9 m tercih aralığında: yan hareket + hafif uzaklaşma bileşeni");
            Assert.Greater(Mathf.Abs(engage.Move.x), 0.5f, "yan hareket baskın");

            o.TargetDistance = 5f;
            o.TargetPosition = new Vector3(0f, 0f, 5f);
            QaBotCommand kite = brain.Decide(o);
            Assert.Less(kite.Move.y, -0.7f, "5 m: geri kaçış (kite) baskın");
            Assert.IsTrue(kite.Fire, "kite sırasında tetik basılı kalır");

            o.TargetLineOfSight = false;
            Assert.IsFalse(brain.Decide(o).Fire, "görüş hattı yokken ateş yok");
            o.TargetDistance = 9f;
            o.TargetPosition = new Vector3(0f, 0f, 9f);

            o.TargetLineOfSight = true;
            o.SecondsSinceDamage = 0.2f;
            QaBotCommand retreat = brain.Decide(o);
            Assert.AreEqual(QaBotMode.Retreat, retreat.Mode);
            Assert.Less(retreat.Move.y, -0.9f, "hasar sonrası hedeften uzağa");
        }

        [Test]
        public void Bot_Reload_TekBasis_BosSarjor()
        {
            var brain = new QaBotBrain(1);
            var o = new QaBotObservation { CameraForward = Vector3.forward, CameraRight = Vector3.right, SecondsSinceDamage = 999f, WeaponEmpty = true, WeaponNeedsReload = true };
            Assert.IsTrue(brain.Decide(o).Reload, "boş şarjör → reload basışı");
            Assert.IsFalse(brain.Decide(o).Reload, "aynı istek sürerken ikinci basış yok");
            o.WeaponEmpty = false; o.WeaponNeedsReload = false;
            brain.Decide(o);
            o.WeaponEmpty = true;
            Assert.IsTrue(brain.Decide(o).Reload, "istek düşüp yeniden gelince yeni basış");
        }

        [Test]
        public void SanalGamepad_DurumEslemesi_ProjeSemasiylaAyni()
        {
            GamepadState s = QaVirtualGamepad.BuildState(new Vector2(0.5f, -0.5f), new Vector2(2f, 0f), true, true, false);
            Assert.AreEqual(new Vector2(0.5f, -0.5f), s.leftStick);
            Assert.AreEqual(1f, s.rightStick.x, 1e-4f, "sağ çubuk birim diske kırpılır");
            Assert.AreEqual(1f, s.rightTrigger);
            Assert.AreEqual(0f, s.leftTrigger);
            Assert.AreNotEqual(0u, s.buttons & (1u << (int)GamepadButton.West), "Reload = buttonWest");
            Assert.AreEqual(0u, s.buttons & (1u << (int)GamepadButton.Start), "Pause basılı değil");
        }

        [Test]
        public void SanalGamepad_CihazEklenirVeKaldirilir()
        {
            int before = InputSystem.devices.Count;
            using (var pad = new QaVirtualGamepad())
            {
                Assert.IsTrue(pad.IsAlive);
                Assert.AreEqual(before + 1, InputSystem.devices.Count);
                pad.Set(Vector2.up, Vector2.zero, false, false, false);
                pad.Flush();
                InputSystem.Update();
                Assert.AreEqual(1f, pad.Device.leftStick.ReadValue().y, 0.05f, "kuyruğa alınan durum cihazda okunur");
            }

            Assert.AreEqual(before, InputSystem.devices.Count, "Dispose cihazı kaldırır");
        }

        [Test]
        public void Argumanlar_ListeVeSayi()
        {
            string[] args = { "app", "-ddQaBotRounds", "5", "-ddQaReadabilityRounds", "1,4,x,8", "-ddQaBot", "/tmp/out" };
            Assert.AreEqual("5", QaArgs.Arg(args, "-ddQaBotRounds"));
            Assert.AreEqual("/tmp/out", QaArgs.Arg(args, "-ddQaBot"));
            Assert.IsNull(QaArgs.Arg(args, "-ddMissing"));
        }

        private static string FirstFailing(QaReport r)
        {
            for (int i = 0; i < r.gates.Count; i++) if (!r.gates[i].pass) return r.gates[i].id;
            return null;
        }
    }
}
