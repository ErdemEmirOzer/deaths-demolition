using System;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// Marks a serialized field as a PROTOTYPE-ONLY value and records which pending
    /// planning decision it temporarily occupies.
    ///
    /// Nothing carrying this attribute is a product decision. The attribute is read by
    /// the Inspector drawer and by the F1 debug panel so the origin is always visible
    /// at the moment somebody is tempted to treat the number as settled.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class PrototypeOnlyAttribute : PropertyAttribute
    {
        /// <summary>Verbatim planning line this value stands in for. See <see cref="PendingDecisions"/>.</summary>
        public readonly string PendingDecision;

        /// <summary>Optional English note: where the number came from, or that it is a pure guess.</summary>
        public readonly string Note;

        /// <summary>
        /// True when the number is not present anywhere in the source request and was
        /// invented purely so the system could run. These need explicit sign-off.
        /// </summary>
        public readonly bool Invented;

        public PrototypeOnlyAttribute(string pendingDecision)
            : this(pendingDecision, null, false)
        {
        }

        public PrototypeOnlyAttribute(string pendingDecision, string note)
            : this(pendingDecision, note, false)
        {
        }

        public PrototypeOnlyAttribute(string pendingDecision, string note, bool invented)
        {
            PendingDecision = string.IsNullOrEmpty(pendingDecision) ? PendingDecisions.None : pendingDecision;
            Note = note;
            Invented = invented;
        }

        /// <summary>Short English caption safe to show on an in-game surface.</summary>
        public string EnglishLabel => PendingDecisions.ToEnglishLabel(PendingDecision);
    }
}
