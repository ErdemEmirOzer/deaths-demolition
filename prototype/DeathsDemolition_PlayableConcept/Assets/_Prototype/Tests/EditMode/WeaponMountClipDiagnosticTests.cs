using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-059 diag: which generic (non-humanoid) transform curves the AP-009 aim clips carry — do the sockets move at all?</summary>
    [TestFixture]
    [Category("WP059Diag")]
    public sealed class WeaponMountClipDiagnosticTests
    {
        [Test]
        public void AimKlipleri_SoketEgrileri()
        {
            string[] names = { "AP009_player_A_idle_aim", "AP009_player_C_idle_aim", "AP009_player_idle", "AP009_player_walk_fwd" };
            foreach (string name in names)
            {
                string[] guids = AssetDatabase.FindAssets(name + " t:AnimationClip");
                AnimationClip clip = null;
                foreach (string g in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (o is AnimationClip c && c.name == name) { clip = c; break; }
                    }
                    if (clip != null) break;
                }

                if (clip == null) { Debug.Log("[DD-TEST] wp059.clip " + name + " NOT FOUND"); continue; }
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                var generic = new List<string>();
                int muscles = 0;
                foreach (EditorCurveBinding b in bindings)
                {
                    if (string.IsNullOrEmpty(b.path)) { muscles++; continue; }
                    generic.Add(b.path.Substring(b.path.LastIndexOf('/') + 1) + ":" + b.propertyName);
                }

                generic.Sort();
                var sockets = generic.FindAll(g => g.Contains("socket"));
                Debug.Log("[DD-TEST] wp059.clip " + name + " humanoid/root curves=" + muscles + " generic=" + generic.Count + " socketCurves=" + sockets.Count +
                          (sockets.Count > 0 ? " [" + string.Join(", ", sockets.GetRange(0, Mathf.Min(sockets.Count, 12))) + "]" : "") +
                          " firstGeneric=" + (generic.Count > 0 ? string.Join(", ", generic.GetRange(0, Mathf.Min(generic.Count, 6))) : "-"));
            }
        }
    }
}
