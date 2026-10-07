import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { randomUUID } from "node:crypto";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { registerTools } from "./tools/registerTools.js";
import { connectionManager } from "./utils/ConnectionManager.js";

const server = new McpServer({
  name: "revit-mcp-server",
  version: "1.0.0",
});

const REVIT_WS_PORT = Number(process.env.REVIT_WS_PORT ?? 8080);
const MCP_HTTP_PORT = Number(process.env.MCP_PORT ?? 2504);
const MCP_HTTP_PATH = "/mcp";

async function main() {
  connectionManager.start(REVIT_WS_PORT);
  await registerTools(server);

  if (process.argv.includes("--http")) {
    await listenHttp();
    return;
  }

  await server.connect(new StdioServerTransport());
  console.error("Revit MCP Server start success (stdio)");
}

async function listenHttp(): Promise<void> {
  const transport = new StreamableHTTPServerTransport({
    sessionIdGenerator: () => randomUUID(),
  });
  await server.connect(transport);

  const httpServer = createServer((req, res) => {
    void handleHttp(req, res, transport);
  });

  await new Promise<void>((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(MCP_HTTP_PORT, "127.0.0.1", () => {
      httpServer.off("error", reject);
      resolve();
    });
  });

  console.error(
    `Revit MCP Server listening at http://127.0.0.1:${MCP_HTTP_PORT}${MCP_HTTP_PATH} ` +
      `(Revit WebSocket ws://127.0.0.1:${REVIT_WS_PORT})`
  );
}

async function handleHttp(
  req: IncomingMessage,
  res: ServerResponse,
  transport: StreamableHTTPServerTransport
): Promise<void> {
  const path = new URL(req.url ?? "/", "http://127.0.0.1").pathname;
  if (path !== MCP_HTTP_PATH) {
    res.writeHead(404, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "Not found" }));
    return;
  }

  try {
    await transport.handleRequest(req, res);
  } catch (error) {
    console.error("MCP HTTP request failed:", error);
    if (!res.headersSent) {
      res.writeHead(500, { "content-type": "application/json" });
      res.end(JSON.stringify({ error: "Internal server error" }));
    }
  }
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
