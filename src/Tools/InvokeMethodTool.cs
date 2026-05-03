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
    public static class InvokeMethodTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "invoke_method",
                Handle,
                "Invokes a method on a specific Unity object.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object." },
                        name = new { type = "string", description = "Name of the method to invoke." },
                        args = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Arguments for the method (as strings, will be converted to parameter types)."
                        }
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
            string methodName = nameProp.GetString();
            
            List<string> args = new List<string>();
            if (parameters.TryGetProperty("args", out var argsProp))
            {
                foreach (var arg in argsProp.EnumerateArray()) args.Add(arg.GetString());
            }

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = Object.FindObjectsOfType<Object>().FirstOrDefault(o => o.GetInstanceID() == instanceId);
                if (obj == null) throw new Exception("Object not found.");

                var type = obj.GetRuntimeType();
                var typedObj = obj.CastToRuntimeType();
                
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == methodName).ToList();

                if (methods.Count == 0) throw new Exception($"Method '{methodName}' not found.");

                // Match parameters (simple version: match by count)
                var method = methods.FirstOrDefault(m => m.GetParameters().Length == args.Count);
                if (method == null) throw new Exception($"Method '{methodName}' with {args.Count} parameters not found.");

                var methodParams = method.GetParameters();
                object[] convertedArgs = new object[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    convertedArgs[i] = ConvertValue(args[i], methodParams[i].ParameterType);
                }

                var result = method.Invoke(typedObj, convertedArgs);
                return result.ToMcpValue();
            });
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
