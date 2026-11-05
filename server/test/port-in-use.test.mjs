import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { isRevitMcpServerCommand } from "../build/utils/stalePort.js";

const serverDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const entry = path.join(serverDir, "build", "index.js");

function startServer(port) {
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

function waitFor(predicate, timeoutMs = 20000) {
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

test("recognizes this server's command line and ignores unrelated node processes", () => {
  assert.equal(
    isRevitMcpServerCommand(
      '"C:\\Program Files\\nodejs\\node.exe" c:\\Projects\\Programming\\My\\revit-mcp-server\\server\\build\\index.js'
    ),
    true
  );
  assert.equal(
    isRevitMcpServerCommand("node /tmp/revit-mcp-server/server/test/port-in-use.test.mjs"),
    false
  );
});

test("a second server replaces a stale revit-mcp-server and does not crash", { timeout: 60_000 }, async () => {
  const port = 18080;
  const first = startServer(port);
  try {
    await waitFor(() => first.stderr().includes(`listening on ws://127.0.0.1:${port}`));

    const second = startServer(port);
    try {
      await waitFor(() => second.stderr().includes(`listening on ws://127.0.0.1:${port}`));
      await waitFor(() => first.child.exitCode !== null);
      assert.equal(second.child.exitCode, null);
      assert.doesNotMatch(second.stderr(), /Unhandled 'error' event/);
      assert.match(second.stderr(), /Stopping stale revit-mcp-server/);
    } finally {
      second.child.kill();
    }
  } finally {
    if (first.child.exitCode === null) first.child.kill();
  }
});

test("an unrelated process holding the port is left running", { timeout: 60_000 }, async () => {
  const port = 18081;
  const blocker = net.createServer();
  await new Promise((resolve, reject) => {
    blocker.once("error", reject);
    blocker.listen(port, resolve);
  });

  const server = startServer(port);
  try {
    await waitFor(() => server.child.exitCode !== null);
    assert.equal(server.child.exitCode, 1);
    assert.match(server.stderr(), /already in use/i);
    assert.doesNotMatch(server.stderr(), /Unhandled 'error' event/);
    assert.equal(blocker.listening, true);
  } finally {
    if (server.child.exitCode === null) server.child.kill();
    await new Promise((resolve) => blocker.close(resolve));
  }
});

test("closing stdin releases the WebSocket port", { timeout: 60_000 }, async () => {
  const port = 18082;
  const first = startServer(port);
  try {
    await waitFor(() => first.stderr().includes(`listening on ws://127.0.0.1:${port}`));
    first.child.stdin.end();
    await waitFor(() => first.child.exitCode !== null);
    assert.equal(first.child.exitCode, 0);

    const second = startServer(port);
    try {
      await waitFor(() => second.stderr().includes(`listening on ws://127.0.0.1:${port}`));
      assert.equal(second.child.exitCode, null);
      assert.doesNotMatch(second.stderr(), /already in use/i);
    } finally {
      second.child.kill();
    }
  } finally {
    if (first.child.exitCode === null) first.child.kill();
  }
});
