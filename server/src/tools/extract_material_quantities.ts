import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { getMaterialTakeoff, replaceMaterialQuantities, storeProject } from "../database/service.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const inputSchema = z.object({
  project_name: z
    .string()
    .min(1)
    .optional()
    .describe("Project card to attach the snapshot to. Defaults to the open Revit document title."),
});

interface MaterialLine {
  kind: string;
  name: string;
  materialClass: string | null;
  volumeM3: number;
  areaM2: number;
  lengthM: number;
}

interface LevelTakeoff {
  name: string;
  elevationM: number | null;
  materials: MaterialLine[];
}

interface MaterialTakeoff {
  projectName: string;
  projectPath: string;
  units: { volume: string; area: string; length: string; elevation: string };
  levels: LevelTakeoff[];
}

export function registerExtractMaterialQuantitiesTool(server: McpServer) {
  server.registerTool(
    "extract-material-quantities",
    {
      description:
        "Read concrete, rebar, steel, masonry and timber quantities from the open Revit model, " +
        "grouped by level, and save that snapshot in the local database. " +
        "Replaces the previous snapshot for the project. Volumes are cubic meters, rebar lengths are meters.",
      inputSchema,
    },
    async (args) => {
      try {
        const takeoff = await withRevitConnection<MaterialTakeoff>("revit.getMaterialTakeoff");
        const projectName = args.project_name?.trim() || takeoff.projectName || "Untitled";
        const projectId = storeProject({
          project_name: projectName,
          project_path: takeoff.projectPath || undefined,
        });

        const rows = takeoff.levels.flatMap((level) =>
          level.materials.map((material) => ({
            levelName: level.name,
            elevationM: level.elevationM,
            kind: material.kind,
            name: material.name,
            materialClass: material.materialClass,
            volumeM3: material.volumeM3,
            areaM2: material.areaM2,
            lengthM: material.lengthM,
          }))
        );
        const stored = replaceMaterialQuantities(projectId, rows);
        const snapshot = getMaterialTakeoff(projectId);

        return {
          content: [
            {
              type: "text" as const,
              text: JSON.stringify(
                {
                  success: true,
                  project_id: projectId,
                  project_name: projectName,
                  rows_stored: stored,
                  units: takeoff.units,
                  captured_at: snapshot.captured_at,
                  levels: snapshot.levels,
                },
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
              type: "text" as const,
              text: `Failed to extract material quantities: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
