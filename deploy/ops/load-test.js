// Load test (k6): how the deployed app holds up under many users at once.
//
//   k6 run -e URL=https://<staging-url> deploy/ops/load-test.js
//
// Ramps to 200 virtual users, each opening the app like a phone does (shell, health) and,
// once the API is deployed, reading the patient list. Thresholds fail the run if more than 1%
// of requests fail or the 95th percentile is over 1.5 s. Point it at staging, not production.
import http from "k6/http"
import { check, sleep } from "k6"

const URL = __ENV.URL
const TOKEN = __ENV.TOKEN // optional: a demo session token, to include API reads

export const options = {
  stages: [
    { duration: "30s", target: 50 },
    { duration: "1m", target: 200 },
    { duration: "30s", target: 0 },
  ],
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<1500"],
  },
}

export default function () {
  check(http.get(`${URL}/`), { "app shell 200": (r) => r.status === 200 })
  check(http.get(`${URL}/health`), { "health 200": (r) => r.status === 200 })
  if (TOKEN) {
    const r = http.get(`${URL}/api/patients?updatedSince=1970-01-01`, {
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
    check(r, { "api 200": (res) => res.status === 200 })
  }
  sleep(1 + Math.random() * 2)
}
