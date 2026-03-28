# Description of each observability asset

This folder contains the core observability files copied into the assessment area so the delivery is self-contained.

## App

- [app/NopTelemetry.cs](./app/NopTelemetry.cs)
  Central application telemetry helper. It defines the custom spans, custom metrics, safe tags and labels, failure classification, noise suppression rules, span descriptions, and fault-injection logic.

- [app/ObservabilityStartup.cs](./app/ObservabilityStartup.cs)
  OpenTelemetry startup configuration for the ASP.NET Core application. It registers tracing and metrics, wires the OTLP exporter, configures histogram buckets, and post-processes server span names and descriptions.

## Infrastructure

- [infra/docker-compose.yml](./infra/docker-compose.yml)
  Main deployment file for the local demo stack. It runs nopCommerce, SQL Server, OTEL Collector, Jaeger, Prometheus, and Grafana, and injects the OTLP and fault-injection environment variables into the app.

- [infra/otel-collector-config.yaml](./infra/otel-collector-config.yaml)
  OTEL Collector configuration. It receives OTLP telemetry from the app, exports traces to Jaeger, and exposes metrics in a Prometheus-compatible format.

- [infra/prometheus.yml](./infra/prometheus.yml)
  Prometheus scrape configuration. It scrapes the OTEL Collector metrics endpoint.

- [infra/datasources.yml](./infra/datasources.yml)
  Grafana datasource provisioning. It preconfigures Prometheus and Jaeger as the dashboard backends.

- [infra/nopcommerce-order-flow.json](./infra/nopcommerce-order-flow.json)
  Main Grafana dashboard definition for the `Customer Places an Order` flow.
