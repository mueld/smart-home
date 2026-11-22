namespace Backend.Kernel.Persistence;

/// <summary>A module and its DbContext; the module owns every request in its root namespace and below.</summary>
public sealed record ModuleRegistration(Type DbContextType, string RootNamespace);

/// <summary>
/// Resolves the DbContext of the module that owns a command, by the namespace of the command
/// (AD-5, AD-14). The Contracts project of a module lives below the module's root namespace
/// (for example Backend.Modules.AirConditioner.Contracts), so its commands belong to the same module.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly ModuleRegistration[] _modules;

    public ModuleRegistry(IEnumerable<ModuleRegistration> registrations)
    {
        _modules = registrations.OrderByDescending(m => m.RootNamespace.Length).ToArray();

        var duplicate = _modules.GroupBy(m => m.RootNamespace).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"The namespace {duplicate.Key} is registered for more than one module.");
        }
    }

    public Type ResolveDbContext(Type requestType)
    {
        var requestNamespace = requestType.Namespace ?? string.Empty;

        foreach (var module in _modules)
        {
            if (requestNamespace == module.RootNamespace
                || requestNamespace.StartsWith(module.RootNamespace + ".", StringComparison.Ordinal))
            {
                return module.DbContextType;
            }
        }

        throw new InvalidOperationException(
            $"No module owns {requestType.FullName}. Register its module with AddModule and the root namespace of the module.");
    }
}
