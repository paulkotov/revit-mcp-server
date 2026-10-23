import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
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
      description: [
        "Read material quantities from the active Revit document.",
        "Call this when the user asks which materials are in the model, or for a quantity takeoff, volumes, areas, or rebar lengths.",
        "It takes no parameters. An open document is required.",
        "The tool sums materials of placed model elements and groups them by level.",
        "View-specific elements are skipped. Paint is included as area.",
        "Rebar is reported by bar length, and by volume when Revit provides it, so it is not lost inside the host concrete.",
        "Elements without a level are grouped as \"(no level)\". Near-zero quantities are omitted.",
        "The tool already converts the Revit result to CSV and writes that file to disk. Do not convert it again and do not write another copy.",
        "Present the CSV as one markdown table, keeping the row order and the numbers as given.",
        "In the same reply, tell the user they can download the CSV and include the file path from the tool result.",
        "Do not add rows that are not in the CSV, and do not convert the units.",
        "Columns: projectName, projectPath, level, elevationM, material, materialClass, volumeM3, areaM2, lengthM.",
        "elevationM and lengthM are meters, volumeM3 is cubic meters, areaM2 is square meters.",
        "lengthM is meaningful for rebar. Rows are ordered by level elevation, then by level name, then by material name.",
      ].join(" "),
    },
    async () => {
      const materials = await withRevitConnection<ModelMaterials>("revit.getModelMaterials");
      const saved = writeModelMaterialsCsv(materials);
      return {
        content: [
          {
            type: "text",
            text: [
              `CSV file ready to download: ${saved.filePath}`,
              "Tell the user they can download this CSV file, and include the path.",
              "",
              saved.csv,
            ].join("\n"),
          },
          {
            type: "resource_link",
            uri: saved.fileUri,
            name: saved.fileName,
            mimeType: "text/csv",
            description: "Download the material quantities CSV",
          },
        ],
      };
    }
  );
}

const CSV_HEADER = [
  "projectName",
  "projectPath",
  "level",
  "elevationM",
  "material",
  "materialClass",
  "volumeM3",
  "areaM2",
  "lengthM",
];

export function toModelMaterialsCsv(materials: ModelMaterials): string {
  const rows: string[][] = [CSV_HEADER];

  for (const level of materials.levels ?? []) {
    for (const material of level.materials ?? []) {
      rows.push([
        materials.projectName ?? "",
        materials.projectPath ?? "",
        level.name ?? "",
        level.elevationM == null ? "" : String(level.elevationM),
        material.name ?? "",
        material.materialClass ?? "",
        String(material.volumeM3 ?? 0),
        String(material.areaM2 ?? 0),
        String(material.lengthM ?? 0),
      ]);
    }
  }

  return rows.map((row) => row.map(csvCell).join(",")).join("\n");
}

function csvCell(value: string): string {
  if (/[",\n\r]/.test(value)) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

const EXPORTS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../exports");

export function writeModelMaterialsCsv(
  materials: ModelMaterials,
  directory = EXPORTS_DIR
): { csv: string; filePath: string; fileName: string; fileUri: string } {
  const csv = toModelMaterialsCsv(materials);
  const fileName = `model-materials-${fileStamp()}.csv`;
  const filePath = path.join(directory, fileName);
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(filePath, `\uFEFF${csv}`, "utf8");
  return {
    csv,
    filePath,
    fileName,
    fileUri: pathToFileUri(filePath),
  };
}

function fileStamp(): string {
  const now = new Date();
  const part = (value: number) => String(value).padStart(2, "0");
  return [
    now.getFullYear(),
    part(now.getMonth() + 1),
    part(now.getDate()),
    "-",
    part(now.getHours()),
    part(now.getMinutes()),
    part(now.getSeconds()),
  ].join("");
}

function pathToFileUri(filePath: string): string {
  const resolved = path.resolve(filePath).replace(/\\/g, "/");
  return encodeURI(`file:///${resolved}`);
}