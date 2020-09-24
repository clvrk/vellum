# Developing plugins for vellum
vellum provides a rather primitive API to interface with internal functionalities or other plugins.

On startup, vellum will load all `*.dll` files in the `plugins` directory relative to the executable. Loading plugins occurs right after initially loading the users configuration, so that plugins have the opportunity to initialize as early as possible.

## Getting started
To develop a plugin you first have to reference the [vellum library](https://github.com/clarkx86/vellum/packages/283543) in your project. To do this, edit your `.csproj` file to include these lines:
```xml
<ItemGroup>
  <Reference Include="Vellum">
    <HintPath>../path/to/library/vellum.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Your project may expose multiple plugins. To create a plugin, create a class and implement the `IPlugin` interface provided in the `Vellum.Extension` namespace. When loading plugins, vellum will look for every class that implements this interface in your assembly, instantiate it and call its `Initialize(IHost host)` method. You should definitely store the `IHost` passed to this method for later reference.

## Interfering with other plugins
Since vellum `v1.3.0`, the `BackupManager` and `RenderManager` classes are *internal* plugins, meaning they provide the same properties and methods defined by the `IPlugin` interface. In this example, we will create a plugin that interferes with `BackupManager` to display "*Hello World*" in the console, upon finishing performing a backup.\
First of all, we need to define our class:
```csharp
using Vellum.Extension;

namespace MyPlugin
{
    public class HelloWorld : IPlugin
    {
        public IHost Host;

        // Implement all the properties provided by the IPlugin interface
    }
}
```

After implementing all the required properties, we need to store the host. Using the `GetPlugins()` method of the host, we get a list of all loaded plugins, internal as well as external ones.
```csharp
public void Initialize(IHost host)
{
    Host = host;

    // Get available plugins
    foreach (IPlugin plugin in host.GetPlugins())
      Console.WriteLine($"{plugin.GetType().Name}\t{plugin.PluginType}");
}
```

This should print the following:
```
BackupManager INTERNAL
RenderManager INTERNAL
HelloWorld    EXTERNAL
```
We could either store our desired plugin by iterating through the list of loaded plugins and checking if the current `plugin.GetType().Name` is the one we want, our use the `GetPluginByName(string name)` method of the host if we know that the plugin exists:
```csharp
public void Initialize(IHost host)
{
    Host = host;

    // Get backup manager
    IPlugin? backupManager = host.GetPluginByName("BackupManager");
}
```
Now we need to hook into a specific event in `BackupManager`'s program flow. To get an overview of available hooks, each implementation of `IPlugin` **must** provide a `GetHooks()` method, which returns a dictionary of hook IDs and their respective names. Under the hood it is recommended to define hooks in an `enum`, but that is up for you to implement.\
To get the hooks available to us, we will write some throw-away code that prints the hook IDs and their names:
```csharp
public void Initialize(IHost host)
{
    Host = host;

    // Get backup manager
    IPlugin? backupManager = host.GetPluginByName("BackupManager");

    if (backupManager != null)
    {
      Dictionary<byte, string> hooks = backupManager.GetHooks();

      foreach (byte k in hooks.Keys)
        Console.WriteLine($"ID: {k}\t\tName: {hooks[k]}");
    }
}
```
This should print the following:
```
ID: 0   Name: BEGIN
ID: 9   Name: END
```
Here we can see that the hook ID for a finished backup is "`9`". Now we can use this ID to hook into this "event":
```csharp
public void Initialize(IHost host)
{
    Host = host;

    // Get backup manager
    IPlugin? backupManager = host.GetPluginByName("BackupManager");

    if (backupManager != null)
    {
      // Register our hook and provide a callback lambda
      backupManager.RegisterHook(9, (object sender, HookEventArgs e) =>
      {
        Console.WriteLine("Hello World!");
      });
    }
}
```
Some hooks also provide `HookEventArgs` when invoked. Because a plugin might need to expose different kinds of objects for its hook attachments, `HookEventArgs` provides a `object? Attachment` property that we need to cast to the type that we expect. To find out what to cast this to, it is recommended for a plugin developer to provide an overview of hook IDs, their intention and what kind of `HookEventArgs` attachment it provides.

---
This should be everything to get you started! In case you want to see a more "hands-on" example, please have a look at the [vellum-git](https://github.com/clarkx86/vellum-git-plugin) plugins source code. If you have any more questions or need help developing a plugin, we welcome you to join our [Discord](https://discord.gg/J2sBaXa).