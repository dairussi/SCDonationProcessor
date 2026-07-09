using Application.Common.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("SolidarityConnection.Worker");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    public RabbitMqEventPublisher(
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqOptions.Host,
            Port = _rabbitMqOptions.Port,
            UserName = _rabbitMqOptions.Username,
            Password = _rabbitMqOptions.Password
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(
            queue: _rabbitMqOptions.DonationProcessedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class
    {
        using var activity = ActivitySource.StartActivity(
            $"{_rabbitMqOptions.DonationProcessedQueue} publish",
            ActivityKind.Producer);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = new Dictionary<string, object>();

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination", _rabbitMqOptions.DonationProcessedQueue);
            activity.SetTag("messaging.operation", "publish");

            Propagator.Inject(
                new PropagationContext(activity.Context, Baggage.Current),
                properties.Headers,
                InjectTraceContext);
        }

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: _rabbitMqOptions.DonationProcessedQueue,
            basicProperties: properties,
            body: body);

        _logger.LogInformation(
            "Mensagem publicada na fila {QueueName}.",
            _rabbitMqOptions.DonationProcessedQueue);

        return Task.CompletedTask;
    }

    private static void InjectTraceContext(
        IDictionary<string, object> headers,
        string key,
        string value)
    {
        headers[key] = Encoding.UTF8.GetBytes(value);
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}