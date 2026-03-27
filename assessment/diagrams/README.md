# Description of each diagram

This folder contains the diagrams used to explain the architecture and observability design of the assignment.

## Files

- [Macro_architecture.png](./Macro_architecture.png)
  High-level architecture overview of the deployed system. It shows the user interacting with nopCommerce, the database, and the observability stack made of OTEL Collector, Jaeger, Prometheus, and Grafana.

- [checkout-tracing-flow.md](./checkout-tracing-flow.md)
  Mermaid diagram of the business stages used to model the `Customer places an order` flow. It shows the logical order of the custom checkout spans from request confirmation to event publication and post-processing.

- [telemetry-pipeline.md](./telemetry-pipeline.md)
  Mermaid diagram of the telemetry path. It shows how app-side instrumentation goes through `NopTelemetry` and the OpenTelemetry SDK, reaches the OTEL Collector, then flows to Jaeger for traces and to Prometheus for metrics, with Grafana querying both.

- [fault-injection-flow.md](./fault-injection-flow.md)
  Mermaid diagram of the controlled fault-injection logic. It shows how each checkout stage passes through the wrapper that can inject a delay or failure before the stage finishes and updates traces and metrics.

## Notes

- The diagrams are stored as Mermaid source so they can be rendered in GitHub.
