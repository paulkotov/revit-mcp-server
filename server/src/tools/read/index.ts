import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerGetModelMaterialsTool } from "./get_model_materials.js";

export {
  registerGetModelMaterialsTool,
  toModelMaterialsCsv,
  writeModelMaterialsCsv,
} from "./get_model_materials.js";

export function registerTools(server: McpServer): void {
  registerGetModelMaterialsTool(server);
}
