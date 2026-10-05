using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CameraMode = DD.Prototype.CameraSystem.CameraMode;
using CameraModeService = DD.Prototype.CameraSystem.CameraModeService;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-087 (C-00 05.10) · muzzle and casing alignment on the firing frame, for every classic weapon in first person, third
    /// person and top-down, against the AP-021 model's own sockets (ADR-042-1 <c>Muzzle</c> / <c>Ejection</c>, the port's +X is
    /// the eject axis): the flash card, the muzzle light, the shot's spawn point, the casing (VFX spawn point and launch
    /// direction, the mechanism event S-08 hears, S-08's landing sound source) and where the visible casing comes to rest.
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087IsaretHizasiTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            CameraModeService.ResetForTests();
        }

        private static string Cm(float metres) => (metres * 100f).ToString("0.0", CultureInfo.InvariantCulture);
        private static string Deg(float d) => d.ToString("0.0", CultureInfo.InvariantCulture);

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator IsaretHizasi_NamluKovan_FpTpUstten()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var rows = new List<string>();
            var bos = new List<string>();
            var wrong = new List<string>();
            FieldInfo velocityField = typeof(CasingEffect).GetField("_velocity", BindingFlags.NonPublic | BindingFlags.Instance);
            var director = DD.Prototype.Audio.PrototypeAudioDirector.Instance;
            object casingAudio = director != null ? director.Casings : null;
            FieldInfo queueField = casingAudio != null ? casingAudio.GetType().GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance) : null;
            FieldInfo countField = casingAudio != null ? casingAudio.GetType().GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance) : null;

            foreach (CameraMode mode in new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson, CameraMode.TopDown })
            {
                CameraModeService.Request(mode, instant: true);
                yield return SpecialWeaponSahne.Kareler(30);
                foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                {
                    // game frames: the switch-in (0.6 s clip-motion window) has played out, the weapon is level (a batch frame is 1–3 ms)
                    Time.captureDeltaTime = 1f / 60f;
                    _s.Controller.EquipClassic(d.Id, _s.Config);
                    yield return SpecialWeaponSahne.Kareler(60);
                    if (_s.Controller.Classic.Ammo <= 0)
                    {
                        _s.Controller.Classic.TryBeginReload();
                        yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo > 0 && !_s.Controller.Classic.IsReloading, 10f, d.RosterKey + " dolmadı");
                    }

                    Transform rigT = mount.Find("ClassicWeaponRig");
                    WeaponVisualRig rig = rigT != null ? rigT.GetComponent<WeaponVisualRig>() : null;
                    if (rig == null) { bos.Add(mode + " " + d.RosterKey + ": rig yok"); continue; }
                    Time.captureDeltaTime = 1f / 60f;

                    bool spawned = false, ejected = false;
                    int ejections = 0, shots = 0;
                    Vector3 shotAt = default, eventAt = default, eventDir = default;
                    System.Action<ProjectileSpawnInfo> onSpawn = info => { if (info.PelletIndex == 0) shots++; if (!spawned) { spawned = true; shotAt = info.Position; } };
                    System.Action<WeaponMechanismEvent> onMech = e => { if (e.Kind != WeaponMechanismKind.CasingEjected) return; ejections++; if (!ejected) { ejected = true; eventAt = e.Position; eventDir = e.Direction; } };
                    ProjectileEvents.Spawned += onSpawn;
                    WeaponEvents.Mechanism += onMech;
                    string row = mode + " " + d.RosterKey;
                    try
                    {
                        _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                        yield return SpecialWeaponSahne.Bekle(() => spawned, 2f, row + " ateşlemedi");
                        _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;

                        // the firing frame: the sockets as drawn this frame
                        Transform muzzle = rig.Muzzle, port = rig.Ejection;
                        float flashCm = -1f;
                        foreach (FlipbookEffect fx in muzzle.GetComponentsInChildren<FlipbookEffect>())
                            if (fx.gameObject.activeInHierarchy) flashCm = Mathf.Max(flashCm, (fx.transform.position - muzzle.position).magnitude);
                        float lightCm = -1f;
                        MuzzleSystem lights = MuzzleSystem.Ensure();
                        Vector3 lightExpect = mode == CameraMode.FirstPerson ? DD.Prototype.CameraSystem.CameraView.ViewmodelToWorld(muzzle.position) : muzzle.position;
                        foreach (Light l in lights.GetComponentsInChildren<Light>())
                            if (l.enabled && l.intensity > 0f) { float dd = (l.transform.position - lightExpect).magnitude; lightCm = lightCm < 0f ? dd : Mathf.Min(lightCm, dd); }
                        float shotCm = (shotAt - muzzle.position).magnitude;
                        float shotDy = shotAt.y - muzzle.position.y;
                        // ≤ 1 cm: the flash card and the light on the muzzle; the shot from it (top-down: its XZ — the ray height is the
                        // WP-042 hit-parity 1.39 m, a gameplay rule, reported as dy, not asserted)
                        string at = mode + " " + d.RosterKey + ": ";
                        if (flashCm < 0f) bos.Add(at + "ağız alevi kartı yok");
                        else if (flashCm > 0.01f) wrong.Add(at + "ağız alevi namludan " + Cm(flashCm) + " cm");
                        if (lightCm > 0.01f) wrong.Add(at + "ağız ışığı namludan " + Cm(lightCm) + " cm");
                        float shotXz = Vector2.Distance(new Vector2(shotAt.x, shotAt.z), new Vector2(muzzle.position.x, muzzle.position.z));
                        if (mode == CameraMode.TopDown ? shotXz > 0.01f : shotCm > 0.01f) wrong.Add(at + "mermi doğumu namludan " + Cm(mode == CameraMode.TopDown ? shotXz : shotCm) + " cm");
                        row += " | muzzle h " + Cm(muzzle.position.y - _s.Rig.transform.position.y) + " pitch " + Deg(Mathf.Asin(Mathf.Clamp(muzzle.forward.y, -1f, 1f)) * Mathf.Rad2Deg);
                        row += " | flash " + (flashCm < 0f ? "none" : Cm(flashCm)) + " light " + (lightCm < 0f ? "none" : Cm(lightCm)) + " shot " + Cm(shotCm) + " (dy " + Cm(shotDy) + ")";

                        // the casing: VFX at the port, the event, S-08's landing source; sg_1 pumps, sg_2 breaks open on reload
                        if (d.Id == ClassicWeaponId.Sg2) _s.Controller.Classic.TryBeginReload();
                        float wait = d.Id == ClassicWeaponId.Sg2 ? 4f : 3f;
                        float ejectAt = -1f;
                        if (d.Id == ClassicWeaponId.Rl1) { row += " | casing n/a (rl_1)"; rows.Add(row); continue; }
                        float t0 = Time.time;
                        CasingEffect casing = null;
                        while (Time.time - t0 < wait && (casing == null || !ejected))
                        {
                            // the newborn casing: in flight, nearest the port (a pooled one may be recycled, so never "new since")
                            if (casing == null)
                            {
                                float best = 0.3f;
                                foreach (CasingEffect c in Object.FindObjectsOfType<CasingEffect>())
                                {
                                    if (!c.gameObject.activeInHierarchy || c.IsResting) continue;
                                    float dd = (c.transform.position - rig.Ejection.position).magnitude;
                                    if (dd < best) { best = dd; casing = c; }
                                }
                            }
                            if (ejected && ejectAt < 0f) ejectAt = Time.time - t0;
                            if (casing == null || !ejected) yield return null;
                        }
                        if (ejected && ejectAt < 0f) ejectAt = Time.time - t0;
                        int resting = 0, flying = 0;
                        foreach (CasingEffect c in Object.FindObjectsOfType<CasingEffect>()) if (c.gameObject.activeInHierarchy) { if (c.IsResting) resting++; else flying++; }

                        if (casing == null || !ejected)
                        {
                            string why = "event " + (ejected ? "at " + ejectAt.ToString("0.00") + " s" : "none") + ", VFX near port " + (casing != null) + " (active casings: flying " + flying + ", resting " + resting + ")";
                            bos.Add(row + ": kovan eksik — " + why); rows.Add(row + " | casing MISSING: " + why); continue;
                        }
                        port = rig.Ejection;
                        Vector3 v = velocityField != null ? (Vector3)velocityField.GetValue(casing) : Vector3.zero;
                        Vector3 design = port.right * 0.819f + Vector3.up * 0.574f - port.forward * 0.174f;
                        float spawnCm = (casing.transform.position - port.position).magnitude;
                        Vector3 off = port.position - casing.transform.position;
                        float lineCm = v.sqrMagnitude > 1e-6f ? (off - Vector3.Project(off, v)).magnitude : spawnCm;   // the port off the casing's flight line
                        float eventCm = (eventAt - port.position).magnitude;
                        float eventDeg = Vector3.Angle(eventDir, port.right);
                        float velRightDeg = Vector3.Angle(v, port.right), velDesignDeg = Vector3.Angle(v, design);
                        float portOutDeg = Vector3.Angle(port.right, rigT.right);
                        Vector3 audioAt = default;
                        bool audio = false;
                        if (queueField != null && countField != null)
                        {
                            var queue = (System.Array)queueField.GetValue(casingAudio);
                            int count = (int)countField.GetValue(casingAudio);
                            for (int i = 0; i < count && !audio; i++)
                            {
                                object p = queue.GetValue(i);
                                bool bounce = (bool)p.GetType().GetField("Bounce").GetValue(p);
                                if (!bounce) { audioAt = (Vector3)p.GetType().GetField("Point").GetValue(p); audio = true; }
                            }
                        }

                        float rest0 = Time.time;
                        while (casing.gameObject.activeInHierarchy && !casing.IsResting && Time.time - rest0 < 3f) yield return null;
                        Vector3 restAt = casing.transform.position;
                        int expected = d.Id == ClassicWeaponId.Sg2 ? 2 : shots;   // a shell per shot (ar_2's burst: 3); sg_2 opens on two
                        if (ejections != expected) wrong.Add(row + ": " + ejections + " kovan olayı, beklenen " + expected);
                        string atc = mode + " " + d.RosterKey + ": kovan ";
                        if (lineCm > 0.01f) wrong.Add(atc + "çıkış portundan " + Cm(lineCm) + " cm (uçuş çizgisi)");
                        if (eventCm > 0.01f) wrong.Add(atc + "olayı portdan " + Cm(eventCm) + " cm");
                        if (eventDeg > 10f) wrong.Add(atc + "olay yönü port +X'ten " + Deg(eventDeg) + "°");
                        if (velDesignDeg > 10f) wrong.Add(atc + "fırlatma yönü tasarımdan " + Deg(velDesignDeg) + "°");
                        if (portOutDeg > 10f) wrong.Add(atc + "port +X silahın sağından " + Deg(portOutDeg) + "°");
                        float audioRestCm = audio ? Vector2.Distance(new Vector2(audioAt.x, audioAt.z), new Vector2(restAt.x, restAt.z)) : -1f;
                        row += " | casing events " + ejections + " (first at " + ejectAt.ToString("0.00", CultureInfo.InvariantCulture) + " s) spawn " + Cm(spawnCm) + " (line " + Cm(lineCm) + ") event " + Cm(eventCm) + " eventDir∠+X " + Deg(eventDeg) + " vel∠+X " + Deg(velRightDeg) + " vel∠design " + Deg(velDesignDeg)
                               + " port+X∠rig+X " + Deg(portOutDeg) + " | audio " + (audio ? "at " + Cm((audioAt - port.position).magnitude) + " from port, ↔ casing rest " + Cm(audioRestCm) + " (rest " + (casing.IsResting ? "yes" : "no") + ")" : "none");
                        rows.Add(row);
                    }
                    finally
                    {
                        ProjectileEvents.Spawned -= onSpawn;
                        WeaponEvents.Mechanism -= onMech;
                        _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                        Time.captureDeltaTime = 0f;
                    }

                    yield return new WaitForSeconds(0.5f);
                }
            }

            Debug.Log("[DD-TEST] wp087.markeralign\n" + string.Join("\n", rows));
            Assert.IsEmpty(bos, "kaynak kayıtta yok\n" + string.Join("\n", bos) + "\n" + string.Join("\n", rows));
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }
    }
}
