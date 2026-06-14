// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Linq;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace PokeLege.UnityRuntimeMCP
{
    public static class UnityObjectCache
    {
        private static readonly System.Collections.Generic.Dictionary<int, WeakReference<object>> _cache = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, BoxedId> _dynamicIds = new();
        private static int _nextDynamicId = 1000000000;

        private class BoxedId
        {
            public int Id { get; set; }
        }

        public static void Register(UnityEngine.Object obj)
        {
            if (obj == null) return;
            int id = obj.GetInstanceID();
            lock (_cache)
            {
                _cache[id] = new WeakReference<object>(obj);
            }
        }

        public static int RegisterNonUnityObject(object obj)
        {
            if (obj == null) return 0;

            lock (_dynamicIds)
            {
                if (_dynamicIds.TryGetValue(obj, out var boxed))
                {
                    return boxed.Id;
                }

                int id = System.Threading.Interlocked.Increment(ref _nextDynamicId);
                var newBoxed = new BoxedId { Id = id };
                _dynamicIds.Add(obj, newBoxed);

                lock (_cache)
                {
                    _cache[id] = new WeakReference<object>(obj);
                }
                return id;
            }
        }

        public static object Get(int id)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(id, out var weakRef) && weakRef.TryGetTarget(out var obj))
                {
                    if (obj != null) return obj;
                }
            }
            return null;
        }
    }

    public static class UnityObjectExtensions
    {
        /// <summary>
        /// Finds a Unity or cached object by its instance ID.
        /// Falls back to FindObjectsOfType if not in cache (only for standard Unity object IDs).
        /// </summary>
        public static object FindObjectById(int instanceId)
        {
            var cached = UnityObjectCache.Get(instanceId);
            if (cached != null) return cached;

            // Fallback to searching all objects
            if (instanceId < 1000000000)
            {
                var obj = UnityEngine.Object.FindObjectsOfType<UnityEngine.Object>().FirstOrDefault(o => o.GetInstanceID() == instanceId);
                if (obj != null)
                {
                    UnityObjectCache.Register(obj);
                    return obj;
                }
            }
            return null;
        }

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
                UnityObjectCache.Register(unityObj);
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

            // Handle collections/arrays (excluding string)
            if (value is System.Collections.IEnumerable enumerable)
            {
                var list = new System.Collections.Generic.List<object>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= 20) break;
                    list.Add(item == null ? null : item.ToMcpValue());
                    count++;
                }
                return list;
            }

            // Handle other reference types (classes)
            if (type.IsClass)
            {
                int id = UnityObjectCache.RegisterNonUnityObject(value);
                return new
                {
                    instance_id = id,
                    name = type.Name,
                    type = type.FullName
                };
            }

            // For other types (e.g. structs like Vector3), return ToString() to prevent serialization errors
            return value.ToString();
        }
    }
}
