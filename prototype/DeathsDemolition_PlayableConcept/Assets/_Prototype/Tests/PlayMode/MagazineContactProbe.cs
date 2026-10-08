using System.Collections.Generic;
using DD.Prototype.Weapons;
using DD.Prototype.Characters;
using UnityEngine;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R2 H3 contactDeviationFramesMax. Per C-00 05.10, S-06 (WP-083) measures it and S-02 writes the H3 artifact.
    /// <para>
    /// The magazine leaves the well on the Detach beat and goes back on the Insert beat, and the left hand's <c>socket_mag_l</c>
    /// should be AT the well on those frames. Every game frame of a reload, the probe records the distance from the socket to the
    /// magazine's seat (the part's authored place in the weapon, read once at <see cref="Begin"/>). Contact means the socket is
    /// within <see cref="ContactMetres"/> of the seat.
    /// </para>
    /// <para>
    /// For each Detach / Insert beat the deviation is |nearest contact frame − beat frame| within ±<see cref="Window"/> frames, or
    /// <see cref="NoContact"/> when the hand never reaches the seat in that window. The max over a run is contactDeviationFramesMax.
    /// </para>
    /// <para>
    /// Use: Begin at the reload start (the part in the weapon), Tick once per game frame while reloading (after the frame, e.g.
    /// right after a yield return null), End after. Then read <see cref="MaxDeviationFrames"/> and <see cref="Report"/>. Tests only.
    /// </para>
    /// </summary>
    public sealed class MagazineContactProbe
    {
        /// <summary>Frames searched either side of a beat for a contact frame.</summary>
        public const int Window = 15;

        /// <summary>The hand "at the well": socket_mag_l (palm centre) within this of the magazine seat, m.</summary>
        public const float ContactMetres = 0.03f;

        /// <summary>The deviation of a beat whose window holds no contact frame (Window + 1: always over the ≤ 2 gate).</summary>
        public const int NoContact = Window + 1;

        private readonly List<float> _distance = new List<float>();
        private readonly List<(int frame, MagazineStep step)> _beats = new List<(int, MagazineStep)>();
        private readonly List<string> _rows = new List<string>();
        private WeaponMagazineOwnership _owner;
        private Transform _socket, _seatParent;
        private Matrix4x4 _seatLocal, _partToSocket;
        private string _label;
        private int _maxDeviation, _beatsMeasured;
        private float _maxClosestCm;

        /// <summary>Largest deviation over every scored beat (frames; −1 before any beat).</summary>
        public int MaxDeviationFrames => _beatsMeasured > 0 ? _maxDeviation : -1;
        /// <summary>The farthest the hand still was from the seat at its closest approach in a beat window (cm; report).</summary>
        public float MaxClosestCm => _maxClosestCm;
        public int BeatsMeasured => _beatsMeasured;
        public string Report => string.Join(" | ", _rows);

        /// <summary>Start one reload: the magazine part (still in the weapon), the hand's socket_mag_l and the ownership machine.</summary>
        public void Begin(string label, Transform part, Transform handSocket, WeaponMagazineOwnership owner)
        {
            End();
            _label = label;
            _distance.Clear();
            _beats.Clear();
            if (part == null || handSocket == null || owner == null) return;
            _socket = handSocket;
            _seatParent = part.parent;
            _seatLocal = Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale);
            ReloadPartSocket row = handSocket.GetComponent<ReloadPartSocket>();
            _partToSocket = row != null && ReloadPartCalibration.TryGet(row.Key, out Matrix4x4 m) ? m.inverse : Matrix4x4.identity;
            _owner = owner;
            _owner.Stepped += OnStep;
        }

        /// <summary>One game frame of the reload.</summary>
        public void Tick()
        {
            if (_owner == null || _seatParent == null || _socket == null) return;
            Vector3 target = (_seatParent.localToWorldMatrix * _seatLocal * _partToSocket).GetColumn(3);
            _distance.Add(Vector3.Distance(_socket.position, target));
        }

        // a beat lands during a frame whose pose the NEXT Tick samples: its index is the current sample count
        private void OnStep(MagazineStep step, bool fromEvent)
        {
            if (step == MagazineStep.Detach || step == MagazineStep.Insert) _beats.Add((_distance.Count, step));
        }

        /// <summary>End the reload and score its Detach / Insert beats.</summary>
        public void End()
        {
            if (_owner == null) return;
            _owner.Stepped -= OnStep;
            _owner = null;
            foreach ((int beat, MagazineStep step) in _beats)
            {
                int from = Mathf.Max(0, beat - Window), to = Mathf.Min(_distance.Count - 1, beat + Window);
                if (to < from) continue;
                int best = from, contact = -1;
                for (int i = from; i <= to; i++)
                {
                    if (_distance[i] < _distance[best]) best = i;
                    if (_distance[i] <= ContactMetres && (contact < 0 || Mathf.Abs(i - beat) < Mathf.Abs(contact - beat))) contact = i;
                }

                int deviation = contact >= 0 ? Mathf.Abs(contact - beat) : NoContact;
                _maxDeviation = Mathf.Max(_maxDeviation, deviation);
                _maxClosestCm = Mathf.Max(_maxClosestCm, _distance[best] * 100f);
                _beatsMeasured++;
                float atBeat = beat < _distance.Count ? _distance[beat] * 100f : float.NaN;
                _rows.Add(_label + " " + step + " beat f" + beat + (contact >= 0 ? " contact f" + contact : " NO contact") + " (Δ" + deviation + "; closest f" + best + " " +
                          (_distance[best] * 100f).ToString("F1") + " cm; at beat " + atBeat.ToString("F1") + " cm)");
            }

            _beats.Clear();
        }
    }
}
