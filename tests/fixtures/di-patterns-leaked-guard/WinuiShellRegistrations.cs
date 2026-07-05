using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace DiPatterns.LeakedGuard;

public sealed class WinuiFeature : ISharedFeature
{
}

public static class WinuiShellRegistrations
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ISharedFeature, WinuiFeature>();
    }
}
