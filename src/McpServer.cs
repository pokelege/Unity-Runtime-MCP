// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;

namespace PokeLege.UnityRuntimeMCP
{
    public static class McpServer
    {
        private static HttpListener _listener;
        private static bool _isRunning;
        private static ManualLogSource _logger;
        private static readonly List<HttpListenerResponse> _sseConnections = new List<HttpListenerResponse>();
        private static readonly Dictionary<string, ToolInfo> _tools = new Dictionary<string, ToolInfo>();

        public class ToolInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public object InputSchema { get; set; }
            public Func<JsonElement, Task<object>> Handler { get; set; }
        }

        public static void RegisterTool(string name, Func<JsonElement, Task<object>> handler, string description = null, object schema = null)
        {
            _tools[name] = new ToolInfo
            {
                Name = name,
                Description = description ?? $"Unity tool: {name}",
                InputSchema = schema ?? new { type = "object", properties = new { } },
                Handler = handler
            };
        }

        public static void Start(string host, int port, ManualLogSource logger)
        {
            _logger = logger;
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://{host}:{port}/");
                _listener.Start();
                _isRunning = true;

                Task.Run(ListenLoop);
                _logger.LogInfo($"MCP Server started on http://{host}:{port}/");
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                _logger.LogError($"Access Denied starting MCP Server on {host}:{port}. " +
                                "Try changing 'Host' to 'localhost' in the config, or run the game as Administrator.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start MCP Server: {ex.Message}");
            }
        }

        private static async Task ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleRequest(context);
                }
                catch (Exception ex)
                {
                    if (_isRunning) _logger.LogError($"Server error: {ex}");
                }
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                _logger.LogDebug($"Incoming {request.HttpMethod} request to {request.Url.AbsolutePath}");

                // Add CORS headers to all responses
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }

                if (request.HttpMethod == "GET")
                {
                    string path = request.Url.AbsolutePath;
                    if (path == "/" || path == "/index.html")
                    {
                        await ServeStaticFile(response, "index.html", "text/html");
                        return;
                    }
                    if (path == "/style.css")
                    {
                        await ServeStaticFile(response, "style.css", "text/css");
                        return;
                    }
                    if (path == "/app.js")
                    {
                        await ServeStaticFile(response, "app.js", "application/javascript");
                        return;
                    }
                    if (path.StartsWith("/assets/") && path.EndsWith(".svg"))
                    {
                        string fileName = System.IO.Path.GetFileName(path);
                        await ServeStaticFile(response, fileName, "image/svg+xml");
                        return;
                    }
                }

                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/mcp")
                {
                    _logger.LogDebug("Handling SSE connection request");
                    await HandleSseRequest(context);
                    return;
                }

                if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/mcp")
                {
                    _logger.LogDebug("Handling JSON-RPC POST request");
                    await HandleJsonRpcRequest(context);
                    return;
                }

                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Request error ({request.HttpMethod} {request.Url.AbsolutePath}): {ex}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                if (response.OutputStream.CanWrite && !IsSseResponse(response))
                {
                    response.Close();
                }
            }
        }

        private static async Task ServeStaticFile(HttpListenerResponse response, string resourceSuffix, string contentType)
        {
            try
            {
                var assembly = typeof(McpServer).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    return;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    return;
                }

                response.ContentType = contentType;
                response.StatusCode = (int)HttpStatusCode.OK;

                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to serve static file {resourceSuffix}: {ex}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }

        private static bool IsSseResponse(HttpListenerResponse response)
        {
            return response.ContentType == "text/event-stream";
        }

        private static async Task HandleSseRequest(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            response.StatusCode = (int)HttpStatusCode.OK;

            lock (_sseConnections)
            {
                _sseConnections.Add(response);
            }

            // Send endpoint event (required by many MCP clients)
            var endpointMsg = "event: endpoint\ndata: /mcp\n\n";
            var buffer = Encoding.UTF8.GetBytes(endpointMsg);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            await response.OutputStream.FlushAsync();

            _logger.LogInfo("New SSE connection established.");

            // Keep the connection open
            while (_isRunning && response.OutputStream.CanWrite)
            {
                await Task.Delay(1000);
            }

            lock (_sseConnections)
            {
                _sseConnections.Remove(response);
            }
        }

        private static async Task HandleJsonRpcRequest(HttpListenerContext context)
        {
            JsonRpcRequest request = null;
            object requestId = null;

            try
            {
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();

                _logger.LogDebug($"Incoming MCP request: {body}");

                try 
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(body);
                    if (request != null && request.Id.ValueKind != JsonValueKind.Undefined)
                    {
                        requestId = request.Id;
                    }
                }
                catch (Exception)
                {
                    // If full deserialization fails, try to extract ID at least
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("id", out var idProp))
                        {
                            requestId = idProp.Clone();
                        }
                    }
                    catch { /* Ignore parse errors here, requestId stays null */ }
                }

                if (request == null) throw new Exception("Invalid JSON-RPC request");

                object result = null;
                if (request.Method == "initialize")
                {
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            tools = new { listChanged = false },
                            logging = new { }
                        },
                        serverInfo = new
                        {
                            name = "UnityRuntimeMCP",
                            version = "1.0.0"
                        }
                    };
                }
                else if (request.Method == "notifications/initialized" || request.Method == "notifications/cancelled")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }
                else if (request.Method == "tools/list")
                {
                    _logger.LogDebug($"Listing {_tools.Count} tools");
                    result = new
                    {
                        tools = _tools.Values.Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            inputSchema = t.InputSchema
                        }).ToList()
                    };
                }
                else if (request.Method == "tools/call")
                {
                    if (request.Params.TryGetProperty("name", out var toolNameProp))
                    {
                        var toolName = toolNameProp.GetString();
                        _logger.LogDebug($"Calling tool: {toolName}");
                        if (_tools.TryGetValue(toolName, out var tool))
                        {
                            request.Params.TryGetProperty("arguments", out var args);
                            _logger.LogDebug($"Tool arguments: {args}");
                            var toolResult = await tool.Handler(args);
                            _logger.LogDebug($"Tool {toolName} execution completed");
                            
                            result = new
                            {
                                content = new[]
                                {
                                    new
                                    {
                                        type = "text",
                                        text = JsonSerializer.Serialize(toolResult, new JsonSerializerOptions { WriteIndented = true })
                                    }
                                }
                            };
                        }
                        else
                        {
                            _logger.LogWarning($"Tool not found: {toolName}");
                            throw new Exception($"Tool not found: {toolName}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Invalid tools/call: missing 'name'");
                        throw new Exception("Invalid tools/call: missing 'name'");
                    }
                }
                else if (_tools.TryGetValue(request.Method, out var oldHandler))
                {
                    result = await oldHandler.Handler(request.Params);
                }
                else if (request.Method == "mcp.list_tools")
                {
                    result = new { tools = _tools.Keys };
                }
                else
                {
                    throw new Exception($"Method not found: {request.Method}");
                }

                if (requestId == null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }

                var response = new JsonRpcResponse
                {
                    Id = requestId,
                    Result = result
                };

                var jsonResponse = JsonSerializer.Serialize(response);
                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                _logger.LogDebug($"Sending JSON-RPC response ({buffer.Length} bytes)");
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                _logger.LogDebug("JSON-RPC response sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"JSON-RPC error: {ex}");
                
                var errorResponse = new JsonRpcResponse
                {
                    Id = requestId,
                    Error = new JsonRpcError { Message = ex.Message }
                };
                
                var jsonResponse = JsonSerializer.Serialize(errorResponse);
                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        public class JsonRpcRequest
        {
            [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
            [JsonPropertyName("method")] public string Method { get; set; }
            [JsonPropertyName("params")] public JsonElement Params { get; set; }
            [JsonPropertyName("id")] public JsonElement Id { get; set; }
        }

        public class JsonRpcResponse
        {
            [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";

            [JsonPropertyName("result")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object Result { get; set; }

            [JsonPropertyName("error")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public JsonRpcError Error { get; set; }

            [JsonPropertyName("id")]
            public object Id { get; set; }
        }

        public class JsonRpcError
        {
            [JsonPropertyName("code")] public int Code { get; set; } = -32000;
            [JsonPropertyName("message")] public string Message { get; set; }
        }

        public static void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            lock (_sseConnections)
            {
                foreach (var conn in _sseConnections)
                {
                    try { conn.Close(); } catch { }
                }
                _sseConnections.Clear();
            }
        }
    }
}
