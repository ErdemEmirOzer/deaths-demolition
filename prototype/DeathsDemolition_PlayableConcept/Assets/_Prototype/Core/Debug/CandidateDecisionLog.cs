using System;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>One PROTOTYPE-ONLY value somebody liked enough to flag during a run.</summary>
    [Serializable]
    public struct CandidateDecision
    {
        public string FieldPath;
        public string Value;
        public string PendingDecision;
        public string EnglishLabel;
        public string Context;

        /// <summary>Panel module the value came from: Config, Weapons, Player, Crowd, ...</summary>
        public string Module;

        /// <summary>Type that declares the field, so the value can be found again in code.</summary>
        public string Owner;

        /// <summary>True when the number had no source at all and was invented to let the system run.</summary>
        public bool Invented;
    }

    /// <summary>
    /// The "candidate decision list" (S6).
    ///
    /// Flagging a value here changes nothing. It is a note that a number felt right
    /// during play. Nothing reaches planning/ from this list without an explicit,
    /// separate instruction from the user - this prototype has no write access to
    /// planning/ at all.
    ///
    /// The list accepts any row the F1 panel can draw, which since the index was widened
    /// means per-system tuning values too, not only PrototypeConfig fields.
    /// </summary>
    public static class CandidateDecisionLog
    {
        private static readonly List<CandidateDecision> s_entries = new List<CandidateDecision>(16);

        public static IReadOnlyList<CandidateDecision> Entries => s_entries;

        public static event Action Changed;

        /// <summary>
        /// Flag a value straight from an index row. Carries the module and owning type with
        /// it, so a value from WeaponUpgradeTuning is traceable back to its declaration and
        /// not just to a field name.
        /// </summary>
        public static void Record(ConfigFieldBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            Record(
                binding.Path,
                binding.ValueAsString(),
                binding.Origin != null ? binding.Origin.PendingDecision : PendingDecisions.None,
                binding.Origin != null
                    ? binding.Origin.EnglishLabel
                    : PendingDecisions.ToEnglishLabel(PendingDecisions.None),
                binding.DeclarationKey,
                binding.Module,
                binding.OwnerLabel,
                binding.Origin != null && binding.Origin.Invented);
        }

        public static void Record(string fieldPath, string value, string pendingDecision, string englishLabel,
            string context = null, string module = null, string owner = null, bool invented = false)
        {
            for (int i = 0; i < s_entries.Count; i++)
            {
                if (s_entries[i].FieldPath == fieldPath)
                {
                    CandidateDecision updated = s_entries[i];
                    updated.Value = value;
                    updated.Context = context;
                    updated.Module = module;
                    updated.Owner = owner;
                    updated.Invented = invented;
                    s_entries[i] = updated;
                    Changed?.Invoke();
                    return;
                }
            }

            s_entries.Add(new CandidateDecision
            {
                FieldPath = fieldPath,
                Value = value,
                PendingDecision = pendingDecision,
                EnglishLabel = englishLabel,
                Context = context,
                Module = module,
                Owner = owner,
                Invented = invented
            });

            Changed?.Invoke();
        }

        public static void Remove(string fieldPath)
        {
            for (int i = 0; i < s_entries.Count; i++)
            {
                if (s_entries[i].FieldPath == fieldPath)
                {
                    s_entries.RemoveAt(i);
                    Changed?.Invoke();
                    return;
                }
            }
        }

        public static bool Contains(string fieldPath)
        {
            for (int i = 0; i < s_entries.Count; i++)
            {
                if (s_entries[i].FieldPath == fieldPath)
                {
                    return true;
                }
            }

            return false;
        }

        public static void Clear()
        {
            s_entries.Clear();
            Changed?.Invoke();
        }

        /// <summary>Markdown table, ready to paste into PROTOTYPE-ONLY.md by hand.</summary>
        public static string ToMarkdown()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("| Module | Owner | Field | Value | Invented | Pending decision |");
            sb.AppendLine("|---|---|---|---|---|---|");

            for (int i = 0; i < s_entries.Count; i++)
            {
                CandidateDecision e = s_entries[i];
                sb.Append("| ").Append(string.IsNullOrEmpty(e.Module) ? "-" : e.Module).Append(" | ")
                    .Append(string.IsNullOrEmpty(e.Owner) ? "-" : e.Owner).Append(" | `")
                    .Append(e.FieldPath).Append("` | ")
                    .Append(e.Value).Append(" | ")
                    .Append(e.Invented ? "yes" : "no").Append(" | ")
                    .Append(e.PendingDecision).AppendLine(" |");
            }

            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_entries.Clear();
            Changed = null;
        }
    }
}
