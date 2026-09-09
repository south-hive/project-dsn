using System.Reflection;
using System.Runtime.Loader;
using Dsn.Contracts;

namespace Dsn.Host;

public sealed class PluginLoader
{
    private readonly List<AssemblyLoadContext> contexts = [];
    public void Load(string path, IWorkspaceRegistration registry, WorkspaceServices services, bool required = true)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Plugin assembly not found", path);
            var context = new PluginContext(Path.GetFullPath(path)); contexts.Add(context);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var types = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IWorkspacePlugin).IsAssignableFrom(t)).ToArray();
            if (types.Length == 0) throw new InvalidOperationException("No plugin factories found");
            foreach (var type in types)
            {
                var plugin = (IWorkspacePlugin)Activator.CreateInstance(type)!;
                if (plugin.ApiVersion != 1) throw new InvalidOperationException("Unsupported plugin API");
                if (!registry.Register(plugin.Create(services)) && required) throw new InvalidOperationException("Required plugin registration rejected");
            }
        }
        catch (Exception e) { services.Errors.Report(new("PLUGIN_LOAD_FAILED", "host", Detail: e.GetType().Name)); if (required) throw; }
    }
    private sealed class PluginContext(string path) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(IWorkspace).Assembly.GetName().Name) return typeof(IWorkspace).Assembly;
            var dependency = resolver.ResolveAssemblyToPath(name);
            return dependency is null ? null : LoadFromAssemblyPath(dependency);
        }
        protected override nint LoadUnmanagedDll(string name)
        {
            var dependency = resolver.ResolveUnmanagedDllToPath(name);
            return dependency is null ? 0 : LoadUnmanagedDllFromPath(dependency);
        }
    }
}
