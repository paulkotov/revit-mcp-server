import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerTools as registerActionsTools } from "./actions/index.js";
import { registerTools as registerBatchTools } from "./batch/index.js";
import { registerTools as registerCadMepTools } from "./cad-mep/index.js";
import { registerTools as registerCodeTools } from "./code/index.js";
import { registerTools as registerCoreTools } from "./core/index.js";
import { registerTools as registerDocumentsTools } from "./documents/index.js";
import { registerTools as registerExportTools } from "./export/index.js";
import { registerTools as registerFamilyTools } from "./family/index.js";
import { registerTools as registerLinksTools } from "./links/index.js";
import { registerTools as registerReadTools } from "./read/index.js";
import { registerTools as registerViewsSheetsTools } from "./views-sheets/index.js";

const toolGroups = [
  ["core", registerCoreTools],
  ["read", registerReadTools],
  ["actions", registerActionsTools],
  ["documents", registerDocumentsTools],
  ["views-sheets", registerViewsSheetsTools],
  ["family", registerFamilyTools],
  ["links", registerLinksTools],
  ["cad-mep", registerCadMepTools],
  ["export", registerExportTools],
  ["batch", registerBatchTools],
  ["code", registerCodeTools],
] as const;

export function registerTools(server: McpServer): void {
  for (const [name, register] of toolGroups) {
    register(server);
    console.error(`Tool group registered: ${name}`);
  }
}
