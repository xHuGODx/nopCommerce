#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

docker run --rm --network host \
  -v "$PROJECT_ROOT:/work" \
  grafana/k6 run /work/loadtests/checkout-baseline.js \
  -e BASE_URL=http://localhost \
  -e PRODUCT_PATH=/htc-smartphone \
  -e PAYMENT_METHOD=Payments.CheckMoneyOrder \
  -e VUS=3 \
  -e ITERATIONS=30 \
  -e PAUSE_SECONDS=20 \
  -e MAX_DURATION=25m \
  "$@"
