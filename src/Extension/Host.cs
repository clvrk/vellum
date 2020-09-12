using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Vellum.Extension
{
    public class Host : IHost
    {
        public static RunConfiguration RunConfig;
        // Plugin host interface
        public Version Version
        {
            get
            {
                return new Version();
            }
        }
        private List<IPlugin> _activePlugins = new List<IPlugin>();

        public Host(ref RunConfiguration runConfig)
        {
            RunConfig = runConfig;
        }

        public uint LoadPlugins(string dir)
        {
            uint pluginCount = 0;
            foreach (string pluginPath in Directory.GetFiles(dir, "*.dll"))
            {
                System.Console.WriteLine("Loading plugin(s) from \"{0}\"...", pluginPath);
                Assembly pluginAssembly = Assembly.LoadFrom(pluginPath);
                foreach (Type type in pluginAssembly.GetTypes())
                {
                    if (typeof(IPlugin).IsAssignableFrom(type))
                    {
                        if (!RunConfig.Plugins.ContainsKey(type.Name))
                        {
                            RunConfig.Plugins.Add(type.Name, new PluginConfig()
                            {
                                Enable = true,
                                Config = (Dictionary<string, object>)type.GetMethod("GetDefaultRunConfig", BindingFlags.Public | BindingFlags.Static).Invoke(null, null)
                            });
                        }

                        if (RunConfig.Plugins[type.Name].Enable)
                        {
                            IPlugin plugin = (IPlugin)pluginAssembly.CreateInstance(type.FullName);
                            // plugin.PluginType = PluginType.EXTERNAL;
                            _activePlugins.Add(plugin);
                            pluginCount++;
                        }
                    }
                }
            }

            // Initialize loaded plugins
            foreach (IPlugin plugin in _activePlugins)
                plugin.Initialize(this);

            return pluginCount;
        }

        public void AddPlugin(IPlugin plugin)
        {
            _activePlugins.Add(plugin);
            plugin.Initialize(this);
        }

        public List<IPlugin> GetPlugins()
        {
            return _activePlugins;
        }

        public IPlugin GetPluginByName(string name)
        {
            IPlugin plugin = null;

            foreach (IPlugin p in _activePlugins)
            {
                if (p.GetType().Name == name)
                {
                    plugin = p;
                    break;
                }                    
            }

            return plugin;
        }
    }
}