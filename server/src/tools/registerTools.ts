import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import path from "path";
import fs from "fs";
import { fileURLToPath } from "url";
// import { z } from "zod";

const EXCLUDED_FILES = ["index.ts", "index.js", "registerTools.ts", "registerTools.js"];

export const registerTools = async (server: McpServer) => {
  // server.registerTool(
  //   "get-revit-version",
  //   {
  //     description: "Get the version of Revit",
  //     inputSchema: {
  //       version: z.string().optional(),
  //     },
  //   },
  //   async () => ({
  //     content: [{ type: "text", text: "unknown" }],
  //   })
  // );
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  const files = fs.readdirSync(__dirname);
  const toolFiles = files.filter(
    (file) =>
      (file.endsWith(".ts") || file.endsWith(".js")) &&
      !EXCLUDED_FILES.includes(file)
  );

  for (const file of toolFiles) {
    try {
      const importPath = `./${file.replace(/\.(ts|js)$/, ".js")}`;

      const module = await import(importPath);

      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`Tool registered: ${file}`);
      } else {
        console.warn(`Warning: No register function found in file ${file}`);
      }
    } catch (error) {
      console.error(`Error registering tool ${file}:`, error);
    }
  }
};
