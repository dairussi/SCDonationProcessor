using Application.Common.Ports;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDonationProcessingService, DonationProcessingService>();

        return services;
    }
}
