import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

interface ModelMaterial {
  name: string;
  materialClass: string | null;
  volumeM3: number;
  areaM2: number;
  lengthM: number;
}

interface MaterialsByLevel {
  name: string;
  elevationM: number | null;
  materials: ModelMaterial[];
}

interface ModelMaterials {
  projectName: string;
  projectPath: string;
  units: {
    volume: "m3";
    area: "m2";
    length: "m";
    elevation: "m";
  };
  levels: MaterialsByLevel[];
}

export function registerGetModelMaterialsTool(server: McpServer) {
  server.registerTool(
    "get-model-materials",
    {
      description: "Get the materials in the model sorting by level",
    },
    async () => {
      const materials = await withRevitConnection<ModelMaterials>("revit.getModelMaterials");
      return {
        content: [{ type: "text", text: JSON.stringify(materials, null, 2) }],
      };
    }
  );
}