# Assignment 1 Critique

## What worked well

- The chosen flow was a good fit for the assignment because it crosses several important business areas and is easy to justify.
- The business-stage spans made the checkout trace much easier to read than pure framework spans.
- The three custom metrics ended up being genuinely useful instead of just decorative.
- The privacy model was simple and reliable because it started from an allowlist.
- ASP.NET Core was pleasant to work with for OpenTelemetry setup because the framework already gives a clean startup model and built-in instrumentation hooks.

## Critique of the platform choices

### nopCommerce

- For a few carefully chosen user journeys, it was reasonably easy to identify good instrumentation points.
- The checkout flow has clear orchestration points such as `CheckoutController`, `OrderProcessingService`, `PaymentService`, `ProductService`, `EventPublisher`, and `EntityRepository`.
- At the same time, I do not think instrumenting the whole application in the same style would be a good idea.
- The codebase is broad, the event system is used for many unrelated concerns, and generic seams can very easily create too much low-value telemetry.
- So for me the main lesson was that nopCommerce is a good target for focused observability, but not for a “trace everything” approach.

### ASP.NET Core

- ASP.NET Core itself was comparatively easy to instrument.
- Registering OpenTelemetry in startup, using the built-in ASP.NET and HttpClient instrumentation, and exporting through OTLP was straightforward.
- The framework made it easy to anchor the work around real request entry points instead of inventing custom plumbing.

## Mistakes made during implementation

- Generic event-bus instrumentation created too many low-value spans.
- Generic repository instrumentation also created noisy traces for auxiliary entities.
- Some dashboard queries needed refinement after container restarts and short load-test runs.

## How those mistakes were mitigated

- Restrict event tracing to high-signal business events.
- Suppress low-value repository entities during checkout.
- Refine Grafana queries so panels show aggregated values and current-instance data more clearly.

## Remaining limitations

- The checkout flow is still part of a monolith, so this is distributed tracing in the OpenTelemetry sense, not in the microservice sense.
- Histogram-based latency panels are approximate because they come from bucketed metrics.
- Some controller-level failure attribution is broader than the leaf business stage and may need explanation.
- The current approach works well for a few important user journeys, but it would need tighter prioritization and probably more automation before being expanded to the whole application.
