import { z } from "zod";
// import { withRevitConnection } from "../utils/ConnectionManager.js";
export function registerSayHelloTool(server) {
    server.registerTool("say-hello", {
        description: "Say hello to the user",
        inputSchema: z.object({
            name: z.string(),
        }),
    }, async (input) => {
        return {
            content: [{ type: "text", text: `Hello, ${input.name}!` }],
        };
    });
}
