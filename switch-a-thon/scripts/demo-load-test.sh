#!/usr/bin/env bash
set -euo pipefail

base_url="${RESOLVEIQ_LABEL_URL:-http://localhost:5082}"
request_count="${1:-90}"
concurrency="${2:-30}"

if ! [[ "$request_count" =~ ^[0-9]+$ ]] || ! [[ "$concurrency" =~ ^[0-9]+$ ]] || (( request_count < 1 || concurrency < 1 )); then
  echo "Usage: bash scripts/demo-load-test.sh [request-count] [concurrency]" >&2
  exit 2
fi

echo "Sending ${request_count} mixed requests with up to ${concurrency} concurrent clients to ${base_url}/labels"
seq 1 "$request_count" | xargs -P "$concurrency" -I '{}' sh -c '
  i="$1"
  case $((i % 3)) in
    0)
      payload="{\"carrierServiceType\":\"GROUND\",\"originPostcode\":\"94105\",\"destinationPostcode\":\"10001\",\"originCountry\":\"US\",\"destinationCountry\":\"US\",\"weight\":2.5,\"length\":30,\"height\":20,\"width\":20}"
      ;;
    1)
      payload="{\"carrierServiceType\":\"GROUND\",\"originPostcode\":\"94105\",\"destinationPostcode\":\"30301\",\"originCountry\":\"US\",\"destinationCountry\":\"US\",\"weight\":2.5,\"length\":30,\"height\":20,\"width\":20}"
      ;;
    2)
      payload="{\"carrierServiceType\":\"GROUND\",\"originPostcode\":\"94105\",\"destinationPostcode\":\"10001\",\"originCountry\":\"US\",\"destinationCountry\":\"US\",\"weight\":2.5,\"length\":-1,\"height\":20,\"width\":20}"
      ;;
  esac
  curl --silent --show-error --output /dev/null --write-out "%{http_code}\n" \
    --request POST "$2/labels" --header "Content-Type: application/json" --data "$payload"
' sh '{}' "$base_url" | sort | uniq -c

echo "Done. Expect HTTP 200 (simulated label success), 422 (missing zone), and 400 (invalid request)."
