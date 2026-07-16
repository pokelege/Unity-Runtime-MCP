// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using PokeLege.UnityRuntimeMCP;
using Object = UnityEngine.Object;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class WriteFieldTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "write_field",
                Handle,
                "Writes a value to a field or property. Supports dot-notation for nested access (e.g. 'transform.position.x'). Can write to static fields/properties by omitting instance_id and specifying class_name.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Optional: Instance ID of the object. Omit if writing a static field/property." },
                        class_name = new { type = "string", description = "Optional: Full class/type name. Required if instance_id is omitted (for static fields/properties)." },
                        name = new { type = "string", description = "Name of the field or property to write. Can be a nested path using dot notation." },
                        value = new { type = "string", description = "Value to write (as string, will be converted to the target field's type)." }
                    },
                    required = new[] { "name", "value" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("name", out var nameProp)) throw new Exception("Missing parameter: name");
            if (!parameters.TryGetProperty("value", out var valueProp)) throw new Exception("Missing parameter: value");

            string name = nameProp.GetString();
            string valueStr = valueProp.GetString();

            int instanceId = 0;
            if (parameters.TryGetProperty("instance_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                instanceId = idProp.GetInt32();
            }

            string className = null;
            if (parameters.TryGetProperty("class_name", out var classNameProp) && classNameProp.ValueKind == JsonValueKind.String)
            {
                className = classNameProp.GetString();
            }

            if (instanceId == 0 && string.IsNullOrEmpty(className))
            {
                throw new Exception("Must provide either instance_id or class_name");
            }

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                Type targetType = null;
                object targetObj = null;

                if (instanceId != 0)
                {
                    var obj = UnityObjectExtensions.FindObjectById(instanceId);
                    if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                    if (obj is UnityEngine.Object uo)
                    {
                        targetType = uo.GetRuntimeType();
                        targetObj = uo.CastToRuntimeType();
                    }
                    else
                    {
                        targetType = obj.GetType();
                        targetObj = obj;
                    }
                }
                else
                {
                    targetType = UnityObjectExtensions.ResolveType(className);
                    if (targetType == null) throw new Exception($"Type '{className}' not found.");
                }

                string[] parts = name.Split('.');
                object currentObj = targetObj;

                // Traverse to the second to last part
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string part = parts[i];
                    if (i == 0 && targetObj == null)
                    {
                        currentObj = GetStaticFieldValue(targetType, part);
                    }
                    else
                    {
                        if (currentObj == null) throw new Exception($"Path '{name}' is broken at '{part}' because it is null.");

                        Type type;
                        object target;

                        if (currentObj is UnityEngine.Object uo)
                        {
                            if (uo == null) throw new Exception($"Path '{name}' is broken at '{part}' because the Unity object is destroyed.");
                            type = uo.GetRuntimeType();
                            target = uo.CastToRuntimeType();
                        }
                        else
                        {
                            type = currentObj.GetType();
                            target = currentObj;
                        }

                        currentObj = GetInstanceFieldValue(target, type, part);
                    }
                }

                string finalName = parts.Last();
                if (parts.Length == 1 && targetObj == null)
                {
                    SetStaticFieldValue(targetType, finalName, valueStr);
                }
                else
                {
                    if (currentObj == null) throw new Exception($"Path '{name}' is broken before the final field '{finalName}' because the parent is null.");

                    Type finalType;
                    object finalTarget;

                    if (currentObj is UnityEngine.Object finalUo)
                    {
                        finalType = finalUo.GetRuntimeType();
                        finalTarget = finalUo.CastToRuntimeType();
                    }
                    else
                    {
                        finalType = currentObj.GetType();
                        finalTarget = currentObj;
                    }

                    SetInstanceFieldValue(finalTarget, finalType, finalName, valueStr);
                }
                return "OK";
            });
        }

        private static object GetStaticFieldValue(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(null);

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop != null) return prop.GetValue(null);

            throw new Exception($"Static field or property '{name}' not found on type {type.FullName}");
        }

        private static object GetInstanceFieldValue(object obj, Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(obj);

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null) return prop.GetValue(obj);

            throw new Exception($"Instance field or property '{name}' not found on type {type.FullName}");
        }

        private static void SetStaticFieldValue(Type type, string name, string value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(null, ConvertValue(value, field.FieldType));
                return;
            }

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                prop.SetValue(null, ConvertValue(value, prop.PropertyType));
                return;
            }

            throw new Exception($"Static field or property '{name}' not found on type {type.FullName}");
        }

        private static void SetInstanceFieldValue(object obj, Type type, string name, string value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(obj, ConvertValue(value, field.FieldType));
                return;
            }

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                prop.SetValue(obj, ConvertValue(value, prop.PropertyType));
                return;
            }

            throw new Exception($"Instance field or property '{name}' not found on type {type.FullName}");
        }

        private static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(string)) return value;

            if (targetType == typeof(Type) || targetType.FullName == "Il2CppSystem.Type")
            {
                var resolvedType = UnityObjectExtensions.ResolveTypeForMethod(value, targetType);
                if (resolvedType == null) throw new Exception($"Could not resolve type: {value}");
                return resolvedType;
            }

            return Convert.ChangeType(value, targetType);
        }
    }
}
