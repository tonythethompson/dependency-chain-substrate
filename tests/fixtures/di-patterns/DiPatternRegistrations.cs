using Microsoft.Extensions.DependencyInjection;

namespace DiPatterns;

public interface IStoragePaths { }
public sealed class StoragePaths : IStoragePaths { }

public interface IHandler { }
public sealed class Handler
{
    public Handler(IStoragePaths paths) { }
}

public sealed class MainWindow
{
    public MainWindow(IHandler handler) { }
}

public static class DiPatternRegistrations
{
    public static void Register(IServiceCollection services, IStoragePaths storagePaths)
    {
        services.TryAddSingleton(storagePaths);
        services.AddScoped(sp => new Handler(sp.GetRequiredService<IStoragePaths>()));
        services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<IHandler>()));
        services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<IHandler>();
            return new MainWindow(handler);
        });
        services.TryAddSingleton<IHandler, Handler>();
        services.AddSingleton<IHandler, Handler>();
    }
}
