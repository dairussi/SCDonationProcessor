using Application.Events;
using Infrastructure.Adapters.Events.Consumers;
using Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Prometheus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

namespace Infrastructure.HostedServices;

public sealed class RabbitMqDonationConsumerHostedService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("SCDonationProcessor.Worker");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    // Contador Prometheus: quantas mensagens foram processadas e com qual resultado
    private static readonly Counter MessagesProcessed = Metrics
        .CreateCounter(
            "worker_messages_processed_total",
            "Total de mensagens processadas pelo worker",
            labelNames: new[] { "status" });

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RabbitMqDonationConsumerHostedService> _logger;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IConnection? _connection;
    private IModel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;

    public RabbitMqDonationConsumerHostedService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RabbitMqDonationConsumerHostedService> logger,
        IOptions<RabbitMqOptions> rabbitMqOptions)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _rabbitMqOptions = rabbitMqOptions.Value;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        InitializeRabbitMq();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(_channel);

        _channel.QueueDeclare(
            queue: _rabbitMqOptions.DonationReceivedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.Received += OnDonationReceivedAsync;
        _consumerTag = _channel.BasicConsume(
            queue: _rabbitMqOptions.DonationReceivedQueue,
            autoAck: false,
            consumer: _consumer);

        _logger.LogInformation(
            "Worker escutando a fila {QueueName}.",
            _rabbitMqOptions.DonationReceivedQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Encerrando consumidor de doacoes.");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _channel.IsOpen && !string.IsNullOrWhiteSpace(_consumerTag))
        {
            _channel.BasicCancel(_consumerTag);
        }

        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }

    private async Task OnDonationReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(_channel);

        // Extrai o traceparent dos headers da mensagem RabbitMQ
        // Isso conecta este span ao trace iniciado na API
        var parentContext = Propagator.Extract(
            default,
            eventArgs.BasicProperties.Headers,
            ExtractTraceContext);

        Baggage.Current = parentContext.Baggage;

        using var activity = ActivitySource.StartActivity(
            $"{_rabbitMqOptions.DonationReceivedQueue} process",
            ActivityKind.Consumer,
            parentContext.ActivityContext);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _rabbitMqOptions.DonationReceivedQueue);
        activity?.SetTag("messaging.operation", "process");

        try
        {
            var payload = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            var donationEvent = JsonSerializer.Deserialize<DonationReceivedEvent>(
                payload,
                _jsonSerializerOptions);

            if (donationEvent is null)
            {
                _logger.LogWarning(
                    "Mensagem recebida na fila {QueueName} nao pode ser desserializada.",
                    _rabbitMqOptions.DonationReceivedQueue);

                activity?.SetTag("error", true);
                activity?.SetTag("error.reason", "deserialization_failed");

                MessagesProcessed.WithLabels("deserialization_failed").Inc();
                _channel.BasicReject(eventArgs.DeliveryTag, requeue: false);
                return;
            }

            activity?.SetTag("donation.id", donationEvent.DonationId.ToString());
            activity?.SetTag("donation.campaign_id", donationEvent.CampaignId.ToString());
            activity?.SetTag("donation.amount", donationEvent.Amount.ToString());

            using var scope = _serviceScopeFactory.CreateScope();
            var consumer = scope.ServiceProvider.GetRequiredService<DonationReceivedConsumer>();

            await consumer.ConsumeAsync(donationEvent, CancellationToken.None);

            MessagesProcessed.WithLabels("success").Inc();
            _channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao processar mensagem da fila {QueueName}. A mensagem sera reenfileirada.",
                _rabbitMqOptions.DonationReceivedQueue);

            activity?.SetTag("error", true);
            activity?.SetTag("error.type", ex.GetType().Name);

            MessagesProcessed.WithLabels("error").Inc();
            _channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
    }

    // Extrai o valor do header traceparent dos headers AMQP
    // Os headers RabbitMQ armazenam valores como byte[] — precisamos converter para string
    private static IEnumerable<string> ExtractTraceContext(
        IDictionary<string, object>? headers,
        string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
            return Enumerable.Empty<string>();

        return value is byte[] bytes
            ? new[] { Encoding.UTF8.GetString(bytes) }
            : Enumerable.Empty<string>();
    }

    private void InitializeRabbitMq()
    {
        ValidateOptions();

        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqOptions.Host,
            Port = _rabbitMqOptions.Port,
            UserName = _rabbitMqOptions.Username,
            Password = _rabbitMqOptions.Password,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_rabbitMqOptions.DonationReceivedQueue))
        {
            throw new InvalidOperationException("A fila de entrada de doacoes nao foi configurada.");
        }
    }
}