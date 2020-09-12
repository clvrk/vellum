using System;
using System.Collections.Generic;

namespace Vellum.Extension
{
    public interface IPlugin
    {
        // public Version Version { get; }
        public PluginType PluginType { get; }
        public delegate void HookHandler(object sender, EventArgs e);

        public void Initialize(IHost host);
        public void Unload();
        public void RegisterHook(byte id, HookHandler callback);
        public Dictionary<byte, string> GetHooks();
        // public Dictionary<string, object> GetDefaultRunConfig();
    }

    public enum PluginType
    {
        INTERNAL,
        EXTERNAL
    }
}