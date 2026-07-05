using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace DiPatterns.LeakedGuard;

public interface ISharedFeature
{
}

public sealed class AvaloniaFeature : ISharedFeature
{
}

public static class AvaloniaComposition
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ISharedFeature, AvaloniaFeature>();
    }
}
