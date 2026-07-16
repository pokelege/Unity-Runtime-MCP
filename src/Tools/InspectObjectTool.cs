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
                "Inspects a specific Unity object by its instance ID, or a class by its class name (to query static members).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new
                        {
                            type = "integer",
                            description = "Optional: The unique instance ID of the Unity GameObject, Component, or cached object to inspect. Required if class_name is omitted."
                        },
                        class_name = new
                        {
                            type = "string",
                            description = "Optional: The full class/type name to inspect static members of (e.g. 'UnityEngine.Time'). Required if instance_id is omitted."
                        },
                        include_methods = new
                        {
                            type = "boolean",
                            description = "Optional: Whether to include the methods list in the output (defaults to false to save context space)."
                        }
                    }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            int instanceId = 0;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("instance_id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.Number)
                {
                    instanceId = idProp.GetInt32();
                }
            }

            string className = null;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("class_name", out var classProp))
            {
                if (classProp.ValueKind == JsonValueKind.String)
                {
                    className = classProp.GetString();
                }
            }

            if (instanceId == 0 && string.IsNullOrEmpty(className))
                throw new Exception("Missing parameter: either instance_id or class_name must be specified.");

            bool includeMethods = parameters.TryGetProperty("include_methods", out var methodsProp) && methodsProp.GetBoolean();

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                Type systemType = null;
                object typedObj = null;
                string objName = null;

                if (instanceId != 0)
                {
                    var obj = UnityObjectExtensions.FindObjectById(instanceId);
                    if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                    if (obj is UnityEngine.Object unityObj)
                    {
                        systemType = unityObj.GetRuntimeType();
                        typedObj = unityObj.CastToRuntimeType();
                        objName = unityObj.name;
                    }
                    else
                    {
                        systemType = obj.GetType();
                        typedObj = obj;
                        objName = systemType.Name;
                    }
                }
                else
                {
                    systemType = UnityObjectExtensions.ResolveType(className);
                    if (systemType == null) throw new Exception($"Type '{className}' not found.");
                    objName = systemType.Name;
                }

                object fields = null;
                object properties = null;
                object methods = null;
                object components = null;
                bool? activeSelf = null;
                bool? activeInHierarchy = null;

                if (instanceId != 0 && typedObj is GameObject go)
                {
                    activeSelf = go.activeSelf;
                    activeInHierarchy = go.activeInHierarchy;
                    components = go.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => new
                        {
                            instance_id = c.GetInstanceID(),
                            name = c.name,
                            type = c.GetRuntimeType()?.FullName ?? c.GetType().FullName
                        }).ToList();
                    
                    fields = Array.Empty<object>();
                    properties = Array.Empty<object>();
                }
                else
                {
                    var bindingFlags = (instanceId != 0) 
                        ? (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        : (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    fields = systemType.GetFields(bindingFlags)
                        .Select(f => new { name = f.Name, type = f.FieldType.FullName, value = SafeGetValue(f, instanceId != 0 ? typedObj : null) }).ToList();

                    properties = systemType.GetProperties(bindingFlags)
                        .Select(p => new { name = p.Name, type = p.PropertyType.FullName, value = SafeGetPropValue(p, instanceId != 0 ? typedObj : null) }).ToList();

                    if (includeMethods)
                    {
                        methods = systemType.GetMethods(bindingFlags)
                            .Select(m => new { 
                                name = m.Name, 
                                return_type = m.ReturnType.FullName ?? m.ReturnType.Name, 
                                parameters = m.GetParameters().Select(p => new { 
                                    name = p.Name, 
                                    parameter_type = p.ParameterType.FullName ?? p.ParameterType.Name 
                                }) 
                            }).ToList();
                    }
                }

                return new
                {
                    instance_id = instanceId,
                    class_name = instanceId == 0 ? className : null,
                    name = objName,
                    type = systemType.FullName,
                    active_self = activeSelf,
                    active_in_hierarchy = activeInHierarchy,
                    components = components,
                    fields = fields,
                    properties = properties,
                    methods = methods
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
