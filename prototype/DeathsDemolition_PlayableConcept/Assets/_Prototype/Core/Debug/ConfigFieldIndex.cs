using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>
    /// One tagged leaf field: either inside <see cref="PrototypeConfig"/> or on any other
    /// live object that declares a <see cref="PrototypeOnlyAttribute"/> field.
    /// </summary>
    public sealed class ConfigFieldBinding
    {
        public string Path;
        public string DisplayName;
        public string GroupPath;
        public FieldInfo Field;
        public object Owner;
        public Type FieldType;
        public PrototypeOnlyAttribute Origin;
        public bool HasRange;
        public float RangeMin;
        public float RangeMax;

        /// <summary>Panel heading this row belongs under: Config, Weapons, Player, Crowd, ...</summary>
        public string Module = ConfigFieldIndex.ConfigModule;

        /// <summary>Type name of the object that actually holds the value.</summary>
        public string OwnerLabel;

        /// <summary>How many live instances declare this field. The row edits the first one.</summary>
        public int OwnerInstanceCount = 1;

        /// <summary>const or static: readable with no instance at all.</summary>
        public bool IsStaticField;

        /// <summary>const: the compiler already inlined it, so the panel must not pretend to edit it.</summary>
        public bool IsConstant;

        /// <summary>Census identity, used to tell bound declarations apart from lost ones.</summary>
        public string DeclarationKey;

        /// <summary>Text buffer used by the panel while the user is typing.</summary>
        public string EditBuffer;

        public object GetValue()
        {
            return Field.GetValue(IsStaticField ? null : Owner);
        }

        public void SetValue(object value)
        {
            if (IsConstant)
            {
                return;
            }

            Field.SetValue(IsStaticField ? null : Owner, value);
        }

        public string ValueAsString()
        {
            object v = GetValue();
            if (v == null)
            {
                return "";
            }

            if (v is float f)
            {
                return f.ToString("0.####", CultureInfo.InvariantCulture);
            }

            if (v is Color c)
            {
                return "#" + ColorUtility.ToHtmlStringRGB(c);
            }

            return v.ToString();
        }

        /// <summary>True when the value is a plain number the panel can edit as text.</summary>
        public bool IsNumeric => FieldType == typeof(float) || FieldType == typeof(int);

        public bool IsBool => FieldType == typeof(bool);
        public bool IsEnum => FieldType.IsEnum;
        public bool IsString => FieldType == typeof(string);

        /// <summary>Anything the panel can only display, not edit. Use the Inspector for these.</summary>
        public bool IsReadOnly => IsConstant || (!IsNumeric && !IsBool && !IsEnum && !IsString);
    }

    /// <summary>What a full index build managed to reach, and what it did not.</summary>
    public sealed class ConfigFieldIndexReport
    {
        /// <summary>Every row the F1 panel will draw, config rows first.</summary>
        public List<ConfigFieldBinding> Bindings = new List<ConfigFieldBinding>(512);

        /// <summary>Tagged fields reached through the PrototypeConfig asset.</summary>
        public int BoundInsideConfig;

        /// <summary>Tagged fields reached through some other live owner.</summary>
        public int BoundOutsideConfig;

        /// <summary>Declared tagged fields living outside PrototypeConfig, bound or not.</summary>
        public int DeclaredOutsideConfig;

        /// <summary>Declared tagged fields no live owner could be found for. The measured loss.</summary>
        public List<string> Unbound = new List<string>(32);

        /// <summary>Rows in <see cref="Bindings"/> that carry no origin tag at all.</summary>
        public List<string> Untagged = new List<string>(8);

        /// <summary>Loaded UnityEngine.Objects examined during the owner sweep.</summary>
        public int OwnersScanned;

        /// <summary>Owner instances that actually carried tagged fields.</summary>
        public int OwnersWithTaggedFields;

        /// <summary>Singleton ScriptableObjects the sweep had to touch to make them exist.</summary>
        public int SingletonsActivated;

        public int UnboundCount => Unbound.Count;
    }

    /// <summary>
    /// Reflects tagged fields into a flat, editable row list once, so the F1 panel never
    /// reflects per frame.
    ///
    /// Two scopes:
    ///   Build(config)    - the PrototypeConfig asset only. Every serialized leaf, because
    ///                      every leaf in there is required to carry an origin tag.
    ///   BuildAll(config) - the config plus every other live object that declares a
    ///                      [PrototypeOnly] field (per-system *Tuning objects, component
    ///                      fields, const tables). Outside the config only tagged fields
    ///                      are indexed, so the surface stays exactly the S6 surface.
    ///
    /// BuildAll is measured against <see cref="PrototypeOnlyCensus"/>: anything the code
    /// declares but the sweep could not reach is listed in
    /// <see cref="ConfigFieldIndexReport.Unbound"/> rather than silently missing.
    ///
    /// Rebuild when the config asset is swapped or when the user asks for a rescan, never
    /// from Update: the owner sweep walks every loaded object once.
    /// </summary>
    public static class ConfigFieldIndex
    {
        /// <summary>Module heading used for everything reached through PrototypeConfig.</summary>
        public const string ConfigModule = "Config";

        private const int MaxDepth = 4;

        internal const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static List<ConfigFieldBinding> Build(PrototypeConfig config)
        {
            var result = new List<ConfigFieldBinding>(256);
            if (config == null)
            {
                return result;
            }

            Walk(config, "", "", 0, result);
            return result;
        }

        /// <summary>
        /// The whole S6 surface: config rows plus every tagged field on any other live
        /// owner, with an explicit count of what could not be bound.
        /// </summary>
        public static ConfigFieldIndexReport BuildAll(PrototypeConfig config)
        {
            var report = new ConfigFieldIndexReport();

            List<ConfigFieldBinding> configRows = Build(config);
            var boundKeys = new HashSet<string>();

            for (int i = 0; i < configRows.Count; i++)
            {
                ConfigFieldBinding row = configRows[i];
                row.Module = ConfigModule;
                row.OwnerLabel = config != null ? config.GetType().Name : "PrototypeConfig";
                row.GroupPath = ConfigModule + " / " + row.GroupPath;

                if (row.Origin == null)
                {
                    report.Untagged.Add(row.Path);
                }
                else if (boundKeys.Add(row.DeclarationKey))
                {
                    report.BoundInsideConfig++;
                }

                report.Bindings.Add(row);
            }

            PrototypeOnlyOwnerScan.Collect(boundKeys, report);

            IReadOnlyList<PrototypeOnlyDeclaration> declarations = PrototypeOnlyCensus.Declarations;
            for (int i = 0; i < declarations.Count; i++)
            {
                PrototypeOnlyDeclaration declaration = declarations[i];
                if (boundKeys.Contains(declaration.Key))
                {
                    continue;
                }

                report.Unbound.Add(declaration.Key);
            }

            report.DeclaredOutsideConfig = declarations.Count - report.BoundInsideConfig;
            return report;
        }

        private static void Walk(object owner, string pathPrefix, string groupPath, int depth,
            List<ConfigFieldBinding> result)
        {
            if (owner == null || depth > MaxDepth)
            {
                return;
            }

            Type type = owner.GetType();
            while (type != null && type != typeof(ScriptableObject) && type != typeof(object))
            {
                FieldInfo[] fields = type.GetFields(Flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    TryAdd(owner, fields[i], pathPrefix, groupPath, depth, result);
                }

                type = type.BaseType;
            }
        }

        private static void TryAdd(object owner, FieldInfo field, string pathPrefix, string groupPath, int depth,
            List<ConfigFieldBinding> result)
        {
            if (field.IsStatic || field.IsNotSerialized)
            {
                return;
            }

            if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
            {
                return;
            }

            if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
            {
                return;
            }

            string niceName = Prettify(field.Name);
            string path = string.IsNullOrEmpty(pathPrefix) ? niceName : pathPrefix + "." + niceName;
            Type fieldType = field.FieldType;

            if (IsLeaf(fieldType))
            {
                var range = field.GetCustomAttribute<RangeAttribute>();
                result.Add(new ConfigFieldBinding
                {
                    Path = path,
                    DisplayName = niceName,
                    GroupPath = string.IsNullOrEmpty(groupPath) ? "General" : groupPath,
                    Field = field,
                    Owner = owner,
                    FieldType = fieldType,
                    Origin = field.GetCustomAttribute<PrototypeOnlyAttribute>(),
                    HasRange = range != null,
                    RangeMin = range != null ? range.min : 0f,
                    RangeMax = range != null ? range.max : 1f,
                    Module = ConfigModule,
                    OwnerLabel = owner.GetType().Name,
                    DeclarationKey = PrototypeOnlyCensus.KeyOf(field)
                });
                return;
            }

            object value = field.GetValue(owner);
            if (value == null)
            {
                return;
            }

            if (value is IList list && !(value is string))
            {
                Type elementType = GetElementType(fieldType);
                if (elementType == null || !IsSerializableClass(elementType))
                {
                    return;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    object element = list[i];
                    if (element == null)
                    {
                        continue;
                    }

                    string label = DescribeElement(element, i);
                    Walk(element, path + "[" + label + "]", path + " / " + label, depth + 1, result);
                }

                return;
            }

            if (IsSerializableClass(fieldType))
            {
                Walk(value, path, path, depth + 1, result);
            }
        }

        internal static bool IsLeaf(Type t)
        {
            return t == typeof(float)
                   || t == typeof(int)
                   || t == typeof(bool)
                   || t == typeof(string)
                   || t.IsEnum
                   || t == typeof(Color)
                   || t == typeof(Vector2)
                   || t == typeof(Vector3);
        }

        internal static bool IsSerializableClass(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
            {
                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                return false;
            }

            return t.IsClass && t.GetCustomAttribute<SerializableAttribute>() != null;
        }

        internal static Type GetElementType(Type listType)
        {
            if (listType.IsArray)
            {
                return listType.GetElementType();
            }

            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>))
            {
                return listType.GetGenericArguments()[0];
            }

            return null;
        }

        /// <summary>Prefer a DisplayName / Id field for the element caption, else the index.</summary>
        internal static string DescribeElement(object element, int index)
        {
            Type t = element.GetType();

            FieldInfo display = t.GetField("DisplayName");
            if (display != null && display.FieldType == typeof(string))
            {
                var s = display.GetValue(element) as string;
                if (!string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }

            FieldInfo id = t.GetField("Id");
            if (id != null)
            {
                object v = id.GetValue(element);
                if (v != null)
                {
                    return v.ToString();
                }
            }

            return index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"_moveSpeed" / "MoveSpeed" -> "Move Speed".</summary>
        internal static string Prettify(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return fieldName;
            }

            int start = 0;
            while (start < fieldName.Length && (fieldName[start] == '_' || fieldName[start] == 'm' && start == 0 && fieldName.Length > 1 && fieldName[1] == '_'))
            {
                start++;
            }

            var sb = new StringBuilder(fieldName.Length + 6);
            bool first = true;
            for (int i = start; i < fieldName.Length; i++)
            {
                char ch = fieldName[i];
                if (first)
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    first = false;
                    continue;
                }

                if (char.IsUpper(ch) && i > start && !char.IsUpper(fieldName[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }
    }
}
