// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using PokeLege.UnityRuntimeMCP;
using Object = UnityEngine.Object;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class InspectObjectTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "inspect_object",
                Handle,
                "Inspects a specific Unity object by its instance ID, returning its fields, properties, and methods.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new
                        {
                            type = "integer",
                            description = "The unique instance ID of the Unity object to inspect."
                        },
                        include_methods = new
                        {
                            type = "boolean",
                            description = "Whether to include the methods list in the output (defaults to true)."
                        }
                    },
                    required = new[] { "instance_id" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("instance_id", out var idProp))
                throw new Exception("Missing parameter: instance_id");

            int instanceId = idProp.GetInt32();
            bool includeMethods = !parameters.TryGetProperty("include_methods", out var methodsProp) || methodsProp.GetBoolean();

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = Object.FindObjectsOfType<Object>().FirstOrDefault(o => o.GetInstanceID() == instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                var systemType = obj.GetRuntimeType();
                var typedObj = obj.CastToRuntimeType();

                var fields = systemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => new { name = f.Name, type = f.FieldType.FullName, value = SafeGetValue(f, typedObj) });

                var properties = systemType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(p => new { name = p.Name, type = p.PropertyType.FullName, value = SafeGetPropValue(p, typedObj) });

                object methods = null;
                if (includeMethods)
                {
                    methods = systemType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(m => new { name = m.Name, return_type = m.ReturnType.FullName, parameters = m.GetParameters().Select(p => new { p.Name, p.ParameterType.FullName }) });
                }

                return new
                {
                    instance_id = instanceId,
                    name = obj.name,
                    type = systemType.FullName,
                    fields,
                    properties,
                    methods
                };
            });
        }

        private static object SafeGetValue(FieldInfo f, object obj)
        {
            try { return f.GetValue(obj).ToMcpValue(); }
            catch { return "<error>"; }
        }

        private static object SafeGetPropValue(PropertyInfo p, object obj)
        {
            try { return p.GetValue(obj).ToMcpValue(); }
            catch { return "<error>"; }
        }
    }
}
