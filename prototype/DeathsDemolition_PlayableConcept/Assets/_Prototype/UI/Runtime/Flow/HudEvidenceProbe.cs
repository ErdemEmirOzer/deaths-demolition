// WP-094: opt-in evidence only. No object or sampling in shipped builds.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using DD.Prototype.Audio;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Observes production HUD state at end of frame; stamps use the same realtime/frame clock as TdCaptureAgent.</summary>
    internal sealed class HudEvidenceProbe : MonoBehaviour
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private RunHudScreen _hud;
        private StreamWriter _samples, _events;
        private string _folder;
        private RectTransform _coin, _heart;
        private TMP_Text _health, _ammo, _caption, _delta;
        private UiBar _healthBar;
        private UiFx _leaf;
        private bool _ended;

        internal void Begin(RunHudScreen hud, string folder)
        {
            _hud = hud;
            _folder = folder;
            Directory.CreateDirectory(folder);
            _coin = Field<RectTransform>("_goldCoin");
            Image heart = Field<Image>("_healthIcon");
            _heart = heart != null ? heart.rectTransform : null;
            _health = Field<TMP_Text>("_healthValue");
            _ammo = Field<TMP_Text>("_ammoValue");
            _caption = Field<TMP_Text>("_reloadCaption");
            _delta = Field<TMP_Text>("_goldDelta");
            _healthBar = Field<UiBar>("_healthBar");
            _leaf = Field<UiFx>("_goldLeaf");
            _events = new StreamWriter(Path.Combine(folder, "events.tsv"));
            _events.WriteLine("realtime\tunscaled\tframe\tdsp\tid\tdetail");
            _samples = new StreamWriter(Path.Combine(folder, "samples.tsv"));
            _samples.WriteLine("realtime\tunscaled\tframe\tdsp\tcoinScale\tdeltaAlpha\tleafStart\tchunks\tghost\thealAlpha\tbeats\theartScale\tedge0\tedge1\tedge2\tedge3\twedges\taccents\tcasings\tammo\tpreview\tlandings\tlowAmmo\tshakes\tswaps\tpunches\tround\tclipWidth\tsealScale\tsealY\tsealHitAt\thealthX\thealthY\tammoX\tammoY\tcaptionX\tcaptionY");
            UiEvents.Raised += OnUi;
            StartCoroutine(Sample());
        }

        private T Field<T>(string name) where T : class
        {
            FieldInfo field = typeof(RunHudScreen).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(_hud) as T : null;
        }

        internal void Mark(string id, string detail)
        {
            if (_ended || _events == null) return;
            _events.WriteLine(string.Join("\t", N(Time.realtimeSinceStartupAsDouble), N(Time.unscaledTime), Time.frameCount.ToString(CI), N(AudioSettings.dspTime), id, detail));
            _events.Flush();
        }

        private void OnUi(UiEvent id, string detail)
        {
            Mark(id == UiEvent.StampLanded && detail == "round" ? "R3-cue" : "UI", id + ":" + detail);
        }

        private static string N(double n) => n.ToString("F6", CI);
        private static float Scale(RectTransform t) => t != null ? t.localScale.x : -1f;
        private static float X(TMP_Text t) => t != null ? t.rectTransform.anchoredPosition.x : -999f;
        private static float Y(TMP_Text t) => t != null ? t.rectTransform.anchoredPosition.y : -999f;

        private IEnumerator Sample()
        {
            var end = new WaitForEndOfFrame();
            while (!_ended)
            {
                yield return end;
                if (_ended || _hud == null) yield break;
                WeaponController weapon = WeaponRegistry.ActiveController;
                _samples.WriteLine(string.Join("\t", new[] {
                    N(Time.realtimeSinceStartupAsDouble), N(Time.unscaledTime), Time.frameCount.ToString(CI), N(AudioSettings.dspTime),
                    N(Scale(_coin)), N(_delta != null ? _delta.canvasRenderer.GetAlpha() : -1f), N(_leaf != null ? _leaf.StartTime : -1f),
                    _hud.HealthChunksLive.ToString(CI), N(_healthBar != null ? _healthBar.GhostValue : -1f),
                    N(_hud.HealGlowGraphic != null ? _hud.HealGlowGraphic.canvasRenderer.GetAlpha() : -1f), _hud.HeartBeats.ToString(CI), N(Scale(_heart)),
                    N(_hud.DamageEdges.EdgeAlpha((UiScreenEdge)0)), N(_hud.DamageEdges.EdgeAlpha((UiScreenEdge)1)), N(_hud.DamageEdges.EdgeAlpha((UiScreenEdge)2)), N(_hud.DamageEdges.EdgeAlpha((UiScreenEdge)3)),
                    _hud.PlayerArcs.LiveWedges(Time.unscaledTime).ToString(CI), _hud.ShotAccents.ToString(CI), _hud.CasingsLive.ToString(CI),
                    (weapon != null && weapon.Classic != null ? weapon.Classic.Ammo : -1).ToString(CI), _hud.ReloadPreviewRounds.ToString(CI), _hud.ReloadLandings.ToString(CI),
                    _hud.AmmoPulsing ? "1" : "0", _hud.DryFireShakes.ToString(CI), _hud.WeaponSwaps.ToString(CI), _hud.GoldPunches.ToString(CI),
                    _hud.RoundNumeral.Value.ToString(CI), N(_hud.RoundClip.sizeDelta.x), N(Scale(_hud.RoundSeal)), N(_hud.RoundSeal.anchoredPosition.y), N(_hud.RoundSealHitAt),
                    N(X(_health)), N(Y(_health)), N(X(_ammo)), N(Y(_ammo)), N(X(_caption)), N(Y(_caption)) }));
            }
        }

        internal void End()
        {
            if (_ended) return;
            Mark("end", "observation complete");
            _ended = true;
            UiEvents.Raised -= OnUi;
            _samples?.Dispose();
            _events?.Dispose();
            if (!string.IsNullOrEmpty(_folder)) AudioPlaybackTelemetry.WriteReport(Path.Combine(_folder, "audio-playback.tsv"));
        }
        private void OnApplicationQuit() { End(); }
        private void OnDestroy() { End(); }
    }
}
#endif
