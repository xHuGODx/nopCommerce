# Description of each load-test asset

This folder contains the k6 scripts and helper shell scripts used to generate telemetry and validate the dashboard under normal and faulty checkout conditions.

## Files

- [checkout-baseline.js](./checkout-baseline.js)
  Main k6 scenario for anonymous one-page checkout. It adds a product to cart, completes the checkout flow, and confirms the order using `Payments.CheckMoneyOrder`.

- [checkout-failure-missing-payment-info.js](./checkout-failure-missing-payment-info.js)
  k6 scenario for a controlled failed checkout. It selects `Payments.Manual` but intentionally skips the payment-info step, forcing a backend validation failure at confirm order.

- [load.sh](./load.sh)
  Shell wrapper for the baseline checkout load run. It executes `checkout-baseline.js` with the current hardcoded run profile used for telemetry generation and dashboard screenshots.

- [failure.sh](./failure.sh)
  Shell wrapper for the controlled failed checkout run. It executes `checkout-failure-missing-payment-info.js` with a minimal single-run configuration.

- [toggle.sh](./toggle.sh)
  Helper script to enable, disable, or inspect fault injection. It restarts `nopcommerce_web` with the environment variables that control injected delay and injected failure by checkout stage.

## What this folder demonstrates

- steady successful checkout traffic for Grafana and Jaeger
- controlled business failure generation
- controlled backend fault injection for demo purposes
