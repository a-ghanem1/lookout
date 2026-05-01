/**
 * Lookout M11.5 — one-hour soak test
 *
 * Purpose: verify that Lookout does not leak memory or cause unbounded SQLite growth
 * under representative dev-CI load over an extended period.
 *
 * Acceptance criteria:
 *   - No "LookoutChannelDropped" warnings in the application log.
 *   - SQLite file size must not grow unboundedly (pruning service must keep it capped).
 *   - GC heap must be stable from t=30m onward (no upward trend).
 *   - p50 for GET /lookout/api/entries stays ≤20 ms.
 *
 * Usage:
 *   dotnet run --project samples/WebApp --urls http://localhost:5080
 *   k6 run tests/load/k6-soak.js
 *
 *   Monitor in a second terminal:
 *   dotnet-counters monitor --process-id <pid> System.Runtime
 *   Watch: GC Heap Size, Allocation Rate, ThreadPool Queue Length.
 *
 *   Check SQLite file size at t=0, t=30m, t=60m:
 *   ls -lh <StoragePath>/.lookout
 *
 *   Record all results in tests/benchmarks/RESULTS.md.
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const apiLatency = new Trend('lookout_api_duration', true);

export const options = {
  scenarios: {
    // Representative dev load: 50 VUs, typical mix of heavy and light endpoints.
    soak: {
      executor: 'constant-vus',
      vus: 50,
      duration: '60m',
    },
    // Poll the Lookout API as the dashboard would — once every 2 seconds per user.
    dashboard_poll: {
      executor: 'constant-vus',
      vus: 2,
      duration: '60m',
      exec: 'pollDashboard',
    },
  },

  thresholds: {
    http_req_failed: ['rate<0.01'],          // <1% error rate overall
    http_req_duration: ['p(99)<5000'],       // sanity guard
    lookout_api_duration: ['p(50)<20'],      // dashboard API p50 ≤20 ms
  },
};

const BASE = __ENV.BASE_URL || 'http://localhost:5080';

export default function () {
  // Alternate between the two sample endpoints to mimic mixed dev traffic.
  if (Math.random() < 0.75) {
    const res = http.get(`${BASE}/orders/1`);
    check(res, { 'orders 200': (r) => r.status === 200 });
  } else {
    const res = http.get(`${BASE}/weatherforecast`);
    check(res, { 'weather 200': (r) => r.status === 200 });
  }
  sleep(0.1 + Math.random() * 0.4); // 100–500 ms think time
}

export function pollDashboard() {
  const res = http.get(`${BASE}/lookout/api/entries?limit=50`);
  apiLatency.add(res.timings.duration);
  check(res, { 'dashboard api 200': (r) => r.status === 200 });
  sleep(2);
}
