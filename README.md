# revit-mcp-server
Revit MCP server for AI connection

Technoligies: 
- client: Claude / ChatGPT / Grok ...
- server: NodeJS + Revit C# Plugin

MCP for communication with Autodesk Revit

The **MCP Server** (TypeScript) translates tool calls from AI clients into WebSocket messages. The **Revit Plugin** (C#) runs inside Revit, listens for those messages, and dispatches them to the **Command Set** (C#), which executes the actual Revit API operations and returns results back up the chain.

## Requirements

- **Node.js 18+** (for the MCP server)
- **Autodesk Revit 2020 - 2026** (any supported version)