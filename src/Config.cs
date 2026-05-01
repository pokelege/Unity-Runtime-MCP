// Copyright (c) 2026 PokeLege
// SPDX-License-Identifier: LGPL-2.1-only
using BepInEx.Configuration;

namespace PokeLege.UnityRuntimeMCP
{
    public static class PluginConfig
    {
        public static ConfigEntry<int> Port { get; private set; }
        public static ConfigEntry<string> Host { get; private set; }

        public static void Init(ConfigFile config)
        {
            Port = config.Bind("General", "Port", 3000, "The port the MCP server will listen on.");
            Host = config.Bind("General", "Host", "127.0.0.1", "The host the MCP server will bind to. Use 'localhost' for local access only, or '*' to listen on all interfaces (requires Admin).");
        }
    }
}
