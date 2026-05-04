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
                "Invokes a method on a specific Unity object. Supports generic methods (e.g. GetComponent<T>) via type_args.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        instance_id = new { type = "integer", description = "Instance ID of the object to call the method on." },
                        name = new { type = "string", description = "Name of the method to invoke." },
                        args = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: List of arguments for the method (as strings, will be converted to parameter types). Pass empty array if no arguments."
                        },
                        type_args = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: List of type names for generic methods (e.g. ['UnityEngine.Camera'] for GetComponent<Camera>())."
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

            List<string> typeArgs = new List<string>();
            if (parameters.TryGetProperty("type_args", out var typeArgsProp))
            {
                foreach (var arg in typeArgsProp.EnumerateArray()) typeArgs.Add(arg.GetString());
            }

            return await McpMainThreadDispatcher.EnqueueAsync(() =>
            {
                var obj = UnityObjectExtensions.FindObjectById(instanceId);
                if (obj == null) throw new Exception($"Object with ID {instanceId} not found.");

                var type = obj.GetRuntimeType();
                var typedObj = obj.CastToRuntimeType();
                
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == methodName).ToList();

                if (methods.Count == 0) throw new Exception($"Method '{methodName}' not found on type {type.FullName}.");

                // Match parameters and generic arguments
                var method = methods.FirstOrDefault(m => 
                    m.GetParameters().Length == args.Count && 
                    m.GetGenericArguments().Length == typeArgs.Count);

                if (method == null) 
                    throw new Exception($"Method '{methodName}' with {args.Count} parameters and {typeArgs.Count} type arguments not found.");

                if (typeArgs.Count > 0)
                {
                    var resolvedTypeArgs = typeArgs.Select(t => UnityObjectExtensions.ResolveType(t)).ToArray();
                    if (resolvedTypeArgs.Any(t => t == null))
                        throw new Exception($"Could not resolve all type arguments: {string.Join(", ", typeArgs)}");
                    
                    method = method.MakeGenericMethod(resolvedTypeArgs);
                }

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
