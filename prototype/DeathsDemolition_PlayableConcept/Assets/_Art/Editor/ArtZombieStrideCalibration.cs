using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-096 (S-03 08.10, C-00 task 1) · the AP-033 zombie gaits' no-slide speed MEASURED ON THE SHIPPED ZOMBIE RIG, written into the
    /// set's <c>unitySpeedMps</c> (the speed the runtime's honest rate divides by).
    ///
    /// Why: the p1 window showed every AP-033 gait striding shorter in Unity than P-06's Blender contact speed (ZL2 GaitVariety-0
    /// control, stance foot backward speed u / travel v: Standard lurch_A 0.874, Runner feral_A 0.847; the IK-off Runner slip test
    /// 5.8 cm / step ≈ feral × 0.93) while the AP-022 gaits sat at 0.93–0.98 — the Humanoid retarget of the 73-bone AP-033 export does
    /// not reproduce the authored stride the way AP-022's did, so the Blender spd cannot be the runtime speed for AP-033.
    ///
    /// How (no Play mode, the -executeMethod step): the Zombie_AP007 prefab rig with the freshly built DD_Zombie.controller is
    /// stepped with <see cref="Animator.Update"/> at <see cref="SampleHz"/> — the real runtime evaluation (Gait blend tree child,
    /// Humanoid retarget onto zombie_body_A's avatar, the Locomotion state's built-in foot IK), unlike StrideCalibrator's
    /// SampleAnimation (no state foot IK; its stance windows trust the event labels). Script IK (FootIkAdapter, LookAt) does not run
    /// in Edit mode: this is the clip's own foot motion — the definition of a clip speed. Per foot the contact point is the lower of
    /// the ankle and toe markers relative to each marker's own lowest point (P-06's lowest heel / MTP / toe marker, Unity rig);
    /// contact = within <see cref="ContactHeight"/> and vertical speed ≤ <see cref="ContactVerticalSpeed"/> (P-06 B4 / ZL L1 mask);
    /// the speed = the mean backward speed of the contact point over contact (forward-moving contact samples — a drag — left out
    /// and reported), the two feet averaged. The event labels are checked against the geometric stance and reported.
    /// Runner feral_A uses the fixed ankle over its original plant → lift event window, matching its authored fixed-ankle
    /// correction and the IK-off Runner acceptance metric. Both neighbours of each central difference must lie in that window;
    /// a changing ankle/toe marker or a height mask must not calibrate a different material point for this clip.
    ///
    /// AP-033 rows take the measurement (guard: 0.70–1.15 × spd, else spd + WARNING). AP-022 rows keep their contact spd (live-verified:
    /// zombie_run's PlayMode zero crossing 5.35 vs spd 5.30, WP-071 r24b) — their measurement is logged as the method check.
    /// Each AP-033 row also gets an automatic rate band when its honest rate at the tier speed / scale (± the speed and scale jitter)
    /// leaves <see cref="CadenceBand"/>: maxRate / minRate rounded out to 0.05 (cap <see cref="MaxAutoRate"/>), 0 = the default band.
    /// </summary>
    public static class ArtZombieStrideCalibration
    {
        public const string LogPrefix = "[DD-ART] ";
        public const string MeasuredPrefix = "AP033_";
        // WP-096 calibration: Animator steps per second, cycles skipped / measured.
        public const float SampleHz = 120f;
        public const int WarmCycles = 1, MeasureCycles = 3;
        // WP-096 calibration contact mask (P-06 B4 / ZL L1): marker within this height of its own lowest point, m; vertical speed at most this, m/s.
        public const float ContactHeight = 0.03f, ContactVerticalSpeed = 0.15f;
        // WP-096 calibration: a measured speed outside this share of the Blender spd is not trusted (spd kept, WARNING).
        public const float TrustMin = 0.70f, TrustMax = 1.15f;
        // WP-096 calibration: the highest automatic per-clip maxRate (above it the clip slides; FootLockMaxRate 1.25 already opens the lock).
        public const float MaxAutoRate = 1.40f;
        public const int MinContactSamples = 6;

        public struct Result
        {
            public bool ok;
            public float speedMps, speedL, speedR, dragShare, labelL, labelR;
            public int samplesL, samplesR;
            public bool axisFlipped;
            public string note;
        }

        /// <summary>
        /// Measure every zombie pool / fallback gait on the prefab rig, write the AP-033 speeds (+ automatic bands) into the zombie set,
        /// log one line per clip. Returns the number of AP-033 rows that took a measurement; <paramref name="summary"/> for the intake line.
        /// </summary>
        public static int Apply(CharacterAnimationSet set, AnimatorController controller, Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest, out string summary)
        {
            summary = "calibration=skipped";
            if (set == null || controller == null || manifest == null) return 0;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.ZombiePrefab);
            if (prefab == null) { Debug.LogWarning(LogPrefix + "stride calibration skipped: " + ArtCharacterPrefabBuilder.ZombiePrefab + " missing"); return 0; }

            var gaits = new List<int>();
            foreach (int[][] table in new[] { C.ZombieGaitPools, C.ZombieGaitFallbacks })
            foreach (int[] pool in table)
            foreach (int g in pool)
                if (!C.IsGaitMirrored(g) && !gaits.Contains(g)) gaits.Add(g);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            int measured = 0, fallback = 0;
            var checks = new List<float>();
            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogWarning(LogPrefix + "stride calibration skipped: the zombie prefab has no humanoid Animator");
                    return 0;
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (int g in gaits)
                {
                    string clip = C.ZombieGaitClips[g];
                    if (!manifest.TryGetValue(clip, out ArtAnimationClipContract.ClipSpec spec)) continue;
                    CharacterAnimationSet.Entry entry = set.Find(clip);
                    if (entry == null) continue;
                    Result r = Measure(animator, g, spec);
                    float spd = spec.speedMps;
                    float ratio = spd > 0.01f ? r.speedMps / spd : 0f;
                    string tail = " (" + r.note + "; L " + F(r.speedL) + "/" + r.samplesL + " R " + F(r.speedR) + "/" + r.samplesR + " drag " + F(r.dragShare, 2)
                                  + " labels L " + F(r.labelL, 2) + " R " + F(r.labelR, 2) + (r.axisFlipped ? " AXIS-FLIPPED" : "") + ")";
                    if (!clip.StartsWith(MeasuredPrefix, StringComparison.Ordinal))
                    {
                        if (r.ok) checks.Add(ratio);
                        Debug.Log(LogPrefix + "stride " + clip + " spd " + F(spd) + " kept (AP-022 contact spd, live-verified) — measured " + F(r.speedMps) + " ×" + F(ratio) + " method check" + tail);
                        continue;
                    }

                    bool trusted = r.ok && ratio >= TrustMin && ratio <= TrustMax;
                    float unity = trusted ? r.speedMps : spd;
                    entry.unitySpeedMps = unity;
                    if (trusted) measured++; else fallback++;
                    string band = AutoBand(entry, spec, unity);
                    Debug.Log(LogPrefix + "stride " + clip + " spd " + F(spd) + " → unity " + F(unity) + " ×" + F(spd > 0.01f ? unity / spd : 0f)
                              + (trusted ? " measured" : " FALLBACK spd (measured " + F(r.speedMps) + " ×" + F(ratio) + " outside " + F(TrustMin, 2) + "–" + F(TrustMax, 2) + ")")
                              + " " + band + tail);
                    if (!trusted) Debug.LogWarning(LogPrefix + "stride " + clip + ": measurement not trusted, the Blender spd stays the runtime speed");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            checks.Sort();
            float median = checks.Count > 0 ? checks[checks.Count / 2] : float.NaN;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            summary = "calibrated=" + measured + " fallback=" + fallback + " ap022check=×" + F(median) + " (" + checks.Count + ")";
            Debug.Log(LogPrefix + "stride calibration " + summary + " — the AP-022 check near ×1.00 says the measurement reads a clip the way the live game does");
            return measured;
        }

        /// <summary>One gait on the rig: Locomotion state, Speed01 1, GaitRate 1, the gait child; <see cref="MeasureCycles"/> cycles at <see cref="SampleHz"/>.</summary>
        public static Result Measure(Animator animator, int gait, ArtAnimationClipContract.ClipSpec spec)
        {
            var r = new Result { note = "-" };
            Transform root = animator.transform;
            Transform[] ankle = { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            Transform[] toe = { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            if (ankle[0] == null || ankle[1] == null) { r.note = "no foot bones"; return r; }
            int cycleFrames = Math.Max(1, spec.frames - 1);
            float cycle = cycleFrames / (float)ArtAnimationClipContract.Fps;
            float dt = 1f / SampleHz;
            int warm = Mathf.CeilToInt(WarmCycles * cycle * SampleHz), n = Mathf.CeilToInt(MeasureCycles * cycle * SampleHz);

            animator.Rebind();
            animator.SetBool(C.Alive, true);
            animator.SetBool(C.Waiting, false);
            animator.SetFloat(C.Speed01, 1f);
            animator.SetFloat(C.GaitRate, 1f);
            animator.SetFloat(C.IdleVariant, 0f);
            animator.SetFloat(C.Gait, gait);
            animator.Play(C.LocomotionState, 0, 0f);
            animator.Update(0f);
            for (int i = 0; i < warm; i++) animator.Update(dt);

            // positions in the rig's own space (the animated body faces +Z: the clip's 180° root offset, WP-032)
            var a = new Vector3[2, n];
            var t = new Vector3[2, n];
            var phase = new float[n];
            for (int i = 0; i < n; i++)
            {
                animator.Update(dt);
                phase[i] = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
                for (int f = 0; f < 2; f++)
                {
                    a[f, i] = root.InverseTransformPoint(ankle[f].position);
                    t[f, i] = toe[f] != null ? root.InverseTransformPoint(toe[f].position) : a[f, i];
                }
            }

            int plantL = ZombieSkinnedVisual.FrameOf(spec.EventsText(), "foot_l"), plantR = ZombieSkinnedVisual.FrameOf(spec.EventsText(), "foot_r");
            int offL = ZombieSkinnedVisual.FrameOf(spec.EventsText(), "foot_l_off"), offR = ZombieSkinnedVisual.FrameOf(spec.EventsText(), "foot_r_off");
            var backs = new List<float>[] { new List<float>(), new List<float>() };
            var label = new float[2];
            double backSum = 0d;
            bool fixedAnkle = spec.name == C.ZombieRunFeralAV4;
            for (int f = 0; f < 2; f++)
            {
                float minA = float.MaxValue, minT = float.MaxValue;
                for (int i = 0; i < n; i++) { minA = Mathf.Min(minA, a[f, i].y); minT = Mathf.Min(minT, t[f, i].y); }
                int inLabel = 0;
                int plant = f == 0 ? plantL : plantR, off = f == 0 ? offL : offR;
                for (int i = 1; i < n - 1; i++)
                {
                    Vector3 prev, next;
                    if (fixedAnkle)
                    {
                        if (plant < 0 || off < 0
                            || !InWindow(phase[i - 1] * cycleFrames, plant, off, cycleFrames)
                            || !InWindow(phase[i] * cycleFrames, plant, off, cycleFrames)
                            || !InWindow(phase[i + 1] * cycleFrames, plant, off, cycleFrames)) continue;
                        prev = a[f, i - 1];
                        next = a[f, i + 1];
                    }
                    else
                    {
                        float hA = a[f, i].y - minA, hT = t[f, i].y - minT;
                        bool useToe = hT < hA;
                        float h = useToe ? hT : hA;
                        prev = useToe ? t[f, i - 1] : a[f, i - 1];
                        next = useToe ? t[f, i + 1] : a[f, i + 1];
                        float vy = (next.y - prev.y) / (2f * dt);
                        if (h > ContactHeight || Mathf.Abs(vy) > ContactVerticalSpeed) continue;
                    }
                    float back = -(next.z - prev.z) / (2f * dt);
                    backs[f].Add(back);
                    backSum += back;
                    if (plant >= 0 && off >= 0 && InWindow(phase[i] * cycleFrames, plant, off, cycleFrames)) inLabel++;
                }

                label[f] = backs[f].Count > 0 && plant >= 0 && off >= 0 ? inLabel / (float)backs[f].Count : float.NaN;
            }

            // the animated body faces +Z in the rig space (the clip's 180° root offset); should it face −Z, every contact moves +Z — the
            // sign follows the contact's net motion, and the flip is reported
            float sign = backSum >= 0d ? 1f : -1f;
            r.axisFlipped = sign < 0f;
            var speed = new float[2];
            var count = new int[2];
            int contactAll = 0, drag = 0;
            for (int f = 0; f < 2; f++)
            {
                double sum = 0d;
                foreach (float b0 in backs[f])
                {
                    float back = sign * b0;
                    contactAll++;
                    if (back < 0f) { drag++; continue; }   // a contact marker moving forward: a drag / settle, not the support
                    sum += back;
                    count[f]++;
                }

                speed[f] = count[f] > 0 ? (float)(sum / count[f]) : 0f;
            }

            r.speedL = speed[0]; r.speedR = speed[1];
            r.samplesL = count[0]; r.samplesR = count[1];
            r.labelL = label[0]; r.labelR = label[1];
            r.dragShare = contactAll > 0 ? drag / (float)contactAll : 0f;
            r.ok = count[0] >= MinContactSamples && count[1] >= MinContactSamples;
            r.speedMps = r.ok ? 0.5f * (speed[0] + speed[1]) : 0f;
            r.note = r.ok ? "Animator.Update " + SampleHz.ToString("F0", CultureInfo.InvariantCulture) + " Hz × " + MeasureCycles + " cycles" : "too few contact samples";
            if (fixedAnkle) r.note += "; fixed ankle, original event window, no height mask";
            return r;
        }

        /// <summary>Phase (frames) inside the cyclic stance window plant → off.</summary>
        private static bool InWindow(float frame, int plant, int off, int cycle)
        {
            float span = (off - plant + cycle) % cycle;
            float d = (frame - plant + cycle) % cycle;
            return d <= span;
        }

        /// <summary>
        /// The clip's honest-rate range at its tier (world speed × 1 ± SpeedSpread over unity × tier scale × the scale jitter); outside
        /// <see cref="CadenceBand"/> the entry gets its own maxRate / minRate (rounded out to 0.05), inside both reset to 0 (default).
        /// </summary>
        private static string AutoBand(CharacterAnimationSet.Entry entry, ArtAnimationClipContract.ClipSpec spec, float unity)
        {
            if (!TryTier(spec.tier, out EnemyTier tier) || unity <= 0.01f) { entry.minRate = 0f; entry.maxRate = 0f; return "band default (no tier)"; }
            float v = TierSpeed(tier);
            float s = TierScale(tier);
            float jMin = AppearanceConst("ScaleJitterMin", 0.94f), jMax = AppearanceConst("ScaleJitterMax", 1.06f);
            float spread = ZombieLocomotionMath.SpeedSpread;
            float nominal = v / (unity * s);
            float lo = v * (1f - spread) / (unity * s * jMax), hi = v * (1f + spread) / (unity * s * jMin);
            entry.maxRate = hi > CadenceBand.Max ? Mathf.Min(MaxAutoRate, Mathf.Ceil(hi * 20f - 1e-4f) / 20f) : 0f;
            entry.minRate = lo < CadenceBand.Min ? Mathf.Max(0.5f, Mathf.Floor(lo * 20f + 1e-4f) / 20f) : 0f;
            return "rate@" + tier + " " + F(nominal) + " [" + F(lo) + "–" + F(hi) + "] band " + (entry.minRate > 0f ? F(entry.minRate, 2) : "def") + "–" + (entry.maxRate > 0f ? F(entry.maxRate, 2) : "def")
                   + (hi > MaxAutoRate ? " SLIDES above " + F(MaxAutoRate, 2) : "");
        }

        private static bool TryTier(string tier, out EnemyTier t)
        {
            switch ((tier ?? "").ToLowerInvariant())
            {
                case "standard": t = EnemyTier.Standard; return true;
                case "runner": case "special": t = EnemyTier.Special; return true;
                case "guard": case "elite": t = EnemyTier.Elite; return true;
                case "executioner": case "ultra": t = EnemyTier.Ultra; return true;
                default: t = EnemyTier.Standard; return false;
            }
        }

        public static float TierSpeed(EnemyTier tier)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            EnemyConfigSection enemies = config != null && config.Enemies != null ? config.Enemies : new EnemyConfigSection();
            return enemies.Find(tier).MoveSpeed;
        }

        /// <summary>The tier body scale (ZombieAppearance's constants, read so there is one source).</summary>
        public static float TierScale(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Special: return AppearanceConst("RunnerScale", 0.96f);
                case EnemyTier.Elite: return AppearanceConst("GuardScale", 1.16f);
                case EnemyTier.Ultra: return AppearanceConst("ExecutionerScale", 1.48f);
                default: return AppearanceConst("StandardScale", 1f);
            }
        }

        private static float AppearanceConst(string name, float fallback)
        {
            FieldInfo f = typeof(DD.Prototype.Enemies.ZombieAppearance).GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null && f.IsLiteral && f.FieldType == typeof(float) ? (float)f.GetRawConstantValue() : fallback;
        }

        private static string F(float v, int digits = 3) => float.IsNaN(v) ? "-" : v.ToString("F" + digits, CultureInfo.InvariantCulture);
    }
}
