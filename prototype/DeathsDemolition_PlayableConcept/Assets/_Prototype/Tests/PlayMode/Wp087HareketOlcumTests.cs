using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// C-00 06.10 (the user's release play, main 4a725bfe, TopDown round 1: "the character moves on its own, WASD does not steer it
    /// right, as if a bot interferes, it reacts late") · DIAGNOSTIC measurement of the default movement path: the run's input source
    /// (IInputSource.Move, what PrototypeInputSource hands the motor) → PlayerMotor → the CharacterController, in the three camera modes,
    /// on 60 fps and on 20 fps game frames (a loaded machine's long frames). Per mode, frame rate and direction (8, the cursor held
    /// north of the player so the facing is steady): the frames from the key to the first motion and to 90 % of the steady speed, the
    /// steady speed, the angle between the asked and the actual direction, the slide after the key is let go; then 10 s without a key —
    /// alone and with three zombies round the player — the drift. Reports; asserts only that the record has the motion in it.
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp087HareketOlcumTests
    {
        private SpecialWeaponSahne _s;
        private ClassicWeaponId _oncekiSecim;

        [SetUp]
        public void Hazirla() => _oncekiSecim = WeaponRegistry.SelectedWeapon;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            CameraModeService.ResetForTests();
            WeaponRegistry.SelectedWeapon = _oncekiSecim;
        }

        private static string F(float v, string fmt = "0.00") => v.ToString(fmt, CultureInfo.InvariantCulture);

        private static Vector3 Planar(Vector3 v) { v.y = 0f; return v; }

        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20Fps_Ustten is the top-down run;
        // Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20Fps keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Explicit]
        [Timeout(1800000)]
        public IEnumerator Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20Fps_Ustten() => Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20FpsCore(new[] { CameraMode.TopDown });

        [UnityTest]
        [Explicit]
        [Timeout(1800000)]
        [Category("FPTP")]
        public IEnumerator Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20Fps() => Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20FpsCore(new[] { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson });

        private IEnumerator Hareket_WASD_Gecikme_Yon_Kayma_UcKamera_60ve20FpsCore(CameraMode[] modes)
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            PlayerMotor motor = _s.Rig.Motor;
            Assert.IsNotNull(motor, "PlayerMotor yok");
            Transform t = motor.transform;
            yield return SpecialWeaponSahne.Kareler(30);
            Vector3 start = t.position;
            var lines = new List<string>();
            var bos = new List<string>();
            var dirs = new[] { new Vector2(0, 1), new Vector2(1, 1).normalized, new Vector2(1, 0), new Vector2(1, -1).normalized, new Vector2(0, -1), new Vector2(-1, -1).normalized, new Vector2(-1, 0), new Vector2(-1, 1).normalized };
            var names = new[] { "W", "WD", "D", "SD", "S", "SA", "A", "WA" };

            foreach (CameraMode mode in modes)
            foreach (float dt in new[] { 1f / 60f, 1f / 20f })
            {
                CameraModeService.Request(mode, instant: true);
                Time.captureDeltaTime = dt;
                yield return SpecialWeaponSahne.Kareler(20);
                string head = mode + " @" + F(1f / dt, "0") + "fps";
                for (int i = 0; i < dirs.Length; i++)
                {
                    motor.Teleport(start);
                    _s.Girdi.Yon = Vector2.zero;
                    yield return SpecialWeaponSahne.Kareler(Mathf.CeilToInt(0.3f / dt));
                    // the cursor north of the player on screen (the facing stays put while it walks)
                    Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : Camera.main;
                    if (cam != null) _s.Girdi.Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.85f);
                    PrototypeCameraRig rig = PrototypeCameraRig.Active;
                    Vector3 fwd = rig != null ? rig.PlanarForward : Vector3.forward, right = rig != null ? rig.PlanarRight : Vector3.right;
                    Vector3 asked = (right * dirs[i].x + fwd * dirs[i].y).normalized;

                    int holdFrames = Mathf.CeilToInt(0.8f / dt);
                    int firstMove = -1, at90 = -1;
                    float steady = 0f;
                    var speeds = new List<float>();
                    Vector3 p0 = t.position, pMid = p0;
                    _s.Girdi.Yon = dirs[i];
                    for (int f = 0; f < holdFrames; f++)
                    {
                        yield return null;
                        float v = Planar(motor.PlanarVelocity).magnitude;
                        speeds.Add(v);
                        if (firstMove < 0 && Planar(t.position - p0).magnitude > 1e-4f) firstMove = f + 1;
                        if (f == holdFrames / 2) pMid = t.position;
                    }

                    Vector3 pHold = t.position;
                    steady = speeds[speeds.Count - 1];
                    for (int f = 0; f < speeds.Count; f++) if (at90 < 0 && speeds[f] >= 0.9f * steady && steady > 0.05f) at90 = f + 1;
                    Vector3 late = Planar(pHold - pMid);
                    float angle = late.sqrMagnitude > 1e-6f ? Vector3.Angle(late, asked) : float.NaN;
                    Vector3 facing = Planar(t.forward).normalized;

                    // let go: the slide and the frames to a stop
                    _s.Girdi.Yon = Vector2.zero;
                    int stopFrames = -1;
                    for (int f = 0; f < Mathf.CeilToInt(1.0f / dt); f++)
                    {
                        yield return null;
                        if (stopFrames < 0 && Planar(motor.PlanarVelocity).magnitude < 0.05f) stopFrames = f + 1;
                    }

                    float slide = Planar(t.position - pHold).magnitude;
                    lines.Add(head + " " + names[i] + " | first motion " + firstMove + " fr (" + F(firstMove * dt * 1000f, "0") + " ms), 90 % at " + at90 + " fr (" +
                              F(at90 * dt * 1000f, "0") + " ms), steady " + F(steady) + " m/s, asked↔actual " + F(angle, "0.0") + "°, facing↔asked " +
                              F(Vector3.Angle(facing, asked), "0") + "° | let go: stop " + stopFrames + " fr (" + F(stopFrames * dt * 1000f, "0") + " ms), slide " + F(slide * 100f, "0.0") + " cm");
                    if (firstMove < 0 || steady <= 0.05f) bos.Add(head + " " + names[i] + ": hareket kayıtta yok (tuş → hız 0)");
                }

                // no key: 10 s alone
                motor.Teleport(start);
                _s.Girdi.Yon = Vector2.zero;
                yield return SpecialWeaponSahne.Kareler(Mathf.CeilToInt(0.5f / dt));
                Vector3 q0 = t.position;
                float maxDrift = 0f;
                for (int f = 0; f < Mathf.CeilToInt(10f / dt); f++) { yield return null; maxDrift = Mathf.Max(maxDrift, Planar(t.position - q0).magnitude); }
                lines.Add(head + " no key 10 s: drift max " + F(maxDrift * 100f, "0.00") + " cm, end " + F(Planar(t.position - q0).magnitude * 100f, "0.00") + " cm");
            }

            // no key with three zombies round the player (TopDown, 60 fps): do they push it?
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            motor.Teleport(start);
            _s.Girdi.Yon = Vector2.zero;
            yield return SpecialWeaponSahne.Kareler(30);
            var zs = new List<ZombieEnemy>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad;
                ZombieEnemy z = _s.Dogur(start + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.6f);
                if (z != null) zs.Add(z);
            }

            Vector3 z0 = t.position;
            float zDrift = 0f;
            for (int f = 0; f < 600; f++) { yield return null; zDrift = Mathf.Max(zDrift, Planar(t.position - z0).magnitude); }
            lines.Add("TopDown @60fps no key 10 s with " + zs.Count + " zombies round: drift max " + F(zDrift * 100f, "0.00") + " cm, end " + F(Planar(t.position - z0).magnitude * 100f, "0.00") + " cm");

            Debug.Log("[DD-TEST] wp087.move\n" + string.Join("\n", lines));
            Assert.IsEmpty(bos, "kaynak kayıtta yok\n" + string.Join("\n", bos) + "\n" + string.Join("\n", lines));
        }
    }
}
