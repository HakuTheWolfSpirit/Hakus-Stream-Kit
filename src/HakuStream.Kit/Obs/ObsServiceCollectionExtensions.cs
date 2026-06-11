using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HakuStream.Kit.Obs;

public static class ObsServiceCollectionExtensions
{
    public static IServiceCollection AddObs(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(configuration.GetSection("Obs").Get<ObsSettings>() ?? new ObsSettings());
        services.TryAddSingleton<ObsClient>();
        return services;
    }
}
