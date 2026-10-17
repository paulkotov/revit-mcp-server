import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { registerTools } from "./tools/registerTools.js";
import { connectionManager } from "./utils/ConnectionManager.js";

const server = new McpServer({
  name: "revit-mcp-server",
  version: "1.0.0",
});

const REVIT_WS_PORT = Number(process.env.REVIT_WS_PORT ?? 8080);

async function main() {
  // Host the WebSocket server the Revit add-in connects to.
  // Wait until the port is actually bound. `ws` reports EADDRINUSE asynchronously,
  // and logging success before that event hides a second process that never listened.
  await connectionManager.start(REVIT_WS_PORT);

  await registerTools(server);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
