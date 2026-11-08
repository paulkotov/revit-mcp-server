import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerGetModelMaterialsTool } from "./get_model_materials.js";
import { registerGetProjectInfoTool } from "./get_project_info.js";

export {
  registerGetModelMaterialsTool,
  toModelMaterialsCsv,
  writeModelMaterialsCsv,
} from "./get_model_materials.js";

export { registerGetProjectInfoTool } from "./get_project_info.js";

export function registerTools(server: McpServer): void {
  registerGetProjectInfoTool(server);
  registerGetModelMaterialsTool(server);
}
