# Assignment 1 Report

In this assignment I added OpenTelemetry-based observability to the `Customer places an order` flow in `nopCommerce`.

## What was implemented

- OpenTelemetry registration in the ASP.NET Core startup path
- Custom checkout spans based on business stages
- Custom metrics for latency, business failures, and end-to-end outcomes
- An allowlist of safe tags so sensitive data is not exported
- A local observability stack with OTEL Collector, Jaeger, Prometheus, and Grafana
- Load-test scripts and controlled fault injection for validation and demo purposes

## Instrumentation strategy

- Use the ASP.NET Core server span as the HTTP entry point.
- Add explicit business spans for the main checkout stages.
- Keep repository spans so the trace reaches the DB boundary.
- Keep event spans selective instead of tracing the whole internal event bus.
- Centralize span names, metrics, safe attributes, and failure classification in `NopTelemetry`.

## Main tracing model

The checkout flow is modeled with the following business stages:

- `checkout.confirm_order`
- `checkout.place_order`
- `checkout.prepare_details`
- `checkout.payment.process`
- `checkout.payment.postprocess`
- `checkout.order.save`
- `checkout.order_items.move`
- `checkout.inventory.adjust`
- `checkout.event.publish`

These stages are used as explicit checkout spans where appropriate, and they are also reused as metric dimensions and failure locations.

## End-to-end tracing plan

- Let ASP.NET Core create the root HTTP request span for checkout confirmation.
- Use `OrderProcessingService.PlaceOrderAsync` as the main business entry point because both checkout variants converge there.
- Add child spans for the main responsibilities inside that flow:
  - `checkout.confirm_order`
  - `checkout.place_order`
  - `checkout.prepare_details`
  - `checkout.payment.process`
  - `checkout.payment.postprocess`
  - `checkout.order.save`
  - `checkout.order_items.move`
  - `checkout.inventory.adjust`
  - `checkout.event.publish`
- Instrument `EntityRepository<TEntity>` so important persistence work is visible without modifying every single service method.

## Custom metrics

- `checkout_stage_duration_seconds`
  Histogram of checkout latency by stage
- `checkout_business_failures_total`
  Counter of business failures by stage and failure category
- `checkout_outcomes_total`
  Counter of end-to-end checkout success or failure

## Why these metrics were chosen

### `checkout_stage_duration_seconds`

- It shows where the checkout path is slowing down before users necessarily see explicit failures.
- A higher p95 on `payment.process` suggests provider trouble.
- A higher p95 on `order.save` suggests DB contention.
- A higher p95 on `inventory.adjust` suggests stock-update bottlenecks.

### `checkout_business_failures_total`

- It captures business failures that would not appear in plain HTTP `5xx` metrics.
- This matters because checkout can fail by re-rendering warnings while the HTTP transport itself still succeeds.

### `checkout_outcomes_total`

- It shows whether checkout attempts are actually finishing successfully, not just being requested.

## Privacy handling

- Telemetry uses an allowlist of safe attributes instead of trying to mask data afterwards.
- Raw personal, address, and payment information is not added to custom spans or metric labels.
- Error handling emits normalized categories such as `payment_declined`, `inventory`, `db`, and `validation` instead of raw provider messages.
- Monetary context is bucketed through `order.total.range` instead of exporting exact totals.

Safe custom span attributes include:

- `checkout.variant`
- `store.id`
- `cart.items.count`
- `payment.method.system_name`
- `is_recurring`
- `order.total.range`

Safe custom metric labels include:

- `checkout_variant`
- `payment_method_system_name`
- `is_recurring`
- plus stage/result/failure labels where applicable

Excluded values include:

- emails
- names
- phone numbers
- addresses
- payment details
- raw provider responses

This means the privacy rule is enforced at emission time: the unsafe data is never added to the custom telemetry in the first place.

## Noise mitigation

My first instrumentation pass was too broad around:

- `EventPublisher`
- `EntityRepository<TEntity>`

That produced noisy traces because a single checkout triggers many generic events and auxiliary DB writes.

The final mitigation was:

- only trace selected event-bus activity with high business value
- suppress low-value repository entities during checkout:
  - `GenericAttribute`
  - `OrderNote`
  - `QueuedEmail`

This made the traces shorter, easier to read, and much more useful during diagnosis.

## Fault injection

Fault injection is implemented at the checkout-stage wrapper level and can be turned on or off with environment variables.

Supported modes:

- probabilistic failure on a selected stage
- probabilistic delay on a selected stage

I used this to validate that:

- Jaeger traces point to the correct failing or delayed stage
- Grafana panels reflect latency and failure changes in the expected location

## Delivery map

- Analysis: [ANALYSIS.md](./ANALYSIS.md)
- Critique: [CRITIQUE.md](./CRITIQUE.md)
- Load tests: [loadtests](./loadtests)
- Observability configs: [observability](./observability)
- Supporting assessment index: [assessment](./assessment)
