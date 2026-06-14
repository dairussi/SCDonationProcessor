using Application.Common.Ports;
using Infrastructure.Adapters.Events.Consumers;
using Infrastructure.HostedServices;
using Infrastructure.Messaging;
using Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMQ"));
        services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
        services.AddScoped<DonationReceivedConsumer>();
        services.AddHostedService<RabbitMqDonationConsumerHostedService>();

        return services;
    }
}
