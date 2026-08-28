import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { connect } from "node:net";
import test from "node:test";
import { fileURLToPath } from "node:url";

for (const mode of ["assertion", "hang-close", "cancel"]) {
  test(
    "production smoke closes its listening process after " + mode,
    { timeout: 45000 },
    async () => {
      const child = spawn(
        process.execPath,
        [
          "--import",
          new URL("./fixtures/smoke-register.mjs", import.meta.url).href,
          fileURLToPath(new URL("../scripts/smoke.mjs", import.meta.url)),
        ],
        {
          env: { ...process.env, SMOKE_TEST_MODE: mode },
          stdio: ["ignore", "pipe", "pipe"],
          shell: false,
        },
      );
      const exited = once(child, "close");
      let captured = "";
      let port;
      const watchdog = setTimeout(() => child.kill(), 40000);
      child.stdout.on("data", (bytes) => {
        captured += bytes.toString();
        const match = /SMOKE_TEST_PORT=(\d+)/.exec(captured);
        if (match) {
          port = Number(match[1]);
          if (mode === "cancel") {
            child.kill();
          }
        }
      });
      child.stderr.on("data", (bytes) => {
        captured += bytes.toString();
      });
      try {
        const [code, signal] = await exited;
        assert.ok(
          code !== 0 || signal !== null,
          "failure cannot report success",
        );
        assert.ok(port > 0, "fixture must have exercised a real listener");
        if (mode === "assertion") {
          assert.match(captured, /SMOKE_TEST_CLOSED/);
        }
        if (mode === "hang-close") {
          assert.match(captured, /Production HTTP smoke timed out/);
        }
        await assert.rejects(
          async () => {
            const socket = connect({ host: "127.0.0.1", port });
            socket.setTimeout(1000, () =>
              socket.destroy(new Error("Probe timed out")),
            );
            try {
              await once(socket, "connect");
            } finally {
              socket.destroy();
            }
          },
          { code: "ECONNREFUSED" },
        );
      } finally {
        clearTimeout(watchdog);
        if (child.exitCode === null && child.signalCode === null) {
          child.kill();
          await exited;
        }
      }
    },
  );
}
