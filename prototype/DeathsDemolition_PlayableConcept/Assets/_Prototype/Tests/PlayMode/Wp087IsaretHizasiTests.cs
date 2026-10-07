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

        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — IsaretHizasi_NamluKovan_FpTpUstten_Ustten is the top-down run;
        // IsaretHizasi_NamluKovan_FpTpUstten keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator IsaretHizasi_NamluKovan_FpTpUstten_Ustten() => IsaretHizasi_NamluKovan_FpTpUsttenCore(new[] { CameraMode.TopDown });

        [UnityTest]
        [Timeout(900000)]
        [Category("FPTP")]
        public IEnumerator IsaretHizasi_NamluKovan_FpTpUstten() => IsaretHizasi_NamluKovan_FpTpUsttenCore(new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson, CameraMode.TopDown });

        private IEnumerator IsaretHizasi_NamluKovan_FpTpUsttenCore(CameraMode[] modes)
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

            foreach (CameraMode mode in modes)
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

        /// <summary>
        /// WP-087 (a) (C-00 05.10): the tracer starts at the visible muzzle. In top-down the shot's ray runs at the 1.39 m hit-parity
        /// height; the ribbon is drawn shifted onto the muzzle on the firing frame (its line ≤ 1 cm from the muzzle) and is on the ray
        /// by the second frame; first and third person fire from the muzzle and draw no shift. The ray itself is untouched.
        /// </summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — IzGorseli_NamludanBaslar_IkiKaredeIsina_Ustten is the top-down run;
        // IzGorseli_NamludanBaslar_IkiKaredeIsina keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator IzGorseli_NamludanBaslar_IkiKaredeIsina_Ustten() => IzGorseli_NamludanBaslar_IkiKaredeIsinaCore(new[] { CameraMode.TopDown });

        [UnityTest]
        [Timeout(300000)]
        [Category("FPTP")]
        public IEnumerator IzGorseli_NamludanBaslar_IkiKaredeIsina() => IzGorseli_NamludanBaslar_IkiKaredeIsinaCore(new[] { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson });

        private IEnumerator IzGorseli_NamludanBaslar_IkiKaredeIsinaCore(CameraMode[] modes)
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
            var rows = new List<string>();
            var wrong = new List<string>();
            foreach (CameraMode mode in modes)
            {
                CameraModeService.Request(mode, instant: true);
                Time.captureDeltaTime = 1f / 60f;
                yield return SpecialWeaponSahne.Kareler(30);
                _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);   // the switch-in has played out (game frames)
                WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
                int id = -1;
                System.Action<ProjectileSpawnInfo> onSpawn = info => { if (id < 0) id = info.ProjectileId; };
                ProjectileEvents.Spawned += onSpawn;
                try
                {
                    _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                    yield return SpecialWeaponSahne.Bekle(() => id >= 0, 2f, mode + " ar_1 ateşlemedi");
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    string row = mode.ToString();
                    float firstLine = -1f, lift0 = -1f, offset = -1f;
                    int onRay = -1;
                    for (int f = 0; f <= 3; f++)
                    {
                        if (f > 0) yield return null;
                        int live = -1;
                        for (int i = 0; i < ProjectileSystem.LiveCount; i++) if (ProjectileSystem.Get(i).Generation == id) { live = i; break; }
                        if (live < 0) break;
                        ProjectileState p = ProjectileSystem.Get(live);
                        Vector3 lift = TracerRibbonRenderer.MuzzleCatchUp(p, Time.deltaTime);
                        if (f == 0)
                        {
                            // the drawn streak's line (head + lift, along the ray) against the muzzle as drawn this frame
                            Vector3 head = p.Position + lift, toMuzzle = rig.Muzzle.position - head;
                            firstLine = (toMuzzle - Vector3.Project(toMuzzle, p.Direction)).magnitude;
                            lift0 = lift.magnitude;
                            offset = p.Shot.HasTracerOrigin ? (p.Shot.TracerOrigin - p.Origin).magnitude : 0f;
                        }

                        if (onRay < 0 && lift.magnitude <= 0.001f) onRay = f;
                    }

                    row += " | firing frame: drawn line ↔ muzzle " + Cm(firstLine) + " cm, shift " + Cm(lift0) + " cm (muzzle ↔ ray " + Cm(offset) + " cm) | on the ray from frame " + onRay;
                    rows.Add(row);
                    if (firstLine < 0f) wrong.Add(mode + ": iz ölçülemedi (mermi bulunamadı)");
                    else if (firstLine > 0.01f) wrong.Add(mode + ": ilk karede iz çizgisi namludan " + Cm(firstLine) + " cm");
                    if (onRay < 0 || onRay > 2) wrong.Add(mode + ": iz iki karede ışına yetişmedi (kare " + onRay + ")");
                    if (mode != CameraMode.TopDown && lift0 > 0.001f) wrong.Add(mode + ": FP/TP'de iz kaydırıldı (" + Cm(lift0) + " cm)");
                    if (mode == CameraMode.TopDown && offset < 0.02f) wrong.Add("TopDown: namlu ↔ ışın farkı yok (" + Cm(offset) + " cm) — ölçüm boş");
                }
                finally
                {
                    ProjectileEvents.Spawned -= onSpawn;
                    _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                    Time.captureDeltaTime = 0f;
                }

                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("[DD-TEST] wp087.tracerorigin\n" + string.Join("\n", rows));
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }

        private sealed class KovanIzi
        {
            public int Serial, Frame;
            public string Row;
            public CasingEffect Effect;
            public Vector3 PredTouch, PredRest, Touch, Rest, Audio;
            public bool Touched, Rested, HasAudio;
        }

        /// <summary>
        /// WP-087 (C-00 05.10): every visible casing announces its throw (CasingEvents.Launched — the per-shot 0.85–1.15 speed jitter
        /// included, with the shot's serial), and CasingEffect.PredictLanding turns it into where that casing touches down and lies:
        /// FP / TP / top-down, every classic with a casing, ≥ 10 casings each — prediction ↔ the visible casing ≤ 10 cm (a frame's
        /// travel), the mechanism event of the same shot carries the same serial. The casing sound's landing (S-08's CasingAudio) is
        /// placed from this event lands on the visible touchdown ≤ 25 cm (WP-085, frames that threw one casing).
        /// </summary>
        // User 06.10 / 07.10 (C-00 (A)): tests run top-down only and FP / TP stay as they are — KovanOlayi_TahminGorunenKovanla_SesRapor_Ustten is the top-down run;
        // KovanOlayi_TahminGorunenKovanla_SesRapor keeps its original rows under FPTP (main-ci leaves that category out).
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator KovanOlayi_TahminGorunenKovanla_SesRapor_Ustten() => KovanOlayi_TahminGorunenKovanla_SesRaporCore(new[] { CameraMode.TopDown });

        [UnityTest]
        [Timeout(900000)]
        [Category("FPTP")]
        public IEnumerator KovanOlayi_TahminGorunenKovanla_SesRapor() => KovanOlayi_TahminGorunenKovanla_SesRaporCore(new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson, CameraMode.TopDown });

        private IEnumerator KovanOlayi_TahminGorunenKovanla_SesRaporCore(CameraMode[] modes)
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            FieldInfo floorField = typeof(CasingEffect).GetField("_floorY", BindingFlags.NonPublic | BindingFlags.Instance);
            var director = DD.Prototype.Audio.PrototypeAudioDirector.Instance;
            var casingAudio = director != null ? director.Casings : null;
            var tracks = new List<KovanIzi>();
            var mechSerials = new HashSet<int>();
            var rows = new List<string>();
            var wrong = new List<string>();
            var bos = new List<string>();
            string current = "";
            var assigned = new HashSet<CasingEffect>();
            System.Action<CasingLaunchInfo> onLaunch = info =>
            {
                // the casing that just started at the port — not one already followed (sg_2 throws two from the same port in one frame)
                CasingEffect c = null;
                float best = 0.001f;
                foreach (CasingEffect e in Object.FindObjectsOfType<CasingEffect>())
                {
                    if (!e.gameObject.activeInHierarchy || e.IsResting || assigned.Contains(e)) continue;
                    float d = (e.transform.position - info.Position).magnitude;
                    if (d < best) { best = d; c = e; }
                }

                if (c != null) assigned.Add(c);
                CasingEffect.PredictLanding(info.Position, info.Velocity, out Vector3 touch, out _, out Vector3 rest, out _);
                tracks.Add(new KovanIzi { Serial = info.ShotSerial, Frame = Time.frameCount, Row = current, Effect = c, PredTouch = touch, PredRest = rest });
            };
            System.Action<WeaponMechanismEvent> onMech = e => { if (e.Kind == WeaponMechanismKind.CasingEjected) mechSerials.Add(e.ShotSerial); };
            CasingEvents.Launched += onLaunch;
            WeaponEvents.Mechanism += onMech;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                foreach (CameraMode mode in modes)
                {
                    CameraModeService.Request(mode, instant: true);
                    yield return SpecialWeaponSahne.Kareler(30);
                    foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                    {
                        if (d.Id == ClassicWeaponId.Rl1) continue;   // no casing
                        _s.Controller.EquipClassic(d.Id, _s.Config);
                        yield return SpecialWeaponSahne.Kareler(60);
                        current = mode + " " + d.RosterKey;
                        int start = tracks.Count;
                        for (int f = 0; f < 1500 && tracks.Count - start < 10; f++)
                        {
                            if (_s.Controller.Classic.IsReloading) { yield return null; Izle(tracks, floorField, casingAudio, assigned); continue; }
                            if (_s.Controller.Classic.Ammo <= 0) { _s.Controller.Classic.TryBeginReload(); yield return null; Izle(tracks, floorField, casingAudio, assigned); continue; }
                            _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                            yield return null;
                            Izle(tracks, floorField, casingAudio, assigned);
                            _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                            if (d.Id == ClassicWeaponId.Sg2) _s.Controller.Classic.TryBeginReload();   // its shells leave on the break-open
                            for (int w = 0; w < 14; w++) { yield return null; Izle(tracks, floorField, casingAudio, assigned); }
                        }

                        for (int w = 0; w < 300 && !TumuDurdu(tracks, start); w++) { yield return null; Izle(tracks, floorField, casingAudio, assigned); }
                        if (tracks.Count - start < 10) bos.Add(current + ": yalnız " + (tracks.Count - start) + " kovan");
                    }
                }
            }
            finally
            {
                CasingEvents.Launched -= onLaunch;
                WeaponEvents.Mechanism -= onMech;
                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                Time.captureDeltaTime = 0f;
            }

            // per row: the prediction against the visible casing; the sound's landing (report)
            var byRow = new Dictionary<string, List<KovanIzi>>();
            foreach (KovanIzi k in tracks) { if (!byRow.TryGetValue(k.Row, out var l)) byRow[k.Row] = l = new List<KovanIzi>(); l.Add(k); }
            foreach (var kv in byRow)
            {
                float touchMax = 0f, restMax = 0f, audioTouchMax = -1f, audioRestMax = -1f;
                int audioN = 0, serialMiss = 0;
                foreach (KovanIzi k in kv.Value)
                {
                    if (k.Effect == null || !k.Rested) { bos.Add(kv.Key + ": kovan izlenemedi (seri " + k.Serial + ")"); continue; }
                    if (!mechSerials.Contains(k.Serial)) serialMiss++;
                    touchMax = Mathf.Max(touchMax, Xz(k.PredTouch, k.Touch));
                    restMax = Mathf.Max(restMax, Xz(k.PredRest, k.Rest));
                    if (k.HasAudio) { audioN++; audioTouchMax = Mathf.Max(audioTouchMax, Xz(k.Audio, k.Touch)); audioRestMax = Mathf.Max(audioRestMax, Xz(k.Audio, k.Rest)); }
                }

                rows.Add(kv.Key + " | casings " + kv.Value.Count + " | predicted ↔ visible touchdown max " + Cm(touchMax) + " cm, rest " + Cm(restMax) + " cm | serial ↔ mechanism misses " + serialMiss
                         + " | sound ↔ visible (n " + audioN + ") touchdown max " + Cm(audioTouchMax) + " cm, rest " + Cm(audioRestMax) + " cm");
                if (touchMax > 0.1f || restMax > 0.1f) wrong.Add(kv.Key + ": tahmin görünen kovandan uzak (iniş " + Cm(touchMax) + ", duruş " + Cm(restMax) + " cm)");
                if (serialMiss > 0) wrong.Add(kv.Key + ": " + serialMiss + " kovanın atış serisi mekanizma olayında yok");
                // WP-085 (C-00 05.10): the casing sound lands on the visible casing's touchdown (≤ 25 cm); its bounce is heard at the rest
                if (audioN > 0 && audioTouchMax > 0.25f) wrong.Add(kv.Key + ": kovan sesi görünen inişten uzak (" + Cm(audioTouchMax) + " cm > 25)");
            }

            Debug.Log("[DD-TEST] wp087.casinglaunch\n" + string.Join("\n", rows));
            Assert.IsEmpty(bos, "kaynak kayıtta yok\n" + string.Join("\n", bos) + "\n" + string.Join("\n", rows));
            Assert.AreEqual(modes.Length * 9, byRow.Count, modes.Length + " kamera × 9 kovanlı silah");
            Assert.IsEmpty(wrong, string.Join("\n", wrong) + "\n" + string.Join("\n", rows));
        }

        private static float Xz(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static bool TumuDurdu(List<KovanIzi> tracks, int start)
        {
            for (int i = start; i < tracks.Count; i++) if (tracks[i].Effect != null && !tracks[i].Rested) return false;
            return true;
        }

        /// <summary>One frame of every tracked casing: its first touchdown (it clamps onto its floor), its rest; the sound's landing of a
        /// frame that threw exactly one casing (CasingAudio.LastLandingPoint, read after the frame's handlers ran).</summary>
        private static void Izle(List<KovanIzi> tracks, FieldInfo floorField, DD.Prototype.Audio.CasingAudio audio, HashSet<CasingEffect> assigned)
        {
            int frame = Time.frameCount, thrown = 0, last = -1;
            for (int i = 0; i < tracks.Count; i++)
            {
                KovanIzi k = tracks[i];
                if (k.Frame == frame - 1) { thrown++; last = i; }
                if (k.Effect == null || k.Rested) continue;
                Vector3 p = k.Effect.transform.position;
                float floor = floorField != null ? (float)floorField.GetValue(k.Effect) : float.NegativeInfinity;
                if (!k.Touched && p.y <= floor + 0.0065f) { k.Touched = true; k.Touch = p; }
                if (k.Effect.IsResting) { k.Rested = true; k.Rest = p; if (!k.Touched) { k.Touched = true; k.Touch = p; } assigned.Remove(k.Effect); }
            }

            if (thrown == 1 && audio != null) { tracks[last].Audio = audio.LastLandingPoint; tracks[last].HasAudio = true; }
        }
    }
}
