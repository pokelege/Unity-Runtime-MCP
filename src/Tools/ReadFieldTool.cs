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
    public static class ReadFieldTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "read_field",
                Handle,
                "Reads the value of a field or property. Supports dot-notation for nested access (e.g. 'transform.parent.name'). Can read static members by omitting instance_id and specifying class_name.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Optional: Instance ID of the object. Omit if reading a static field/property." },
                        class_name = new { type = "string", description = "Optional: Full class/type name. Required if instance_id is omitted (for static fields/properties)." },
                        name = new { type = "string", description = "Name of the field or property to read. Can be a nested path using dot notation." }
                    },
                    required = new[] { "name" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("name", out var nameProp)) throw new Exception("Missing parameter: name");
            string name = nameProp.GetString();

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

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    if (i == 0 && targetObj == null)
                    {
                        currentObj = GetStaticFieldValue(targetType, part);
                    }
                    else
                    {
                        if (currentObj == null) return null;

                        Type type;
                        object target;

                        if (currentObj is UnityEngine.Object uo)
                        {
                            if (uo == null) return null; // Handle destroyed objects
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

                return currentObj.ToMcpValue();
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
    }
}
