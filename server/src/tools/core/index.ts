import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerGetRevitVersionTool } from "./get_revit_version.js";
import { registerSayHelloTool } from "./say_hello.js";

export { registerGetRevitVersionTool, registerSayHelloTool };

export function registerTools(server: McpServer): void {
  registerSayHelloTool(server);
  registerGetRevitVersionTool(server);
}
