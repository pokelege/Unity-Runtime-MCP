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
                "Reads the value of a field or property from a specific Unity object.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object." },
                        name = new { type = "string", description = "Name of the field or property to read." }
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
                var obj = Object.FindObjectsOfType<Object>().FirstOrDefault(o => o.GetInstanceID() == instanceId);
                if (obj == null) throw new Exception("Object not found.");

                var type = obj.GetRuntimeType();
                var typedObj = obj.CastToRuntimeType();
                
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(typedObj)?.ToString() ?? "null";

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetValue(typedObj)?.ToString() ?? "null";

                throw new Exception($"Field or property '{name}' not found on type {type.FullName}");
            });
        }
    }
}
