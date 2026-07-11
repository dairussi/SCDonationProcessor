using Infrastructure;
using Application;
using Prometheus;
using Prometheus.DotNetRuntime;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Métricas de runtime .NET (GC, memória, threads)
DotNetRuntimeStatsBuilder.Customize().StartCollecting();

// OpenTelemetry — tracing distribuído
const string serviceName = "SCDonationProcessor.Worker";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(serviceName)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(
                    builder.Configuration["Jaeger:OtlpEndpoint"] ?? "http://localhost:4317");
            });
    });

var host = builder.Build();

// Servidor HTTP leve só para expor /metrics na porta 9091
// (o Worker não tem ASP.NET Core, então usamos o KestrelMetricServer do prometheus-net)
var metricServer = new KestrelMetricServer(port: 9091);
metricServer.Start();

await host.RunAsync();