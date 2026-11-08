using System.Reflection;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>
/// Closes the plugin folder stack. The public <c>System.CloseFolders</c> command only closes
/// custom folders (they live on a different stack), and the SDK has no "close folder" call, so
/// this resolves the host's folder navigation service through the bridge the host handed us.
/// Everything is best-effort and logged: a future host that changes its internals must not
/// crash the plugin.
/// </summary>
internal static class HostFolders
{
    private const string NavigationTypeName = "LoupixDeck.Services.FolderNavigation.IFolderNavigationService";

    public static void CloseAll(IPluginHost host)
    {
        try
        {
            object? navigation = FindFolderNavigation(host);
            if (navigation is null)
            {
                host.Logger.Warn(
                    "Return to workspace: could not find the host folder navigation service; the folder stays open.");
                return;
            }

            MethodInfo? exitAll = navigation.GetType().GetMethod(
                "ExitAll", BindingFlags.Public | BindingFlags.Instance);

            if (exitAll is null)
            {
                host.Logger.Warn(
                    "Return to workspace: the host folder navigation service has no ExitAll method; the folder stays open.");
                return;
            }

            if (exitAll.Invoke(navigation, null) is Task task)
                task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            host.Logger.Warn($"Return to workspace: closing the plugin folder failed: {ex.Message}");
        }
        finally
        {
            // Public path for custom folders. Run after the plugin stack so the two closers
            // never snap the same stack concurrently.
            host.ExecuteCommand(PlexampSettings.CloseFoldersCommand);
        }
    }

    private static object? FindFolderNavigation(IPluginHost host)
    {
        FieldInfo? executeField = host.GetType().GetField(
            "_executeCommand", BindingFlags.NonPublic | BindingFlags.Instance);

        if (executeField?.GetValue(host) is not Delegate execute || execute.Target is null)
            return null;

        // The delegate is a local function closure inside PluginManager. It holds the manager
        // (directly or as the delegate target); from there Device resolves the per-device
        // service provider that owns the folder navigation service.
        object? manager = FindObjectOfType(execute.Target, "PluginManager");
        if (manager is null)
            return null;

        IServiceProvider? services = GetDeviceServices(manager);
        if (services is null)
            return null;

        Type? navigationType = manager.GetType().Assembly.GetType(NavigationTypeName);
        return navigationType is null ? null : services.GetService(navigationType);
    }

    private static object? FindObjectOfType(object? root, string typeName, int depth = 3)
    {
        if (root is null || depth < 0)
            return null;

        if (root.GetType().Name == typeName)
            return root;

        foreach (FieldInfo field in root.GetType().GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? value;

            try { value = field.GetValue(root); }
            catch { continue; }

            if (value is null || value is string || value.GetType().IsPrimitive)
                continue;

            if (value.GetType().Name == typeName)
                return value;

            // Closures can nest one level (a local function inside a method group).
            object? nested = FindObjectOfType(value, typeName, depth - 1);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static IServiceProvider? GetDeviceServices(object manager)
    {
        Type type = manager.GetType();

        try
        {
            PropertyInfo? device = type.GetProperty(
                "Device", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (device?.GetValue(manager) is IServiceProvider services)
                return services;
        }
        catch
        {
            // Fall through to the router fields.
        }

        FieldInfo? routerField = type.GetField("_router", BindingFlags.Instance | BindingFlags.NonPublic);
        object? router = routerField?.GetValue(manager);
        if (router is null)
            return null;

        try
        {
            PropertyInfo? current = router.GetType().GetProperty("Current");
            if (current?.GetValue(router) is IServiceProvider currentServices)
                return currentServices;

            PropertyInfo? fallback = router.GetType().GetProperty("Default");
            return fallback?.GetValue(router) as IServiceProvider;
        }
        catch
        {
            return null;
        }
    }
}
