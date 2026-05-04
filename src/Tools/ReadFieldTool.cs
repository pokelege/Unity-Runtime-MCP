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
                "Reads the value of a field or property. Supports dot-notation for nested access (e.g. 'transform.parent.name').",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object." },
                        name = new { type = "string", description = "Name of the field or property to read. Can be a nested path using dot notation." }
                    },
                    required = new[] { "instance_id", "name" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("instance_id", out var idProp)) throw new Exception("Missing parameter: instance_id");
            if (!parameters.TryGetProperty("name", out var nameProp)) throw new Exception("Missing parameter: name");

            int instanceId = idProp.GetInt32();
            string name = nameProp.GetString();

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = UnityObjectExtensions.FindObjectById(instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                string[] parts = name.Split('.');
                object currentObj = obj;

                foreach (var part in parts)
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

                    currentObj = GetFieldValue(target, type, part);
                }

                return currentObj.ToMcpValue();
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
    }
}
