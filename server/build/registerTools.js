import { z } from "zod";
export const registerTools = async (server) => {
    server.registerTool("get-revit-version", {
        description: "Get the version of Revit",
        inputSchema: {
            version: z.string().optional(),
        },
    }, async () => ({
        content: [{ type: "text", text: "unknown" }],
    }));
};
