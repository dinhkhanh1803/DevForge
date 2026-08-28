import assert from "node:assert/strict";
import { createServer } from "node:http";
import next from "next";

const hostname = "127.0.0.1";
const app = next({ dev: false, hostname });
const handler = app.getRequestHandler();
const server = createServer((request, response) => {
  void handler(request, response).catch(() => {
    response.statusCode = 500;
    response.end("Request failed.");
  });
});
const deadline = setTimeout(() => {
  console.error("Production HTTP smoke timed out.");
  process.exit(1);
}, 30000);

try {
  await app.prepare();
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, hostname, resolve);
  });
  const address = server.address();
  assert.ok(address && typeof address !== "string");
  const origin = "http://" + hostname + ":" + address.port;
  const page = await fetch(origin, { signal: AbortSignal.timeout(5000) });
  assert.equal(page.status, 200);
  assert.match(await page.text(), /Start with the handoff/);
  assert.equal(page.headers.get("x-powered-by"), null);
  const health = await fetch(origin + "/api/health", {
    signal: AbortSignal.timeout(5000),
  });
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), {
    status: "ok",
    service: "team-portal",
  });
  console.log("Production HTTP smoke passed.");
} finally {
  server.closeAllConnections();
  await new Promise((resolve) => server.close(resolve));
  await app.close();
  clearTimeout(deadline);
}
