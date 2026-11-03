import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { registerTools } from "./tools/registerTools.js";
import { connectionManager } from "./utils/ConnectionManager.js";
import { clearPidLock, releaseStaleServer, writePidLock } from "./utils/stalePort.js";

const server = new McpServer({
  name: "revit-mcp-server",
  version: "1.0.0",
});

const REVIT_WS_PORT = Number(process.env.REVIT_WS_PORT ?? 8080);

function isPortInUse(error: unknown): boolean {
  return error instanceof Error && error.message.includes("already in use");
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Cursor can drop the MCP client while this process is still coming up.
// The WebSocket listener keeps the event loop alive, so the process would
// otherwise stay bound to the Revit port and the next launch would fail.
function exitWhenClientDisconnects(port: number): void {
  let stopped = false;
  const shutdown = () => {
    if (stopped) return;
    stopped = true;
    clearPidLock(port);
    process.exit(0);
  };

  if (process.stdin.readableEnded || process.stdin.destroyed) {
    shutdown();
    return;
  }

  process.stdin.on("end", shutdown);
  process.stdin.on("close", shutdown);
  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

async function bindRevitPort(port: number): Promise<void> {
  const deadline = Date.now() + 8000;
  let lastError: unknown;
  while (Date.now() <= deadline) {
    try {
      await connectionManager.start(port);
      return;
    } catch (error) {
      lastError = error;
      if (!isPortInUse(error)) throw error;
      const released = await releaseStaleServer(port);
      if (!released) throw error;
      await delay(100);
    }
  }
  throw lastError;
}

async function main() {
  // Host the WebSocket server the Revit add-in connects to.
  // Wait until the port is actually bound. `ws` reports EADDRINUSE asynchronously,
  // and logging success before that event hides a second process that never listened.
  await bindRevitPort(REVIT_WS_PORT);
  writePidLock(REVIT_WS_PORT);
  exitWhenClientDisconnects(REVIT_WS_PORT);

  await registerTools(server);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
