// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PokeLege.UnityRuntimeMCP.Tools
{
    public static class FindTypesTool
    {
        public static void Register()
        {
            McpServer.RegisterTool(
                "find_types",
                Handle,
                "Searches for loaded System.Types in the AppDomain containing the query string (case-insensitive). Returns matching type full names.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "The substring to search for in type names (e.g., 'Player', 'Controller', 'Camera')."
                        }
                    },
                    required = new[] { "query" }
                }
            );
        }

        private static async Task<object> Handle(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("query", out var queryProp))
                throw new Exception("Missing parameter: query");

            string query = queryProp.GetString();
            if (string.IsNullOrEmpty(query)) return new List<string>();

            // Perform in task to avoid blocking MCP server if AppDomain has many types
            return await Task.Run(() =>
            {
                var matchingTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(asm =>
                    {
                        try
                        {
                            return asm.GetTypes();
                        }
                        catch
                        {
                            return Array.Empty<Type>();
                        }
                    })
                    .Where(t => t != null && t.FullName != null && t.FullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName)
                    .OrderBy(name => name)
                    .Take(100) // Cap to avoid response bloat
                    .ToList();

                return (object)matchingTypes;
            });
        }
    }
}
