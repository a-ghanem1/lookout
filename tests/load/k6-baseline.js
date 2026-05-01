/**
 * Lookout M11.1 — baseline load test
 *
 * Purpose: measure p99 HTTP overhead introduced by Lookout at 1,000 concurrent VUs.
 * Acceptance criterion: Lookout-enabled p99 ≤ baseline p99 × 1.05 (≤5% regression).
 *
 * Usage:
 *   # 1. Start the sample app WITHOUT Lookout (comment out AddLookout / UseLookout in Program.cs):
 *   dotnet run --project samples/WebApp --urls http://localhost:5080
 *   k6 run tests/load/k6-baseline.js --env LOOKOUT=off --out json=tests/load/results-baseline.json
 *
 *   # 2. Start the sample app WITH Lookout enabled:
 *   dotnet run --project samples/WebApp --urls http://localhost:5080
 *   k6 run tests/load/k6-baseline.js --env LOOKOUT=on  --out json=tests/load/results-lookout.json
 *
 *   # 3. Compare p50/p95/p99 from both outputs.
 *
 * Results (fill in after running both passes):
 * ┌─────────────────────────────┬──────────────────┬──────────────────┬────────────┐
 * │ Scenario                    │ p50 (ms)         │ p95 (ms)         │ p99 (ms)   │
 * ├─────────────────────────────┼──────────────────┼──────────────────┼────────────┤
 * │ Baseline (Lookout off)      │ TBD              │ TBD              │ TBD        │
 * │ Lookout enabled             │ TBD              │ TBD              │ TBD        │
 * │ Overhead (p99)              │                  │                  │ TBD %      │
 * └─────────────────────────────┴──────────────────┴──────────────────┴────────────┘
 *
 * dotnet-counters (run in parallel during both passes):
 *   dotnet-counters monitor --process-id <pid> System.Runtime
 *   Watch: GC Heap Size, ThreadPool Queue Length, Allocation Rate.
 *   Flag if allocation rate doubles relative to baseline.
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

// Custom per-endpoint metrics for more granular analysis.
const ordersLatency = new Trend('orders_duration', true);
const weatherLatency = new Trend('weather_duration', true);

export const options = {
  scenarios: {
    // Heavy scenario: /orders/1 exercises EF queries, N+1 detection, cache, and outbound HTTP.
    orders: {
      executor: 'constant-vus',
      vus: 750,
      duration: '60s',
      exec: 'hitOrders',
    },
    // Lightweight scenario: /weatherforecast is a pure in-memory endpoint.
    weather: {
      executor: 'constant-vus',
      vus: 250,
      duration: '60s',
      exec: 'hitWeather',
    },
  },

  // Thresholds enforce the ≤5% p99 budget.
  // Populate the baseline p99 value here after the first (no-Lookout) run.
  thresholds: {
    // Overall p99 across all requests must stay under 2 s (sanity guard).
    http_req_duration: ['p(99)<2000'],
    // Per-endpoint thresholds (update after measuring baseline):
    orders_duration: ['p(99)<2000'],
    weather_duration: ['p(99)<2000'],
  },
};

const BASE = __ENV.BASE_URL || 'http://localhost:5080';

export function hitOrders() {
  const res = http.get(`${BASE}/orders/1`, { tags: { name: 'orders' } });
  ordersLatency.add(res.timings.duration);
  check(res, { 'orders 200': (r) => r.status === 200 });
  sleep(0.05);
}

export function hitWeather() {
  const res = http.get(`${BASE}/weatherforecast`, { tags: { name: 'weather' } });
  weatherLatency.add(res.timings.duration);
  check(res, { 'weather 200': (r) => r.status === 200 });
  sleep(0.05);
}
