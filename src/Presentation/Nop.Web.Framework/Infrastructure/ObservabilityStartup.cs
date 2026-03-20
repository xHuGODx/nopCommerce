using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nop.Web.Framework.Infrastructure;

/// <summary>
/// Registers OpenTelemetry tracing and metrics for nopCommerce.
/// </summary>
public partial class ObservabilityStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = GetOtlpEndpoint(configuration);
        var serviceVersion = typeof(ObservabilityStartup).Assembly.GetName().Version?.ToString() ?? "unknown";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: NopTelemetry.ServiceName,
                serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .SetSampler(new AlwaysOnSampler())
                .AddSource(NopTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = false;
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = false;
                })
                .AddOtlpExporter(options => options.Endpoint = endpoint))
            .WithMetrics(metrics => metrics
                .AddMeter(NopTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddView(
                    "checkout_stage_duration_seconds",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30]
                    })
                .AddOtlpExporter(options => options.Endpoint = endpoint));
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public virtual void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 150;

    private static Uri GetOtlpEndpoint(IConfiguration configuration)
    {
        var endpoint = configuration["Observability:Otlp:Endpoint"]
                       ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                       ?? "http://localhost:4317";

        return new Uri(endpoint);
    }
}
