using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Kagura Bell's pressure fronts, live.
    ///
    /// THE VERB IS SWEEP
    /// Every classic weapon in this prototype asks "where is the cursor". This one does
    /// not read the aim direction at all: the ring leaves the player in every direction
    /// at once. It is the answer to the one thing the classic weapons cannot answer -
    /// being surrounded by forty bodies - and it is why it is worth taking over an
    /// Assault Rifle even though its single-target damage per second is a quarter of the
    /// old rotary cannon's.
    ///
    /// WHY THE FRONT TRAVELS INSTEAD OF RESOLVING INSTANTLY
    /// An instant annulus with an animated ring drawn over it is a lie, and the player
    /// finds out: the bodies at the rim fly before the ring reaches them. Here the front
    /// really does move - each frame it sweeps the band between where it was and where it
    /// is - so the visible ring and the moment of impact are the same event by
    /// construction rather than by tuning.
    ///
    /// ONE ENEMY IS CROSSED ONCE PER FRONT. A body walking inward would otherwise be
    /// swept twice by the same ring, which would double its damage for the crime of
    /// moving toward the player.
    /// </summary>
    public sealed class KaguraWaveField
    {
        /// <summary>
        /// PROTOTYPE-ONLY: fronts that may be in flight at once. At 2.4 rings/s and a
        /// 0.34 s travel the honest maximum is two; three leaves headroom for an F1 edit
        /// to the fire rate without silently dropping rings.
        /// </summary>
        public const int MaxWaves = 3;

        private sealed class Wave
        {
            public bool Active;
            public Vector3 Origin;
            public float Elapsed;
            public float Radius;
            public float TravelSeconds;
            public float Damage;
            public float FalloffAtRim;
            public float ImpactForce;
            public readonly List<int> Crossed = new List<int>(24);
        }

        private readonly Wave[] _waves = new Wave[MaxWaves];
        private readonly List<DamageTarget> _scratch = new List<DamageTarget>(32);

        /// <summary>Fronts currently in flight. F1 readout and test hook.</summary>
        public int ActiveWaves { get; private set; }

        /// <summary>Enemies crossed by a front since the last <see cref="Clear"/>. Test hook.</summary>
        public int TotalCrossings { get; private set; }

        public KaguraWaveField()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                _waves[i] = new Wave();
            }
        }

        /// <summary>Ring the bell. Returns false when every slot is already in flight.</summary>
        public bool Emit(Vector3 origin, KaguraBellSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            Wave wave = FreeSlot();
            if (wave == null)
            {
                return false;
            }

            wave.Active = true;
            wave.Origin = origin;
            wave.Elapsed = 0f;
            wave.Radius = Mathf.Max(1f, settings.WaveRadius);
            wave.TravelSeconds = Mathf.Max(0.05f, settings.WaveTravelSeconds);
            wave.Damage = Mathf.Max(0f, settings.Common.Damage);
            wave.FalloffAtRim = Mathf.Clamp01(settings.FalloffAtRim);
            wave.ImpactForce = Mathf.Max(0f, settings.Common.ImpactForce);
            wave.Crossed.Clear();

            // No sound here: the controller already raises the weapon's fire cue for every
            // primary event. Two cues for one trigger pull is how a heavy weapon starts
            // sounding like a rattle.
            HitFeedbackDirector.Instance.ReportShockRing(origin, wave.Radius, wave.TravelSeconds);

            // WP-056: the usage bus (S-03 stumble at the front, S-07 sw_kagura_field sheet, S-08).
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.KaguraBell, SpecialUsageKind.FieldSpawned,
                SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FieldVfxFamily, origin, Vector3.up, wave.Radius, wave.TravelSeconds, 0);

            RecountActive();
            return true;
        }

        /// <summary>Advance every front. Call once per frame while the bell is in hand.</summary>
        public void Tick(float deltaTime)
        {
            if (ActiveWaves <= 0)
            {
                return;
            }

            for (int i = 0; i < MaxWaves; i++)
            {
                Wave wave = _waves[i];
                if (!wave.Active)
                {
                    continue;
                }

                float previous = FrontRadius(wave, wave.Elapsed);
                wave.Elapsed += deltaTime;
                float current = FrontRadius(wave, wave.Elapsed);

                Sweep(wave, previous, current);

                if (wave.Elapsed >= wave.TravelSeconds)
                {
                    wave.Active = false;
                    SpecialWeaponUsageEvents.Publish(SpecialWeaponId.KaguraBell, SpecialUsageKind.FieldEnded,
                        SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FieldVfxFamily, wave.Origin, Vector3.up, wave.Radius, wave.TravelSeconds, wave.Crossed.Count);
                    wave.Crossed.Clear();
                }
            }

            RecountActive();
        }

        /// <summary>Drop every front. Called when the activation ends.</summary>
        public void Clear()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                _waves[i].Active = false;
                _waves[i].Crossed.Clear();
            }

            ActiveWaves = 0;
            TotalCrossings = 0;
        }

        /// <summary>
        /// Front position over time. Ease-out, matching <see cref="ShockRingEffect"/>'s
        /// own curve exactly - the two are the same wave and must not diverge.
        /// </summary>
        public static float FrontRadius(float radius, float travelSeconds, float elapsed)
        {
            float t = travelSeconds > 0f ? Mathf.Clamp01(elapsed / travelSeconds) : 1f;
            float eased = 1f - (1f - t) * (1f - t);
            return radius * eased;
        }

        private static float FrontRadius(Wave wave, float elapsed)
        {
            return FrontRadius(wave.Radius, wave.TravelSeconds, elapsed);
        }

        private void Sweep(Wave wave, float innerRadius, float outerRadius)
        {
            if (outerRadius <= innerRadius)
            {
                return;
            }

            int count = HitscanEngine.OverlapEnemies(wave.Origin, outerRadius, _scratch);
            if (count == 0)
            {
                return;
            }

            // WP-007 (DT-1/3): this frame's band of this wave is ONE attack - every body it
            // crosses shares one transaction id. A body is crossed at most once per wave
            // (Crossed), so the receiver's same-transaction guard never refuses a legitimate hit.
            using (HitscanEngine.BeginExternalAttack())
            {
                for (int i = 0; i < count; i++)
                {
                    DamageTarget target = _scratch[i];
                    if (!target.IsValid)
                    {
                        continue;
                    }

                    if (wave.Crossed.Contains(target.Id))
                    {
                        continue;
                    }

                    Vector3 centre = HitscanEngine.CentreOf(target);

                    Vector3 flat = centre - wave.Origin;
                    flat.y = 0f;
                    float distance = flat.magnitude;

                    // Only the band the front swept this frame. A body already inside the
                    // previous radius was crossed on an earlier frame or was never crossed at
                    // all because it spawned inside the ring - either way, not this frame's.
                    if (distance < innerRadius)
                    {
                        continue;
                    }

                    wave.Crossed.Add(target.Id);
                    TotalCrossings++;

                    float reach = Mathf.Max(0.001f, wave.Radius);
                    float falloff = Mathf.Lerp(1f, wave.FalloffAtRim, Mathf.Clamp01(distance / reach));

                    Vector3 direction = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;

                    HitscanEngine.ApplyExternalHit(
                        target,
                        wave.Damage * falloff,
                        centre,
                        direction,
                        wave.ImpactForce,
                        DamageSourceKind.Explosion,
                        WeaponPower.Heavy);
                    // WP-056 · targeted Hit for S-03 (stumble at the front): force on the DamageForce scale, falloff at this distance.
                    SpecialWeaponUsageEvents.PublishHit(SpecialWeaponId.KaguraBell, SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FieldVfxFamily,
                        target, centre, direction, wave.ImpactForce, falloff, wave.TravelSeconds - wave.Elapsed);
                }
            }
        }

        private Wave FreeSlot()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                if (!_waves[i].Active)
                {
                    return _waves[i];
                }
            }

            return null;
        }

        private void RecountActive()
        {
            int active = 0;
            for (int i = 0; i < MaxWaves; i++)
            {
                if (_waves[i].Active)
                {
                    active++;
                }
            }

            ActiveWaves = active;
        }
    }
}
