using Spectre.Console.Cli;

namespace DotnetGitTool.Infrastructure;

internal sealed class TypeRegistrar(ServiceRegistry registry) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(registry);

    public void Register(Type service, Type implementation) => registry.Register(service, implementation);

    public void RegisterInstance(Type service, object implementation) => registry.RegisterInstance(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => registry.RegisterLazy(service, factory);

    private sealed class TypeResolver(ServiceRegistry registry) : ITypeResolver, IDisposable
    {
        public object? Resolve(Type? type) => type is null ? null : registry.TryGet(type);

        public void Dispose() => registry.Dispose();
    }
}

internal sealed class ServiceRegistry : IDisposable
{
    private readonly Dictionary<Type, Func<object>> factories = [];
    private readonly List<IDisposable> disposables = [];

    public void AddSingleton<TService, TImplementation>() where TImplementation : TService
        => RegisterLazy(typeof(TService), () => Create(typeof(TImplementation)));

    public void AddSingleton<TService>(TService instance) where TService : class
        => RegisterInstance(typeof(TService), instance);

    public void AddSingleton<TImplementation>() where TImplementation : class
        => RegisterLazy(typeof(TImplementation), () => Create(typeof(TImplementation)));

    public void Register(Type service, Type implementation)
        => RegisterLazy(service, () => Create(implementation));

    public void RegisterInstance(Type service, object implementation)
        => factories[service] = () => implementation;

    public void RegisterLazy(Type service, Func<object> factory)
    {
        var gate = new object();
        object? instance = null;
        factories[service] = () =>
        {
            if (instance is not null)
            {
                return instance;
            }

            lock (gate)
            {
                instance ??= factory();
                if (instance is IDisposable disposable)
                {
                    disposables.Add(disposable);
                }

                return instance;
            }
        };
    }

    public object Get(Type type)
    {
        if (factories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        throw new InvalidOperationException($"No service is registered for {type.FullName}.");
    }

    public object? TryGet(Type type)
    {
        if (factories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var itemType = type.GetGenericArguments()[0];
            var matches = factories
                .Where(item => itemType.IsAssignableFrom(item.Key))
                .Select(item => item.Value())
                .ToArray();
            var array = Array.CreateInstance(itemType, matches.Length);
            Array.Copy(matches, array, matches.Length);
            return array;
        }

        return null;
    }

    private object Create(Type type)
    {
        var constructor = type.GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Type {type.FullName} has no public constructor.");
        var arguments = constructor.GetParameters().Select(parameter => Get(parameter.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    public void Dispose()
    {
        foreach (var disposable in disposables.AsEnumerable().Reverse())
        {
            disposable.Dispose();
        }
    }
}
