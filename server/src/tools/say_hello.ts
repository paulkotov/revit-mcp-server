import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
// import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSayHelloTool(server: McpServer) {
  server.registerTool(
    "say-hello",
    {
      description: "Say hello to the user",
      inputSchema: z.object({
        name: z.string(),
      }),
    },
    async (input) => {
      return {
        content: [{ type: "text", text: `Hello, ${input.name}!` }],
      };
    }
  );
}