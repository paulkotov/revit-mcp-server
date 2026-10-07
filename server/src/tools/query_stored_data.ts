import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import {
  getAllMaterialsWithProject,
  getAllProjects,
  getMaterialTakeoff,
  getProjectById,
  getProjectByName,
  getStats,
} from "../database/service.js";

const inputSchema = z.object({
  query_type: z
    .enum([
      "all_projects",
      "project_by_id",
      "project_by_name",
      "materials_by_project_id",
      "materials_by_project_name",
      "all_materials",
      "stats",
    ])
    .describe("Which saved snapshot to read"),
  project_id: z
    .number()
    .int()
    .optional()
    .describe("Required for project_by_id and materials_by_project_id"),
  project_name: z
    .string()
    .optional()
    .describe("Required for project_by_name and materials_by_project_name"),
});

function missing(field: string) {
  return {
    isError: true as const,
    content: [{ type: "text" as const, text: `${field} is required for this query.` }],
  };
}

function missingProject(label: string) {
  return {
    isError: true as const,
    content: [{ type: "text" as const, text: `Project ${label} was not found in the local database.` }],
  };
}

function jsonData(queryType: string, data: unknown) {
  return {
    content: [
      {
        type: "text" as const,
        text: JSON.stringify({ success: true, query_type: queryType, data }, null, 2),
      },
    ],
  };
}

export function registerQueryStoredDataTool(server: McpServer) {
  server.registerTool(
    "query-stored-data",
    {
      description:
        "Read saved project cards and material quantities from the local database. " +
        "Does not contact Revit. Quantities appear here only after extract-material-quantities.",
      inputSchema,
    },
    async (args) => {
      try {
        switch (args.query_type) {
          case "all_projects":
            return jsonData(args.query_type, getAllProjects());
          case "project_by_id": {
            if (args.project_id === undefined) return missing("project_id");
            const project = getProjectById(args.project_id);
            if (!project) return missingProject(`id ${args.project_id}`);
            return jsonData(args.query_type, project);
          }
          case "project_by_name": {
            if (!args.project_name) return missing("project_name");
            const project = getProjectByName(args.project_name);
            if (!project) return missingProject(`"${args.project_name}"`);
            return jsonData(args.query_type, project);
          }
          case "materials_by_project_id": {
            if (args.project_id === undefined) return missing("project_id");
            return jsonData(args.query_type, getMaterialTakeoff(args.project_id));
          }
          case "materials_by_project_name": {
            if (!args.project_name) return missing("project_name");
            const project = getProjectByName(args.project_name);
            if (!project) return missingProject(`"${args.project_name}"`);
            return jsonData(args.query_type, getMaterialTakeoff(project.id));
          }
          case "all_materials":
            return jsonData(args.query_type, getAllMaterialsWithProject());
          case "stats":
            return jsonData(args.query_type, getStats());
          default:
            return {
              isError: true,
              content: [{ type: "text" as const, text: `Unknown query type: ${args.query_type}` }],
            };
        }
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text" as const,
              text: `Failed to query stored data: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
