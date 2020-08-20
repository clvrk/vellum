using System;
using System.Collections.Generic;

namespace Vellum.Extension
{
    public interface IHost
    {
        public Version Version { get; }
        
        public static RunConfiguration RunConfig { get; }
        public uint LoadPlugins(string dir);
        public void AddPlugin(IPlugin plugin);
        public List<IPlugin> GetPlugins();
        public IPlugin GetPluginByName(string name);
    }
}