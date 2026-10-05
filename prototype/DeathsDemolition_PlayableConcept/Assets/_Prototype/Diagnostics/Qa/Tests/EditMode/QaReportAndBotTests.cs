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
                HealthRatio = 1f,
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

            o.SecondsSinceDamage = 999f;
            o.HealthRatio = 0.5f;
            Assert.AreEqual(QaBotMode.Retreat, brain.Decide(o).Mode, "düşük can, hasar penceresi bitse de mesafe açmalı");
            o.HealthRatio = 1f;
            o.NearbyEnemies = QaBotBrain.CrowdRetreatCount;
            Assert.AreEqual(QaBotMode.Retreat, brain.Decide(o).Mode, "yoğun yakın baskıda çevrelemek yerine kaçmalı");
        }

        [Test]
        public void Bot_KacisPlanlayici_UcTaraftanKusatmada_AcikYonuSecer_DuvariReddeder()
        {
            // WP-064B: crowd north, east and west of the player at 2–4 m; south is open ground
            var threats = new[] { new Vector3(0f, 0f, 2.5f), new Vector3(1.5f, 0f, 3f), new Vector3(3f, 0f, 0.5f), new Vector3(-3f, 0f, 0.2f), new Vector3(-2f, 0f, 2.5f) };
            var weights = new[] { 1f, 1f, 1f, 1f, 1f };
            Vector3 best = Vector3.zero; float bestScore = float.NegativeInfinity;
            for (int k = 0; k < 16; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                float s = QaBotBrain.ScoreDirection(Vector3.zero, dir, 6f, 5f, threats, weights, threats.Length, Vector3.zero, Vector3.zero);
                if (s > bestScore) { bestScore = s; best = dir; }
            }
            Assert.Less(best.z, -0.7f, "açık güney seçilmeli: " + best);

            // the same open south behind a wall one stride away is rejected
            Assert.Less(QaBotBrain.ScoreDirection(Vector3.zero, Vector3.back, 0.8f, 5f, threats, weights, threats.Length, Vector3.zero, Vector3.zero), -1e5f);

            // a telegraphing body weighs more: the direction past it scores lower than past a calm one
            var one = new[] { new Vector3(2f, 0f, 2f) };
            float calm = QaBotBrain.ScoreDirection(Vector3.zero, Vector3.right, 6f, 5f, one, new[] { 1f }, 1, Vector3.zero, Vector3.zero);
            float swing = QaBotBrain.ScoreDirection(Vector3.zero, Vector3.right, 6f, 5f, one, new[] { 3f }, 1, Vector3.zero, Vector3.zero);
            Assert.Less(swing, calm);

            // near the ±45 m ring with nothing around: heading back toward the centre beats running along / into the ring
            Vector3 atEdge = new Vector3(40f, 0f, 0f);
            var none = new Vector3[0];
            float inward = QaBotBrain.ScoreDirection(atEdge, Vector3.left, 6f, 5f, none, new float[0], 0, Vector3.zero, Vector3.zero, Vector3.zero, 45f);
            float outward = QaBotBrain.ScoreDirection(atEdge, Vector3.right, 6f, 5f, none, new float[0], 0, Vector3.zero, Vector3.zero, Vector3.zero, 45f);
            Assert.Greater(inward, outward, "halkaya yakınken merkeze dönüş");
        }

        [Test]
        public void Bot_Reload_GuvenliPencerede_YariSarjorAltinda()
        {
            var brain = new QaBotBrain(1);
            var o = new QaBotObservation { CameraForward = Vector3.forward, CameraRight = Vector3.right, SecondsSinceDamage = 999f,
                HasTarget = true, TargetDistance = 12f, TargetLineOfSight = true, WeaponCanFire = true, WeaponNeedsReload = true,
                WeaponAmmoRatio = 0.3f, NearestThreatDistance = 12f };
            Assert.IsTrue(brain.Decide(o).Reload, "yakında kimse yok, şarjör yarının altında → reload");
            var brain2 = new QaBotBrain(1);
            o.NearestThreatDistance = 4f; o.TargetDistance = 4f;
            Assert.IsFalse(brain2.Decide(o).Reload, "4 m'de zombi varken dolu olmayan şarjör reload'u beklemez");
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
        public void Bot_Reload_YokSayilanBasisTekrarlanir()
        {
            // WP-078 C1: the WP-069 reserve runs dry, the runtime ignores the press and refills a magazine 4 s later —
            // a standing request must press again instead of latching until the round times out
            var brain = new QaBotBrain(1);
            var o = new QaBotObservation { CameraForward = Vector3.forward, CameraRight = Vector3.right, SecondsSinceDamage = 999f, WeaponEmpty = true, WeaponNeedsReload = true };
            Assert.IsTrue(brain.Decide(o).Reload, "ilk basış");
            int presses = 0;
            for (int i = 0; i < QaBotBrain.ReloadRepressTicks * 3; i++)
            {
                if (brain.Decide(o).Reload) presses++;
            }

            Assert.AreEqual(3, presses, "istek sürdükçe her ReloadRepressTicks kararda bir yeni basış");
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

        /// <summary>
        /// R2 (C-00 01.10, FINAL dab326ab A2 fp "stick 0 / pointer 1587"): the bot's pad must keep the scheme — input ignores
        /// focus while it lives, the host's native keyboards / pointers are held off, and Dispose gives both back.
        /// </summary>
        [Test]
        public void SanalGamepad_OdagaBakmaz_YerelKlavyeVeImleciAskiyaAlir_DisposeGeriVerir()
        {
            InputSettings.BackgroundBehavior before = InputSystem.settings.backgroundBehavior;
            var native = new System.Collections.Generic.List<InputDevice>();
            foreach (InputDevice d in InputSystem.devices) if (d.native && d.enabled && (d is Keyboard || d is Pointer)) native.Add(d);
            using (var pad = new QaVirtualGamepad())
            {
                Assert.AreEqual(InputSettings.BackgroundBehavior.IgnoreFocus, InputSystem.settings.backgroundBehavior, "bot koşusunda girdi odağa bakmamalı");
                Assert.IsTrue(pad.PadEnabled, "sanal pad açık olmalı");
                Assert.AreEqual(native.Count, pad.SuspendedNative, "yerel klavye / imleç askıda olmalı");
                foreach (InputDevice d in native) Assert.IsFalse(d.enabled, d.name + " askıda olmalı");
            }

            Assert.AreEqual(before, InputSystem.settings.backgroundBehavior, "Dispose eski arka plan davranışını geri verir");
            foreach (InputDevice d in native) Assert.IsTrue(d.enabled, d.name + " geri açılmalı");
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
