#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

FAIL_STAGE="checkout.payment.process"
FAIL_PERCENT="40"
DELAY_STAGE="checkout.prepare_details"
DELAY_PERCENT="100"
DELAY_MS="750"

show_usage() {
  cat <<'EOF'
Usage:
  bash ./loadtests/toggle.sh on
  bash ./loadtests/toggle.sh off
  bash ./loadtests/toggle.sh status
EOF
}

show_status() {
  if command -v rg >/dev/null 2>&1; then
    docker inspect nopcommerce --format '{{range .Config.Env}}{{println .}}{{end}}' | rg '^FAULT_INJECTION_|^OTEL_EXPORTER_OTLP_ENDPOINT=' || true
  else
    docker inspect nopcommerce --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -E '^FAULT_INJECTION_|^OTEL_EXPORTER_OTLP_ENDPOINT=' || true
  fi
}

restart_with_faults() {
  (
    cd "$PROJECT_ROOT"
    FAULT_INJECTION_ENABLED=true \
    FAULT_INJECTION_FAIL_STAGE="$FAIL_STAGE" \
    FAULT_INJECTION_FAIL_PERCENT="$FAIL_PERCENT" \
    FAULT_INJECTION_DELAY_STAGE="$DELAY_STAGE" \
    FAULT_INJECTION_DELAY_PERCENT="$DELAY_PERCENT" \
    FAULT_INJECTION_DELAY_MS="$DELAY_MS" \
    docker compose up -d nopcommerce_web
  )
}

restart_without_faults() {
  (
    cd "$PROJECT_ROOT"
    FAULT_INJECTION_ENABLED=false \
    FAULT_INJECTION_FAIL_STAGE= \
    FAULT_INJECTION_FAIL_PERCENT=0 \
    FAULT_INJECTION_DELAY_STAGE= \
    FAULT_INJECTION_DELAY_PERCENT=0 \
    FAULT_INJECTION_DELAY_MS=0 \
    docker compose up -d nopcommerce_web
  )
}

ACTION="${1:-status}"

case "$ACTION" in
  on)
    restart_with_faults
    echo "Fault injection enabled."
    show_status
    ;;
  off)
    restart_without_faults
    echo "Fault injection disabled."
    show_status
    ;;
  status)
    show_status
    ;;
  *)
    show_usage
    exit 1
    ;;
esac
