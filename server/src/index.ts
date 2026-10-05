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
  connectionManager.start(REVIT_WS_PORT);

  await registerTools(server);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
