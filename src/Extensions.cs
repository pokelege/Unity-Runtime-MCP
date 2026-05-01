// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace PokeLege.UnityRuntimeMCP
{
    public static class UnityObjectExtensions
    {
        /// <summary>
        /// Gets the actual runtime System.Type of a Unity object in IL2CPP.
        /// This is necessary because obj.GetType() often returns the base UnityEngine.Object type.
        /// </summary>
        public static Type GetRuntimeType(this UnityEngine.Object obj)
        {
            if (obj == null) return null;
            
            try
            {
                // Get the IL2CPP internal type
                var il2cppType = obj.GetIl2CppType();
                
                // Convert to a managed System.Type using its name
                // Some versions of Il2CppInterop have better ways, but this is a reliable fallback
                var managedType = Type.GetType(il2cppType.AssemblyQualifiedName);
                if (managedType != null) return managedType;

                // Try to find the type by FullName if AssemblyQualifiedName fails
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType(il2cppType.FullName);
                    if (type != null) return type;
                }

                return obj.GetType();
            }
            catch (Exception)
            {
                return obj.GetType();
            }
        }

        /// <summary>
        /// Casts a Unity object to its actual runtime managed type.
        /// This ensures that reflection calls match the target object's type.
        /// </summary>
        public static object CastToRuntimeType(this UnityEngine.Object obj)
        {
            if (obj == null) return null;
            var type = obj.GetRuntimeType();
            if (type == null || type == typeof(UnityEngine.Object)) return obj;

            try
            {
                // Use reflection to call the generic Cast<T> method from Il2CppObjectBase
                var castMethod = obj.GetType().GetMethod("Cast", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (castMethod != null)
                {
                    var genericCast = castMethod.MakeGenericMethod(type);
                    return genericCast.Invoke(obj, null);
                }
                return obj;
            }
            catch (Exception)
            {
                return obj;
            }
        }
    }
}
