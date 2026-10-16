import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const serverDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const entry = path.join(serverDir, "build", "index.js");
const port = 18080;

function startServer() {
  const child = spawn(process.execPath, [entry], {
    cwd: serverDir,
    env: { ...process.env, REVIT_WS_PORT: String(port) },
    stdio: ["pipe", "pipe", "pipe"],
  });
  let stderr = "";
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });
  return {
    child,
    stderr: () => stderr,
  };
}

function waitFor(predicate, timeoutMs = 8000) {
  const started = Date.now();
  return new Promise((resolve, reject) => {
    const tick = () => {
      if (predicate()) {
        resolve();
        return;
      }
      if (Date.now() - started > timeoutMs) {
        reject(new Error("timed out"));
        return;
      }
      setTimeout(tick, 50);
    };
    tick();
  });
}

test("a second server reports that the WebSocket port is taken and does not crash", async () => {
  const first = startServer();
  try {
    await waitFor(() => first.stderr().includes(`listening on ws://127.0.0.1:${port}`));

    const second = startServer();
    try {
      await waitFor(() => second.child.exitCode !== null);
      const output = second.stderr();
      assert.equal(second.child.exitCode, 1);
      assert.match(output, /already in use/i);
      assert.doesNotMatch(output, /Unhandled 'error' event/);
      assert.equal(first.child.exitCode, null);
    } finally {
      second.child.kill();
    }
  } finally {
    first.child.kill();
  }
});
