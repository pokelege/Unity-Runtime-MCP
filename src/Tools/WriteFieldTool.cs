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
                "Writes a value to a field or property of a specific Unity object.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object." },
                        name = new { type = "string", description = "Name of the field or property to write." },
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
                var obj = Object.FindObjectsOfType<Object>().FirstOrDefault(o => o.GetInstanceID() == instanceId);
                if (obj == null) throw new Exception("Object not found.");

                var type = obj.GetRuntimeType();
                var typedObj = obj.CastToRuntimeType();
                
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(typedObj, ConvertValue(valueStr, field.FieldType));
                    return "OK";
                }

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(typedObj, ConvertValue(valueStr, prop.PropertyType));
                    return "OK";
                }

                throw new Exception($"Field or property '{name}' not found on type {type.FullName}");
            });
        }

        private static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(string)) return value;
            // Add more types as needed or use a generic converter
            return Convert.ChangeType(value, targetType);
        }
    }
}
