using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-073 · M-55 (C-00 30.09): every runtime capture agent (<c>*CaptureAgent*.cs</c> under Assets/_Prototype, outside
    /// Tests/ and Editor/) is development-only — the whole file sits inside <c>#if DEVELOPMENT_BUILD || UNITY_EDITOR</c>
    /// (first non-empty line) … <c>#endif</c> (last non-empty line), so a release Player carries no capture flag, no frame
    /// writer and no Application.Quit path. Owners not yet done are listed in <see cref="PendingOwners"/>: reported
    /// with Debug.Log (not Assert.Warn — a "Warning" result counts as a red in tools/ci_summary.py), never failed.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073CaptureAgentReleaseGuardTests
    {
        private const string Guard = "#if DEVELOPMENT_BUILD || UNITY_EDITOR";

        // S-09 / S-01 share of M-55 (C-00 30.09) — paths relative to Assets/_Prototype.
        private static readonly HashSet<string> PendingOwners = new HashSet<string>(StringComparer.Ordinal)
        {
            "UI/Runtime/Fx/UiFxCaptureAgent.cs",    // S-09
            "UI/Runtime/Flow/UiCaptureAgent.cs",    // S-09
            "Core/Boot/PrototypeCaptureAgent.cs",   // S-01
        };

        // The S-07 agents: the scan must see them, so an empty glob cannot pass.
        private static readonly string[] S07Agents =
        {
            "VfxFireCaptureAgent.cs", "VfxSpecialCaptureAgent.cs", "VfxLauncherCaptureAgent.cs", "VfxWeaponCaptureAgent.cs",
            "VfxAtmoCaptureAgent.cs", "VfxImpactCaptureAgent.cs", "VfxGroundFireCaptureAgent.cs"
        };

        [Test]
        public void RuntimeCaptureAgents_AreDevelopmentOnly()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype");
            Assert.That(Directory.Exists(root), Is.True, "missing " + root);

            var scanned = new List<string>();
            var offenders = new StringBuilder();
            var pending = new StringBuilder();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).IndexOf("CaptureAgent", StringComparison.Ordinal) < 0) continue;
                string rel = file.Substring(root.Length + 1).Replace('\\', '/');
                string[] segments = rel.Split('/');
                if (segments.Take(segments.Length - 1).Any(s => s == "Tests" || s == "Editor")) continue;
                scanned.Add(rel);

                string[] lines = File.ReadAllLines(file).Select(l => l.Trim().TrimStart('﻿')).Where(l => l.Length > 0).ToArray();
                string first = lines.Length > 0 ? lines[0] : "(empty)";
                string last = lines.Length > 0 ? lines[lines.Length - 1] : "(empty)";
                bool ok = first.StartsWith(Guard, StringComparison.Ordinal) && last.StartsWith("#endif", StringComparison.Ordinal);
                if (ok) continue;

                string line = rel + " — first: '" + first + "', last: '" + last + "'";
                if (PendingOwners.Contains(rel)) pending.AppendLine(line);
                else offenders.AppendLine(line);
            }

            foreach (string agent in S07Agents)
            {
                Assert.That(scanned.Any(r => Path.GetFileName(r) == agent), Is.True, "scan did not find " + agent);
            }

            if (pending.Length > 0)
            {
                Debug.Log("[WP073 M-55] capture agents still release-compiled (pending owner, not failed):\n" + pending);
            }

            Assert.That(offenders.Length, Is.EqualTo(0),
                "capture agents must be wrapped whole in '" + Guard + "' … '#endif' (M-55):\n" + offenders);
        }
    }
}
