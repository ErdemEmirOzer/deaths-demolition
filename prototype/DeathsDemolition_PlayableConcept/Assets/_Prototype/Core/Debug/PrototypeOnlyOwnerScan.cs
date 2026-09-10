using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>
    /// Finds the live objects that hold the tagged fields <see cref="PrototypeOnlyCensus"/>
    /// declares, so the F1 panel can show values that live outside PrototypeConfig.
    ///
    /// Why reflection instead of self-registration: the tagged fields sit in
    /// DD.Prototype.Runtime, which Core is not allowed to reference, and a registration
    /// call that nobody writes disappears without a sound. A type scan cannot be forgotten
    /// - a new [PrototypeOnly] field is visible the moment it compiles. The cost is that a
    /// declaration with no live instance cannot be shown; that case is counted, not hidden
    /// (see <see cref="ConfigFieldIndexReport.Unbound"/>).
    ///
    /// This sweep walks every loaded UnityEngine.Object once. It belongs on panel open or
    /// an explicit rescan, never in Update.
    /// </summary>
    public static class PrototypeOnlyOwnerScan
    {
        private const int MaxDepth = 4;

        /// <summary>Type -> "does this type, or anything serialized under it, declare a tagged field".</summary>
        private static readonly Dictionary<Type, bool> s_carriesTagged = new Dictionary<Type, bool>(256);

        // Elle kayit dikisi KALDIRILDI: RegisterOwner/UnregisterOwner sifir cagranli
        // public API idi ve HandoffIntegrityTests onu "belgelenmis ama icra edilmemis
        // devir" sinifinda yakaladi. Census tip taramasi bu ihtiyaci zaten karsiliyor;
        // gerekirse geri gelir - ama BU SEFER bir cagranla birlikte.


        /// <summary>
        /// Adds a row for every declared tagged field outside <paramref name="boundKeys"/>
        /// that a live owner can be found for, and marks each one bound.
        /// </summary>
        internal static void Collect(HashSet<string> boundKeys, ConfigFieldIndexReport report)
        {
            IReadOnlyList<PrototypeOnlyDeclaration> declarations = PrototypeOnlyCensus.Declarations;

            var rows = new List<ConfigFieldBinding>(256);

            // 1. const / static declarations need no instance at all.
            for (int i = 0; i < declarations.Count; i++)
            {
                PrototypeOnlyDeclaration declaration = declarations[i];
                if (!declaration.IsStatic || boundKeys.Contains(declaration.Key))
                {
                    continue;
                }

                rows.Add(BuildRow(declaration.Field, declaration.Origin, null, declaration.DeclaringType,
                    declaration.Module, ConfigFieldIndex.Prettify(declaration.Field.Name), 1));
                boundKeys.Add(declaration.Key);
            }

            // 2. Instance declarations need a live owner.
            var candidates = new Dictionary<Type, List<object>>(64);
            CollectSingletonOwners(declarations, candidates, report);
            CollectLoadedOwners(candidates, report);

            // Elle kayit dikisi KALDIRILDI: sifir cagranli public API idi ve
            // HandoffIntegrityTests onu "belgelenmis ama icra edilmemis devir"
            // sinifinda yakaladi. Census tip taramasi zaten bu ihtiyaci karsiliyor;
            // gerekirse dort satirla, BU SEFER bir cagranla birlikte geri gelir.

            foreach (KeyValuePair<Type, List<object>> pair in candidates)
            {
                List<object> instances = pair.Value;
                object representative = ChooseRepresentative(instances);
                if (representative == null)
                {
                    continue;
                }

                report.OwnersWithTaggedFields++;
                string module = PrototypeOnlyCensus.ModuleFor(pair.Key);
                WalkOwner(representative, pair.Key, module, "", instances.Count, 0, boundKeys, rows);
            }

            Sort(rows);

            for (int i = 0; i < rows.Count; i++)
            {
                report.Bindings.Add(rows[i]);
            }

            report.BoundOutsideConfig = rows.Count;
        }

        // ------------------------------------------------------------------ owner sources

        /// <summary>
        /// A per-system tuning ScriptableObject usually exists only as a code-default
        /// singleton behind a static Active property, created the first time gameplay asks
        /// for it. Reading that property here is the same call the game makes; without it
        /// the whole upgrade/gold surface would be invisible until the first upgrade card.
        /// </summary>
        private static void CollectSingletonOwners(IReadOnlyList<PrototypeOnlyDeclaration> declarations,
            Dictionary<Type, List<object>> candidates, ConfigFieldIndexReport report)
        {
            var seen = new HashSet<Type>();

            for (int i = 0; i < declarations.Count; i++)
            {
                Type type = declarations[i].DeclaringType;
                if (!seen.Add(type))
                {
                    continue;
                }

                if (!typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    continue;
                }

                PropertyInfo active;
                try
                {
                    active = type.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                }
                catch (Exception)
                {
                    continue;
                }

                if (active == null || !active.CanRead || active.PropertyType != type)
                {
                    continue;
                }

                try
                {
                    var value = active.GetValue(null) as UnityEngine.Object;
                    if (value != null)
                    {
                        AddCandidate(candidates, value);
                        report.SingletonsActivated++;
                    }
                }
                catch (Exception)
                {
                    // A singleton that throws while building itself is not this panel's problem.
                }
            }
        }

        private static void CollectLoadedOwners(Dictionary<Type, List<object>> candidates,
            ConfigFieldIndexReport report)
        {
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                report.OwnersScanned++;
                AddCandidate(candidates, behaviours[i]);
            }

            ScriptableObject[] assets = Resources.FindObjectsOfTypeAll<ScriptableObject>();
            for (int i = 0; i < assets.Length; i++)
            {
                report.OwnersScanned++;
                AddCandidate(candidates, assets[i]);
            }
        }

        private static void AddCandidate(Dictionary<Type, List<object>> candidates, object owner)
        {
            if (owner == null)
            {
                return;
            }

            if (owner is UnityEngine.Object unityOwner && unityOwner == null)
            {
                return;
            }

            Type type = owner.GetType();
            if (!CarriesTaggedField(type, 0))
            {
                return;
            }

            if (!candidates.TryGetValue(type, out List<object> list))
            {
                list = new List<object>(4);
                candidates.Add(type, list);
            }

            list.Add(owner);
        }

        /// <summary>
        /// Prefer an instance that actually lives in a scene: a prefab asset and a spawned
        /// copy are both "loaded", but only the spawned copy is the one being played.
        /// </summary>
        private static object ChooseRepresentative(List<object> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] is Component component && component != null && component.gameObject.scene.IsValid())
                {
                    return instances[i];
                }
            }

            return instances.Count > 0 ? instances[0] : null;
        }

        // ------------------------------------------------------------------ the walk

        /// <summary>
        /// Records tagged leaves only. Outside PrototypeConfig an untagged field is not a
        /// prototype value standing in for a decision, it is ordinary plumbing, and putting
        /// it on this surface would bury the values that do need watching.
        /// </summary>
        private static void WalkOwner(object owner, Type ownerType, string module, string pathPrefix,
            int instanceCount, int depth, HashSet<string> boundKeys, List<ConfigFieldBinding> rows)
        {
            if (owner == null || depth > MaxDepth)
            {
                return;
            }

            Type type = owner.GetType();
            while (type != null && type != typeof(object) && type != typeof(MonoBehaviour)
                   && type != typeof(ScriptableObject) && type != typeof(Behaviour)
                   && type != typeof(Component) && type != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(ConfigFieldIndex.Flags);
                }
                catch (Exception)
                {
                    return;
                }

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    string display = string.IsNullOrEmpty(pathPrefix)
                        ? ConfigFieldIndex.Prettify(field.Name)
                        : pathPrefix + " > " + ConfigFieldIndex.Prettify(field.Name);

                    var origin = field.GetCustomAttribute<PrototypeOnlyAttribute>();
                    if (origin != null)
                    {
                        string key = PrototypeOnlyCensus.KeyOf(field);
                        if (boundKeys.Add(key))
                        {
                            rows.Add(BuildRow(field, origin, owner, ownerType, module, display, instanceCount));
                        }

                        continue;
                    }

                    Type fieldType = field.FieldType;
                    Type elementType = null;
                    bool isList = fieldType.IsArray || IsGenericList(fieldType);

                    if (isList)
                    {
                        elementType = ConfigFieldIndex.GetElementType(fieldType);
                        if (elementType == null || !ConfigFieldIndex.IsSerializableClass(elementType)
                                                || !CarriesTaggedField(elementType, 0))
                        {
                            continue;
                        }
                    }
                    else if (ConfigFieldIndex.IsLeaf(fieldType)
                             || !ConfigFieldIndex.IsSerializableClass(fieldType)
                             || !CarriesTaggedField(fieldType, 0))
                    {
                        continue;
                    }

                    object value;
                    try
                    {
                        value = field.GetValue(field.IsStatic ? null : owner);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (value == null || value is string || value is UnityEngine.Object)
                    {
                        continue;
                    }

                    if (isList && value is IList list)
                    {
                        // One representative element is enough: a declaration is a field on a
                        // type, not a field on an element, so nothing is lost by not repeating it.
                        for (int e = 0; e < list.Count; e++)
                        {
                            if (list[e] == null)
                            {
                                continue;
                            }

                            string label = ConfigFieldIndex.DescribeElement(list[e], e);
                            WalkOwner(list[e], ownerType, module, display + "[" + label + "]", list.Count, depth + 1,
                                boundKeys, rows);
                            break;
                        }

                        continue;
                    }

                    if (!isList)
                    {
                        WalkOwner(value, ownerType, module, display, instanceCount, depth + 1, boundKeys, rows);
                    }
                }

                type = type.BaseType;
            }
        }

        private static ConfigFieldBinding BuildRow(FieldInfo field, PrototypeOnlyAttribute origin, object owner,
            Type ownerType, string module, string display, int instanceCount)
        {
            var range = field.GetCustomAttribute<RangeAttribute>();
            string ownerLabel = ownerType != null ? ownerType.Name : field.DeclaringType.Name;

            return new ConfigFieldBinding
            {
                Path = module + "." + ownerLabel + "." + display,
                DisplayName = display,
                GroupPath = module + " / " + ownerLabel,
                Field = field,
                Owner = owner,
                FieldType = field.FieldType,
                Origin = origin,
                HasRange = range != null,
                RangeMin = range != null ? range.min : 0f,
                RangeMax = range != null ? range.max : 1f,
                Module = module,
                OwnerLabel = ownerLabel,
                OwnerInstanceCount = instanceCount,
                IsStaticField = field.IsStatic,
                IsConstant = field.IsLiteral,
                DeclarationKey = PrototypeOnlyCensus.KeyOf(field)
            };
        }

        // ------------------------------------------------------------------ type analysis

        /// <summary>
        /// True when instances of this type are worth walking. Cached per type, so the
        /// sweep costs one dictionary lookup per loaded object in the common case.
        /// </summary>
        private static bool CarriesTaggedField(Type type, int depth)
        {
            if (type == null || depth > MaxDepth || ConfigFieldIndex.IsLeaf(type) || type.IsPrimitive)
            {
                return false;
            }

            if (s_carriesTagged.TryGetValue(type, out bool cached))
            {
                return cached;
            }

            // Guard against a type that reaches itself through a field.
            s_carriesTagged[type] = false;

            bool found = false;
            Type cursor = type;
            while (!found && cursor != null && cursor != typeof(object) && cursor != typeof(MonoBehaviour)
                   && cursor != typeof(ScriptableObject) && cursor != typeof(Behaviour)
                   && cursor != typeof(Component) && cursor != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields;
                try
                {
                    fields = cursor.GetFields(ConfigFieldIndex.Flags);
                }
                catch (Exception)
                {
                    break;
                }

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.GetCustomAttribute<PrototypeOnlyAttribute>() != null)
                    {
                        found = true;
                        break;
                    }

                    Type fieldType = field.FieldType;
                    if (fieldType.IsArray || IsGenericList(fieldType))
                    {
                        Type elementType = ConfigFieldIndex.GetElementType(fieldType);
                        if (elementType != null && ConfigFieldIndex.IsSerializableClass(elementType)
                                                && CarriesTaggedField(elementType, depth + 1))
                        {
                            found = true;
                            break;
                        }

                        continue;
                    }

                    if (ConfigFieldIndex.IsSerializableClass(fieldType) && CarriesTaggedField(fieldType, depth + 1))
                    {
                        found = true;
                        break;
                    }
                }

                cursor = cursor.BaseType;
            }

            s_carriesTagged[type] = found;
            return found;
        }

        private static bool IsGenericList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>Module, then owner, then declaration order. Stable across rebuilds.</summary>
        private static void Sort(List<ConfigFieldBinding> rows)
        {
            var order = new Dictionary<ConfigFieldBinding, int>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                order[rows[i]] = i;
            }

            rows.Sort((a, b) =>
            {
                int byModule = string.Compare(a.Module, b.Module, StringComparison.Ordinal);
                if (byModule != 0)
                {
                    return byModule;
                }

                int byOwner = string.Compare(a.OwnerLabel, b.OwnerLabel, StringComparison.Ordinal);
                return byOwner != 0 ? byOwner : order[a].CompareTo(order[b]);
            });
        }

        /// <summary>Diagnostic caption for a row the panel is about to draw.</summary>
        public static string DescribeOwner(ConfigFieldBinding binding)
        {
            if (binding == null)
            {
                return string.Empty;
            }

            if (binding.IsConstant)
            {
                return "const";
            }

            if (binding.OwnerInstanceCount > 1)
            {
                return binding.OwnerInstanceCount.ToString(CultureInfo.InvariantCulture) + " live";
            }

            return string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_carriesTagged.Clear();
        }
    }
}
