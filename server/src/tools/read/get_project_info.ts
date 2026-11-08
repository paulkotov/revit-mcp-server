import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../../utils/ConnectionManager.js";

interface ProjectDimensions {
  sizeXM: number | null;
  sizeYM: number | null;
  sizeZM: number | null;
  minXM: number | null;
  minYM: number | null;
  minZM: number | null;
  maxXM: number | null;
  maxYM: number | null;
  maxZM: number | null;
  elementCount: number;
}

interface ProjectLevel {
  name: string;
  elevationM: number;
}

interface ProjectInfo {
  projectName: string;
  projectNumber: string | null;
  buildingName: string | null;
  clientName: string | null;
  address: string | null;
  status: string | null;
  author: string | null;
  organizationName: string | null;
  organizationDescription: string | null;
  issueDate: string | null;
  fileName: string;
  projectPath: string;
  units: {
    length: "m";
    elevation: "m";
  };
  dimensions: ProjectDimensions;
  levels: ProjectLevel[];
}

export function registerGetProjectInfoTool(server: McpServer) {
  server.registerTool(
    "get-project-info",
    {
      description: [
        "Read general information about the active Revit project.",
        "Call this when the user asks for project information, overall model dimensions, or the list of levels and their names.",
        "It takes no parameters. An open document is required.",
        "The result has the project identity from Project Information: projectName, projectNumber, buildingName, clientName, address, status, author, organizationName, organizationDescription, issueDate, fileName, and projectPath.",
        "dimensions is the axis-aligned bounding box of host model elements, in meters.",
        "sizeXM, sizeYM, and sizeZM are the overall sizes along project X, Y, and Z. min* and max* are the box corners in project coordinates.",
        "These sizes follow the project axes, so a rotated building is not measured along its own walls.",
        "View-specific elements, CAD imports, Revit links, scope boxes, and elements in non-primary design options are excluded.",
        "When the model has no measurable geometry, the dimension numbers are null and elementCount is 0.",
        "levels lists every level. name is the level name. elevationM is its elevation relative to the project base point, in meters.",
        "Levels are ordered by elevationM, then by name.",
        "Present the project identity, then the three overall sizes in meters, then the levels as a markdown table with columns name and elevationM.",
        "Keep the numbers as given. Do not convert the units.",
      ].join(" "),
    },
    async () => {
      const info = await withRevitConnection<ProjectInfo>("revit.getProjectInfo");
      return {
        content: [{ type: "text", text: JSON.stringify(info, null, 2) }],
      };
    }
  );
}
