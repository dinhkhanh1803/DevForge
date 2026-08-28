// Test-only Next replacement. It still uses the unchanged smoke script's real
// HTTP listener, deadline and shutdown path; no production import loads this.
export default function createApp() {
  const mode = process.env.SMOKE_TEST_MODE;
  return {
    async prepare() {},
    getRequestHandler() {
      return async (request, response) => {
        console.log("SMOKE_TEST_PORT=" + request.socket.localPort);
        if (mode === "cancel") {
          await new Promise(() => {});
        } else {
          response.statusCode = 503;
          response.end("Test failure.");
        }
      };
    },
    async close() {
      if (mode === "hang-close") {
        await new Promise(() => {});
      }
      console.log("SMOKE_TEST_CLOSED");
    },
  };
}
