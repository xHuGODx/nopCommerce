# Assignment 1 Analysis

For this assignment I focused on the `Customer places an order` flow in `nopCommerce`. This file is about the architectural analysis behind that choice, not the implementation details themselves.

## Scope

- Flow analyzed:
  basket -> checkout confirmation -> order placement -> payment -> inventory update -> business event publication
- Main goal:
  add useful OpenTelemetry tracing and metrics without exposing sensitive data
- Constraint:
  instrument an existing modular monolith instead of trying to redesign it

## Architectural reading of nopCommerce

### Layer organization and dependency direction

- `nopCommerce` is a modular monolith, and that matters because the whole checkout flow happens inside the same application process.
- The dependency direction is mostly downward:
  `Nop.Core -> Nop.Data -> Nop.Services -> Nop.Web.Framework -> Nop.Web`
- `Nop.Core` contains the basic abstractions and domain/event contracts.
- `Nop.Data` contains repositories and migrations.
- `Nop.Services` contains most of the business logic and the event publisher implementation.
- `Nop.Web.Framework` is more than UI support; it is also an important composition layer.
- `Nop.Web` is the presentation and application entry layer.

This layered structure made it possible to find a few central seams where observability could be added without touching the whole codebase.

### Event model

- `IEventPublisher` is a small abstraction with a single `PublishAsync<TEvent>(TEvent)` method.
- The implementation lives in `Nop.Services` and resolves all `IConsumer<TEvent>` handlers from the runtime context.
- Event delivery is in-process, and the consumer order is basically the order in which the runtime scan resolves them.
- The same event mechanism is used for several different purposes:
  - domain notifications
  - persistence notifications
  - UI/model hooks
  - plugin extension points

That flexibility is useful for extensibility, but it is also why generic event instrumentation can become noisy very fast.

- `CheckoutController` is the request entry point for standard and one-page checkout confirmation.
- `OrderProcessingService` is the orchestration center for order placement.
- `PaymentService` handles payment processing and post-processing.
- `ProductService` is where inventory adjustment becomes visible.
- `EventPublisher` is the internal event bus for in-process domain notifications.
- `EntityRepository<TEntity>` is the shared persistence boundary used across business services.

## Where observability is easy vs hard to add

### Easy seams

- `INopStartup`
- `IEventPublisher`
- `EntityRepository<TEntity>`
- ASP.NET Core request entry points such as `CheckoutController`

These are good seams because they are central, already shared by many flows, and can be instrumented without changing the architecture of the application.

### Harder parts

- The application relies a lot on runtime resolution and shared context lookups.
- The event system is broad and not tied to only one business concern.
- Generic repository instrumentation can become noisy very quickly.
- Many useful checkout failures are business failures, not HTTP transport failures.

## Why this flow was chosen

- It crosses the exact business areas requested in the assignment: basket, order, payment, and inventory.
- It contains real orchestration logic instead of being just a simple CRUD action.
- It can fail in ways that are not visible if I only look at HTTP status codes.
- It is a good fit for both tracing and metrics because it includes business logic, persistence, and side effects.

So, from an observability point of view, it is much more interesting than something like viewing a product page or browsing a category.

## Observability tradeoffs

- Instrumenting very generic seams such as the event bus and the repository layer looked attractive at first because it gave quick coverage.
- In practice, that approach produced too many low-value spans.
- What worked better was a mix of business-stage spans and selective infrastructure spans.
- That kept the checkout trace readable while still showing the important DB boundary.

More concretely:

- Event instrumentation had to be restricted to high-signal business events instead of tracing the whole internal event bus.
- Repository instrumentation was kept, but low-value entities had to be filtered out during checkout.
- The main lesson was that “more spans” does not automatically mean “better observability”.

## Structural changes worth doing vs not worth doing

### Worth doing

- Add OpenTelemetry centrally through a startup module.
- Introduce a small shared telemetry helper (`NopTelemetry`) so span names, metrics, safe tags, and failure categories are kept consistent.
- Instrument the main checkout seams directly:
  - controller entry
  - order processing
  - payment
  - inventory
  - repository boundary

### Not worth doing just for this assignment

- A full re-layering of nopCommerce
- Removing all runtime service-location style patterns
- A complete redesign of the internal event system

Those changes might improve the codebase in the long term, but they are too large for the scope of this assignment and not necessary to get useful observability.
