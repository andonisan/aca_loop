using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Orchestrator.Constants;

namespace Orchestrator.ServiceDefaults;

/// <summary>
/// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
/// Referenced by every host/service project in the solution.
/// </summary>
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
            )
            .WithTracing(tracing =>
                tracing
                    .AddSource(LoggingNames.InstrumentationSourceName)
                    .AddAspNetCoreInstrumentation(aspNetCore =>
                        aspNetCore.Filter = context =>
                            !context.Request.Path.StartsWithSegments(
                                Orchestrator.Constants.HealthChecks.HealthEndpoint
                            )
                            && !context.Request.Path.StartsWithSegments(
                                Orchestrator.Constants.HealthChecks.AliveEndpoint
                            )
                    )
                    .AddHttpClientInstrumentation()
            );

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        if (
            !string.IsNullOrWhiteSpace(
                builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            )
        )
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddHealthChecks()
            .AddCheck(
                Orchestrator.Constants.HealthChecks.SelfName,
                () => HealthCheckResult.Healthy(),
                [Orchestrator.Constants.HealthChecks.LiveTag]
            );

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(Orchestrator.Constants.HealthChecks.HealthEndpoint).AllowAnonymous();

        app.MapHealthChecks(
                Orchestrator.Constants.HealthChecks.AliveEndpoint,
                new HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains(Orchestrator.Constants.HealthChecks.LiveTag),
                }
            )
            .AllowAnonymous();

        return app;
    }
}
