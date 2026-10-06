import { WebSocketServer, WebSocket } from "ws";
import { randomUUID } from "crypto";
/**
 * Hosts the WebSocket server that the Revit add-in (WS client) connects to, and
 * provides JSON-RPC request/response correlation by `id`.
 *
 * On the TCP level Node is the server and Revit is the client; on the JSON-RPC
 * level Node is the client (sends requests) and Revit is the server (responds).
 */
class RevitConnectionManager {
    wss;
    revitSocket;
    pending = new Map();
    start(port = 8080) {
        if (this.wss)
            return;
        this.wss = new WebSocketServer({ port });
        console.error(`Revit WS server listening on ws://127.0.0.1:${port}`);
        this.wss.on("connection", (socket) => {
            console.error("Revit add-in connected.");
            this.revitSocket = socket;
            socket.on("message", (data) => this.onMessage(data.toString()));
            socket.on("close", () => {
                console.error("Revit add-in disconnected.");
                if (this.revitSocket === socket)
                    this.revitSocket = undefined;
            });
            socket.on("error", (err) => console.error("Revit WS error:", err.message));
        });
    }
    get isConnected() {
        return this.revitSocket?.readyState === WebSocket.OPEN;
    }
    /**
     * Sends a JSON-RPC request to Revit and resolves with its result.
     * Rejects on JSON-RPC error, timeout, or missing connection.
     */
    send(method, params, timeoutMs = 60000) {
        return new Promise((resolve, reject) => {
            if (!this.revitSocket || this.revitSocket.readyState !== WebSocket.OPEN) {
                reject(new Error("Revit is not connected."));
                return;
            }
            const id = randomUUID();
            const timer = setTimeout(() => {
                this.pending.delete(id);
                reject(new Error(`Revit request '${method}' timed out after ${timeoutMs} ms.`));
            }, timeoutMs);
            this.pending.set(id, {
                resolve: resolve,
                reject,
                timer,
            });
            this.revitSocket.send(JSON.stringify({ jsonrpc: "2.0", id, method, params }));
        });
    }
    onMessage(raw) {
        let response;
        try {
            response = JSON.parse(raw);
        }
        catch {
            console.error("Received non-JSON message from Revit:", raw);
            return;
        }
        const id = response.id;
        if (id === null || id === undefined)
            return; // notification, nothing to correlate
        const pending = this.pending.get(String(id));
        if (!pending)
            return;
        this.pending.delete(String(id));
        clearTimeout(pending.timer);
        if (response.error) {
            pending.reject(new Error(`Revit error ${response.error.code}: ${response.error.message}`));
        }
        else {
            pending.resolve(response.result);
        }
    }
}
export const connectionManager = new RevitConnectionManager();
/**
 * Convenience helper for MCP tools: forwards a method call to Revit over WS.
 */
export async function withRevitConnection(method, params) {
    return connectionManager.send(method, params);
}
