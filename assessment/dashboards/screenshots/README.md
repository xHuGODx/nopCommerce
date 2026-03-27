## Description of each screenshot

- `Example_dashboard_view.png`
  Main Grafana dashboard for the `Customer Places an Order` flow. It shows the Jaeger-backed trace list, the `Checkout Stage P95` panel, `Business Failures By Stage`, `Checkout Outcomes`, and the computed `Checkout Error Rate`.

- `Example_trace_details.png`
  Jaeger trace details for a checkout execution, focused on the `checkout.place_order` span and one of its child stages. It shows the custom business tags and the span `description` field added by the observability layer.

- `Faul_injection_on_vs_off.png`
  Jaeger search/results view used to compare runs with and without fault injection. It shows a mix of successful traces and shorter error traces for the same `POST /checkout/OpcConfirmOrder` operation.

- `Faulty_delayed_trace.png`
  Jaeger trace timeline for a delayed checkout. It shows a successful order flow where `checkout.prepare_details` was intentionally slowed down, increasing the duration of the parent checkout stages without breaking the whole request.

- `Faulty_error_trace.png`
  Jaeger trace timeline for a failed checkout caused by injected failure. It shows the flow stopping around `checkout.payment.process`, with a shorter trace and error-marked spans compared to a normal successful checkout.
