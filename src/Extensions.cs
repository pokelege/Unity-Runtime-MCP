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

        /// <summary>
        /// Resolves a string type name to a System.Type across all loaded assemblies.
        /// </summary>
        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            var type = Type.GetType(typeName);
            if (type != null) return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// Resolves a string type name to an object compatible with the target parameter type (System.Type or Il2CppSystem.Type).
        /// </summary>
        public static object ResolveTypeForMethod(string typeName, Type targetParameterType)
        {
            var managedType = ResolveType(typeName);
            if (managedType == null) return null;

            if (targetParameterType.FullName == "Il2CppSystem.Type")
            {
                try
                {
                    // Use Il2CppInterop to convert System.Type to Il2CppSystem.Type
                    return Il2CppInterop.Runtime.Il2CppType.From(managedType);
                }
                catch
                {
                    // Fallback to managed type if conversion fails
                    return managedType;
                }
            }

            return managedType;
        }

        /// <summary>
        /// Converts a value to an MCP-friendly format. 
        /// If the value is a Unity Object, it returns an object with identity information.
        /// </summary>
        public static object ToMcpValue(this object value)
        {
            if (value == null) return null;

            if (value is UnityEngine.Object unityObj)
            {
                if (unityObj == null) return null; // Handle destroyed objects
                return new
                {
                    instance_id = unityObj.GetInstanceID(),
                    name = unityObj.name,
                    type = unityObj.GetRuntimeType()?.FullName ?? unityObj.GetType().FullName
                };
            }

            // Handle non-serializable types
            if (value is IntPtr || value is UIntPtr) return value.ToString();

            // If it's a primitive type, string, or common serializable type, return as is
            var type = value.GetType();
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return value;

            // For other types, return ToString() to prevent serialization errors
            return value.ToString();
        }
    }
}
