using System;
using System.Collections.Generic;

namespace Vellum.Extension
{
    public interface IHost
    {
        public Version Version { get; }
        public RunConfiguration RunConfig { get; }
        public T LoadPluginConfiguration<T>(Type type);
        public string PluginDirectory { get; }
        public void SetPluginDirectory(string dir);
        public uint LoadPlugins();
        public void AddPlugin(IPlugin plugin);
        public List<IPlugin> GetPlugins();
        public IPlugin GetPluginByName(string name);
    }
}