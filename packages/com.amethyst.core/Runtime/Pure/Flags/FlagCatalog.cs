using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Amethyst.Flags
{
    /// <summary>
    /// Declares every flag up front, so <see cref="FlagState.Validate"/> can tell an unknown
    /// name from a flag whose class nothing has touched yet.
    /// </summary>
    public static class FlagCatalog
    {
        /// <summary>Runs the static initializer of every type in <paramref name="assemblies"/> with a
        /// static <see cref="Flag"/> field.</summary>
        public static void Load(IEnumerable<Assembly> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                foreach (var type in TypesOf(assembly))
                {
                    var declares = type.GetFields(
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                        )
                        .Any(f => typeof(Flag).IsAssignableFrom(f.FieldType));
                    if (declares)
                        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
            }
        }

        /// <summary>This assembly and every loaded one that references it.</summary>
        public static IEnumerable<Assembly> Declaring(IEnumerable<Assembly> loaded)
        {
            var core = typeof(Flag).Assembly;
            var name = core.GetName().Name;
            return loaded.Where(a =>
                a == core || a.GetReferencedAssemblies().Any(r => r.Name == name)
            );
        }

        static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
    }
}
