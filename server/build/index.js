import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { registerTools } from "./registerTools.js";
const server = new McpServer({
    name: "mcp-server-for-revit",
    version: "1.0.0",
});
async function main() {
    await registerTools(server);
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("Revit MCP Server start success");
}
main().catch((error) => {
    console.error("Error starting Revit MCP Server:", error);
    process.exit(1);
});
