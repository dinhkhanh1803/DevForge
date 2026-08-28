import assert from "node:assert/strict";
import test from "node:test";
import { readPublicEnvironment } from "../src/lib/environment.ts";

test("missing public name has a deterministic local default", () => {
  assert.deepEqual(readPublicEnvironment({}), { siteName: "Team Portal" });
});

test("public name is trimmed and no other environment values are returned", () => {
  assert.deepEqual(readPublicEnvironment({ NEXT_PUBLIC_SITE_NAME: " Team " }), {
    siteName: "Team",
  });
});

for (const value of [
  "",
  " ",
  "x".repeat(65),
  "line\nbreak",
  "control\u0000byte",
]) {
  test(
    "invalid public environment fails without echoing its value: " +
      value.length,
    () => {
      assert.throws(
        () => readPublicEnvironment({ NEXT_PUBLIC_SITE_NAME: value }),
        { message: "Invalid public site name." },
      );
    },
  );
}
