using System;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>
    /// One declared <see cref="PrototypeOnlyAttribute"/> field, found by scanning types
    /// rather than objects. A declaration exists whether or not anything is alive to
    /// hold it, which is exactly why it is the denominator for the coverage counters.
    /// </summary>
    public sealed class PrototypeOnlyDeclaration
    {
        public Type DeclaringType;
        public FieldInfo Field;
        public PrototypeOnlyAttribute Origin;

        /// <summary>const or static: readable without an instance, so it can never go missing.</summary>
        public bool IsStatic;

        /// <summary>const: readable, never writable.</summary>
        public bool IsConstant;

        /// <summary>Coarse bucket for the F1 panel: Config, Weapons, Player, Crowd, Spawn, ...</summary>
        public string Module;

        /// <summary>Stable identity of the declaration, independent of any instance.</summary>
        public string Key;
    }

    /// <summary>
    /// Type-level census of every <see cref="PrototypeOnlyAttribute"/> field in the loaded
    /// prototype assemblies.
    ///
    /// Why a census at all: <see cref="ConfigFieldIndex"/> can only show a value it can
    /// reach through a live object. Before this class existed, a value nobody could reach
    /// was simply absent - no row, no count, no complaint. The census is the independent
    /// denominator: it knows how many tagged fields the code declares, so the index can
    /// report how many of them it failed to bind instead of losing them quietly.
    ///
    /// The scan walks types once and caches. It is never valid to call this per frame.
    /// </summary>
    public static class PrototypeOnlyCensus
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static
                                                                 | BindingFlags.Public | BindingFlags.NonPublic
                                                                 | BindingFlags.DeclaredOnly;

        private static List<PrototypeOnlyDeclaration> s_declarations;
        private static Dictionary<string, PrototypeOnlyDeclaration> s_byKey;

        private static int s_invented;
        private static int s_withoutPendingDecision;
        private static int s_staticCount;
        private static int s_assembliesScanned;
        private static int s_typesScanned;

        /// <summary>Every tagged field the loaded prototype assemblies declare.</summary>
        public static IReadOnlyList<PrototypeOnlyDeclaration> Declarations
        {
            get
            {
                EnsureScanned();
                return s_declarations;
            }
        }

        /// <summary>Total declared tagged fields.</summary>
        public static int Total
        {
            get
            {
                EnsureScanned();
                return s_declarations.Count;
            }
        }

        /// <summary>Declared fields whose number was invented outright.</summary>
        public static int Invented
        {
            get
            {
                EnsureScanned();
                return s_invented;
            }
        }

        /// <summary>Tagged, but occupying <see cref="PendingDecisions.None"/> - no open planning line.</summary>
        public static int WithoutPendingDecision
        {
            get
            {
                EnsureScanned();
                return s_withoutPendingDecision;
            }
        }

        /// <summary>const / static declarations. These bind without an instance.</summary>
        public static int StaticCount
        {
            get
            {
                EnsureScanned();
                return s_staticCount;
            }
        }

        public static int AssembliesScanned
        {
            get
            {
                EnsureScanned();
                return s_assembliesScanned;
            }
        }

        public static int TypesScanned
        {
            get
            {
                EnsureScanned();
                return s_typesScanned;
            }
        }

        public static PrototypeOnlyDeclaration Find(string key)
        {
            EnsureScanned();
            return s_byKey.TryGetValue(key, out PrototypeOnlyDeclaration d) ? d : null;
        }

        /// <summary>Identity used to match a declaration against a bound field.</summary>
        public static string KeyOf(FieldInfo field)
        {
            if (field == null)
            {
                return string.Empty;
            }

            Type owner = field.DeclaringType;
            return (owner != null ? owner.FullName : "?") + "." + field.Name;
        }

        /// <summary>Drop the cache. Only needed if assemblies are loaded after the first scan.</summary>
        public static void Invalidate()
        {
            s_declarations = null;
            s_byKey = null;
        }

        private static void EnsureScanned()
        {
            if (s_declarations != null)
            {
                return;
            }

            s_declarations = new List<PrototypeOnlyDeclaration>(512);
            s_byKey = new Dictionary<string, PrototypeOnlyDeclaration>(512);
            s_invented = 0;
            s_withoutPendingDecision = 0;
            s_staticCount = 0;
            s_assembliesScanned = 0;
            s_typesScanned = 0;

            Assembly attributeAssembly = typeof(PrototypeOnlyAttribute).Assembly;
            string attributeAssemblyName = attributeAssembly.GetName().Name;
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < loaded.Length; i++)
            {
                Assembly assembly = loaded[i];
                if (!MayUseAttribute(assembly, attributeAssembly, attributeAssemblyName))
                {
                    continue;
                }

                s_assembliesScanned++;
                ScanAssembly(assembly);
            }
        }

        /// <summary>
        /// Only assemblies that reference the attribute's assembly can carry the attribute.
        /// This is a hard fact of the reference graph, not a naming convention, so a renamed
        /// or newly added prototype assembly is picked up without touching this file.
        /// </summary>
        private static bool MayUseAttribute(Assembly assembly, Assembly attributeAssembly, string attributeAssemblyName)
        {
            if (assembly == attributeAssembly)
            {
                return true;
            }

            if (assembly.IsDynamic)
            {
                return false;
            }

            AssemblyName[] referenced;
            try
            {
                referenced = assembly.GetReferencedAssemblies();
            }
            catch (Exception)
            {
                return false;
            }

            for (int i = 0; i < referenced.Length; i++)
            {
                if (string.Equals(referenced[i].Name, attributeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ScanAssembly(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // A half-loaded assembly still has usable types; take what resolved.
                types = e.Types ?? Array.Empty<Type>();
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                s_typesScanned++;
                ScanType(type);
            }
        }

        private static void ScanType(Type type)
        {
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(Flags);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                PrototypeOnlyAttribute origin;
                try
                {
                    origin = field.GetCustomAttribute<PrototypeOnlyAttribute>();
                }
                catch (Exception)
                {
                    continue;
                }

                if (origin == null)
                {
                    continue;
                }

                var declaration = new PrototypeOnlyDeclaration
                {
                    DeclaringType = type,
                    Field = field,
                    Origin = origin,
                    IsStatic = field.IsStatic,
                    IsConstant = field.IsLiteral,
                    Module = ModuleFor(type),
                    Key = KeyOf(field)
                };

                if (s_byKey.ContainsKey(declaration.Key))
                {
                    continue;
                }

                s_declarations.Add(declaration);
                s_byKey.Add(declaration.Key, declaration);

                if (origin.Invented)
                {
                    s_invented++;
                }

                if (origin.PendingDecision == PendingDecisions.None)
                {
                    s_withoutPendingDecision++;
                }

                if (declaration.IsStatic)
                {
                    s_staticCount++;
                }
            }
        }

        /// <summary>
        /// Namespace -> panel module. Everything unknown falls back to the last namespace
        /// segment, so a new system shows up under its own heading without an edit here.
        /// </summary>
        public static string ModuleFor(Type type)
        {
            string ns = type != null ? type.Namespace : null;
            if (string.IsNullOrEmpty(ns))
            {
                return "Misc";
            }

            switch (ns)
            {
                case "DD.Prototype.Core.Config": return "Config";
                case "DD.Prototype.PlayerSystem": return "Player";
                case "DD.Prototype.Navigation": return "Crowd";
                case "DD.Prototype.CameraSystem": return "Camera";
                case "DD.Prototype.Inputs": return "Input";
            }

            int dot = ns.LastIndexOf('.');
            return dot >= 0 && dot < ns.Length - 1 ? ns.Substring(dot + 1) : ns;
        }
    }
}
