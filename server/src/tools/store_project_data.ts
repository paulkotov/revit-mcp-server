import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { getProjectByName, storeProject } from "../database/service.js";

const inputSchema = z.object({
  project_name: z.string().min(1).describe("The name of the Revit project"),
  project_path: z.string().optional().describe("File path to the project"),
  project_number: z.string().optional().describe("Project number or identifier"),
  project_address: z.string().optional().describe("Project address or location"),
  client_name: z.string().optional().describe("Client name"),
  project_status: z.string().optional().describe("Project status, for example Active, Completed, On Hold"),
  author: z.string().optional().describe("Project author or creator"),
  metadata: z.record(z.string(), z.string()).optional().describe("Extra project fields as key-value strings"),
});

export function registerStoreProjectDataTool(server: McpServer) {
  server.registerTool(
    "store-project-data",
    {
      description:
        "Store or update Revit project metadata in the local database. " +
        "Does not read Revit. Omitted fields on an existing project are left unchanged.",
      inputSchema,
    },
    async (args) => {
      try {
        const projectId = storeProject(args);
        const project = getProjectByName(args.project_name);
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                { success: true, message: "Project data stored.", project_id: projectId, project },
                null,
                2
              ),
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `Failed to store project data: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
