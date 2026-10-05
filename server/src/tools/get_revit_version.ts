import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

interface RevitVersion {
  versionName: string;
  versionNumber: string;
  versionBuild: string;
}

export function registerGetRevitVersionTool(server: McpServer) {
  server.registerTool(
    "get-revit-version",
    {
      description: "Get the version of the running Autodesk Revit instance",
    },
    async () => {
      try {
        const version = await withRevitConnection<RevitVersion>("revit.getVersion");
        return {
          content: [{ type: "text", text: JSON.stringify(version, null, 2) }],
        };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `Failed to get Revit version: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
