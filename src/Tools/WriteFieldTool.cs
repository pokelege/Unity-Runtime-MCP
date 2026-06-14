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
                "Writes a value to a field or property. Supports dot-notation for nested access (e.g. 'transform.position.x').",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object." },
                        name = new { type = "string", description = "Name of the field or property to write. Can be a nested path using dot notation. Suggestion: prefer property setters over private backing fields to ensure side-effects (e.g. layout/graphic updates) execute, though direct field writes are acceptable if bypassing setters is explicitly intended." },
                        value = new { type = "string", description = "Value to write (as string, will be converted to the field's type)." }
                    },
                    required = new[] { "instance_id", "name", "value" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("instance_id", out var idProp)) throw new Exception("Missing parameter: instance_id");
            if (!parameters.TryGetProperty("name", out var nameProp)) throw new Exception("Missing parameter: name");
            if (!parameters.TryGetProperty("value", out var valueProp)) throw new Exception("Missing parameter: value");

            int instanceId = idProp.GetInt32();
            string name = nameProp.GetString();
            string valueStr = valueProp.GetString();

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = UnityObjectExtensions.FindObjectById(instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                string[] parts = name.Split('.');
                object currentObj = obj;

                // Traverse to the second to last part
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string part = parts[i];
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

                    currentObj = GetFieldValue(target, type, part);
                }

                if (currentObj == null) throw new Exception($"Path '{name}' is broken before the final field '{parts.Last()}' because the parent is null.");

                string finalName = parts.Last();
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

                SetFieldValue(finalTarget, finalType, finalName, valueStr);
                return "OK";
            });
        }

        private static object GetFieldValue(object obj, Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(obj);

            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null) return prop.GetValue(obj);

            throw new Exception($"Field or property '{name}' not found on type {type.FullName}");
        }

        private static void SetFieldValue(object obj, Type type, string name, string value)
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

            throw new Exception($"Field or property '{name}' not found on type {type.FullName}");
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
