#if DEVELOPMENT_BUILD || UNITY_EDITOR
// R4 WP-106 (S-10 WP-111 request, SAHIP-ISTEKLERI S-09): Prepare Run v2's evidence frames - dev builds and the Editor only, as
// the rest of the agent.
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.UI
{
    public static partial class UiCaptureAgent
    {
        private sealed partial class Driver
        {
            /// <summary>The <c>-ddUiCapture</c> value of this mode.</summary>
            private const string PrepareMode = "prepare";

            /// <summary>The screen version the first log line names (final-r4.zsh counts the frames as v2 only with it).</summary>
            private const string PrepareScreenVersion = "prepareRunV2";

            /// <summary>Frame 03's view: the weapon turned 55°, the camera 10° higher, one zoom step in (PrepareRunStage's units).</summary>
            private const float PrepareYaw = 55f;
            private const float PreparePitch = 10f;
            private const float PrepareZoom = 1f;

            /// <summary>Video 05's length: one full turn of the preview in this many seconds.</summary>
            private const float PrepareVideoSeconds = 6f;

            /// <summary>Frame 02's weapon: the first open one of these that is not the chosen one (a clear change of silhouette).</summary>
            private static readonly string[] PrepareSecondWeapons = { "lmg_1", "sg_1", "sr_1", "smg_1", "ar_2" };

            /// <summary>
            /// R4 WP-106 · <c>-ddUiCapture prepare -ddUiCaptureOut &lt;dir&gt;</c> (1920×1080: <c>-screen-width 1920 -screen-height
            /// 1080</c>; Turkish: <c>-ddUiLanguage tr</c>, a second launch into its own folder): the player's path to Prepare Run
            /// and four frames - <c>01-prepare-default</c> (the session's choices, the preview at its reset view, auto-turn held
            /// for a repeatable frame), <c>02-prepare-weapon-changed</c> (another open weapon focused and pressed),
            /// <c>03-prepare-preview-rotated</c> (the preview turned and moved in), <c>04-prepare-difficulty</c> (the hardest open
            /// mode focused and chosen - its detail line shows), then <c>video/prepare-preview</c> (a full turn of the preview with
            /// a zoom in and out, PrepareVideoSeconds; tools/td-capture's tdmux makes the MP4). The first line is
            /// <c>[UiCapture] screen=prepareRunV2</c>; a
            /// <c>prepare.json</c> beside the frames names each frame's weapon, view and mode. The session's weapon and mode are
            /// put back before the quit. Run it inside the guard window, as every capture.
            /// </summary>
            private IEnumerator PrepareFrames()
            {
                Debug.Log("[UiCapture] screen=" + PrepareScreenVersion + " language=" + UiSettings.Language + " framebuffer " + Screen.width + "x" + Screen.height);
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);

                yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 20f);
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 20f);
                SelectionScreen screen = FindObjectOfType<SelectionScreen>();
                if (screen == null)
                {
                    Debug.LogError("[UiCapture] prepare: the Prepare Run screen is not up");
                    Application.Quit();
                    yield break;
                }

                ClassicWeaponId weaponBefore = PrototypeUiFlow.SelectedWeapon;
                IPrepareDifficulty modes = PrepareDifficulty.Source;
                int modeBefore = modes.Selected;
                PrepareRunStage stage = screen.Stage;
                if (stage == null)
                {
                    Debug.LogWarning("[UiCapture] prepare: no 3D preview on this build - the side view stands in");
                }
                else
                {
                    stage.AutoTurn = false;
                    stage.ResetView();
                }

                var json = new StringBuilder();
                json.Append("{\"mode\":\"").Append(PrepareMode).Append("\",\"screen\":\"").Append(PrepareScreenVersion)
                    .Append("\",\"language\":\"").Append(UiSettings.Language).Append("\",\"framebuffer\":\"")
                    .Append(Screen.width).Append('x').Append(Screen.height).Append("\",\"frames\":[");

                // 01 - as the player arrives.
                yield return new WaitForSecondsRealtime(_delay);
                yield return WaitFor(() => stage == null || stage.Renders > 0, 10f);
                yield return Frame(folder, "01-prepare-default");
                AppendPrepareFrame(json, "01-prepare-default", screen, stage, true);

                // 02 - another open weapon: focused, then pressed (the stage and the preview follow).
                UiButton other = SecondOpenWeapon(screen);
                if (other != null)
                {
                    string shownBefore = stage != null ? stage.WeaponId : null;
                    UiInput.Focus(other);
                    Submit(other.gameObject);
                    yield return WaitFor(() => stage == null || stage.WeaponId != shownBefore, 10f);
                    yield return new WaitForSecondsRealtime(0.8f);
                }
                else
                {
                    Debug.LogWarning("[UiCapture] prepare: no second open weapon - frame 02 repeats the chosen one");
                }

                yield return Frame(folder, "02-prepare-weapon-changed");
                AppendPrepareFrame(json, "02-prepare-weapon-changed", screen, stage, false);

                // 03 - the preview turned and moved in.
                if (stage != null)
                {
                    int renders = stage.Renders;
                    stage.Orbit(new Vector2(PrepareYaw, PreparePitch));
                    stage.Zoom(PrepareZoom);
                    yield return WaitFor(() => stage.Renders > renders, 5f);
                    yield return new WaitForSecondsRealtime(0.5f);
                }

                yield return Frame(folder, "03-prepare-preview-rotated");
                AppendPrepareFrame(json, "03-prepare-preview-rotated", screen, stage, false);

                // 04 - the hardest open mode, focused and chosen: its row takes the check, the line under the rows its detail.
                UiButton[] rows = screen.DifficultyRows;
                int pick = -1;
                for (int i = rows != null ? rows.Length - 1 : -1; i >= 0; i--)
                {
                    if (!modes.IsLockedAt(i))
                    {
                        pick = i;
                        break;
                    }
                }

                if (pick >= 0)
                {
                    UiInput.Focus(rows[pick]);
                    Submit(rows[pick].gameObject);
                    yield return new WaitForSecondsRealtime(0.8f);
                }

                yield return Frame(folder, "04-prepare-difficulty");
                AppendPrepareFrame(json, "04-prepare-difficulty", screen, stage, false);

                // 05 - the preview in motion (video/prepare-preview, the windows tour's recorder): a full turn by hand, one zoom
                // step in and back out on the way; the times from the real clock (the frames are recorded in real time).
                if (stage != null)
                {
                    stage.ResetView();
                    yield return WaitFor(() => stage.Renders > 0, 5f);
                    StartCoroutine(Record(folder, "prepare-preview"));
                    float start = Time.realtimeSinceStartup, last = start;
                    while (last - start < PrepareVideoSeconds)
                    {
                        yield return null;
                        float now = Time.realtimeSinceStartup, step = now - last, t = now - start;
                        last = now;
                        stage.Orbit(new Vector2(360f / PrepareVideoSeconds * step, 0f));
                        if (t > 0.35f * PrepareVideoSeconds && t < 0.5f * PrepareVideoSeconds)
                        {
                            stage.Zoom(step / (0.15f * PrepareVideoSeconds));
                        }
                        else if (t > 0.6f * PrepareVideoSeconds && t < 0.75f * PrepareVideoSeconds)
                        {
                            stage.Zoom(-step / (0.15f * PrepareVideoSeconds));
                        }
                    }

                    _recording = false;
                    yield return new WaitForSecondsRealtime(0.5f);
                }

                // 06 / 07 - C-00 12.10: the two weapons that lie on the post shim (PrepareRunStage.Seats: ar_2 on the rear cap,
                // smg_1 on the front one), chosen like 02 and seen in the default view.
                if (stage != null)
                {
                    for (int k = 0; k < PrepareShimWeapons.Length; k++)
                    {
                        string key = PrepareShimWeapons[k];
                        UiButton row = OpenWeapon(screen, key);
                        if (row == null)
                        {
                            Debug.LogWarning("[UiCapture] prepare: no open " + key + " row - no shim frame");
                            continue;
                        }

                        UiInput.Focus(row);
                        Submit(row.gameObject);
                        yield return WaitFor(() => stage.WeaponId == key, 10f);
                        stage.ResetView();
                        yield return new WaitForSecondsRealtime(0.8f);
                        string name = "0" + (6 + k) + "-prepare-shim-" + key.Replace("_", string.Empty);
                        yield return Frame(folder, name);
                        AppendPrepareFrame(json, name, screen, stage, false);
                    }
                }

                json.Append("]}");
                File.WriteAllText(Path.Combine(folder, "prepare.json"), json.ToString());
                Debug.Log("[UiCapture] prepare " + json);

                // The session's choices go back (the Player quits here; an Editor session keeps its statics).
                PrototypeUiFlow.SelectedWeapon = weaponBefore;
                modes.Select(modeBefore);
                Debug.Log("[UiCapture] prepare done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>C-00 12.10: the weapons the post shim carries (frames 06 / 07).</summary>
            private static readonly string[] PrepareShimWeapons = { "ar_2", "smg_1" };

            /// <summary>The open (pressable) row of weapon <paramref name="weaponId"/>, or null.</summary>
            private static UiButton OpenWeapon(SelectionScreen screen, string weaponId)
            {
                UiButton[] rows = screen.WeaponRows;
                for (int i = 0; rows != null && i < rows.Length; i++)
                {
                    UiButton row = rows[i];
                    if (row != null && row.ConfirmCue && row.name.StartsWith("Weapon." + weaponId + ".", System.StringComparison.Ordinal))
                    {
                        return row;
                    }
                }

                return null;
            }

            private static UiButton SecondOpenWeapon(SelectionScreen screen)
            {
                UiButton[] rows = screen.WeaponRows;
                if (rows == null)
                {
                    return null;
                }

                UiButton fallback = null;
                foreach (string weaponId in PrepareSecondWeapons)
                {
                    for (int i = 0; i < rows.Length; i++)
                    {
                        UiButton row = rows[i];
                        if (row == null || row.IsSelectedChoice || !row.ConfirmCue)
                        {
                            continue;   // the chosen row, or a locked one (it refuses the press)
                        }

                        if (row.name.StartsWith("Weapon." + weaponId + ".", System.StringComparison.Ordinal))
                        {
                            return row;
                        }

                        fallback = fallback ?? row;
                    }
                }

                return fallback;
            }

            private static void AppendPrepareFrame(StringBuilder json, string frame, SelectionScreen screen, PrepareRunStage stage, bool first)
            {
                IPrepareDifficulty modes = PrepareDifficulty.Source;
                CultureInfo inv = CultureInfo.InvariantCulture;
                json.Append(first ? "{" : ",{").Append("\"frame\":\"").Append(frame).Append("\",\"weapon\":\"")
                    .Append(stage != null ? stage.WeaponId : string.Empty).Append("\",\"selected\":\"").Append(PrototypeUiFlow.SelectedWeapon)
                    .Append("\",\"yaw\":").Append(stage != null ? stage.Yaw.ToString("0.0", inv) : "null")
                    .Append(",\"distance\":").Append(stage != null ? stage.Distance.ToString("0.000", inv) : "null")
                    .Append(",\"renders\":").Append(stage != null ? stage.Renders : 0)
                    .Append(",\"difficulty\":\"").Append(modes.IdAt(modes.Selected)).Append("\",\"statColumns\":").Append(screen.StatColumns)
                    .Append(",\"specialsBeside\":").Append(screen.SpecialsBeside ? "true" : "false").Append('}');
                Debug.Log("[UiCapture] " + frame + " weapon=" + (stage != null ? stage.WeaponId : "-") + " yaw=" + (stage != null ? stage.Yaw.ToString("0.0", inv) : "-")
                          + " distance=" + (stage != null ? stage.Distance.ToString("0.000", inv) : "-") + " difficulty=" + modes.IdAt(modes.Selected));
            }
        }
    }
}
#endif
