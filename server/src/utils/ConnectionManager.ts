import { WebSocketServer, WebSocket } from "ws";
import { randomUUID } from "crypto";

interface DataExchangeError {
  code: number;
  message: string;
  data?: unknown;
}

interface DataExchangeResponse {
  jsonrpc: "2.0";
  id: string | number | null;
  result?: unknown;
  error?: DataExchangeError;
}

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
  timer: NodeJS.Timeout;
}

/**
 * Hosts the WebSocket server that the Revit add-in (WS client) connects to, and
 * provides JSON-RPC request/response correlation by `id`.
 *
 * On the TCP level Node is the server and Revit is the client; on the JSON-RPC
 * level Node is the client (sends requests) and Revit is the server (responds).
 */
class RevitConnectionManager {
  private wss?: WebSocketServer;
  private revitSocket?: WebSocket;
  private readonly pending = new Map<string, PendingRequest>();

  start(port = 8080): void {
    if (this.wss) return;

    this.wss = new WebSocketServer({ port });
    console.error(`Revit WS server listening on ws://127.0.0.1:${port}`);

    this.wss.on("connection", (socket) => {
      console.error("Revit add-in connected.");
      this.revitSocket = socket;

      socket.on("message", (data) => this.onMessage(data.toString()));
      socket.on("close", () => {
        console.error("Revit add-in disconnected.");
        if (this.revitSocket === socket) this.revitSocket = undefined;
      });
      socket.on("error", (err) => console.error("Revit WS error:", err.message));
    });
  }

  get isConnected(): boolean {
    return this.revitSocket?.readyState === WebSocket.OPEN;
  }

  /**
   * Sends a JSON-RPC request to Revit and resolves with its result.
   * Rejects on JSON-RPC error, timeout, or missing connection.
   */
  send<T = unknown>(method: string, params?: unknown, timeoutMs = 60000): Promise<T> {
    return new Promise<T>((resolve, reject) => {
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
        resolve: resolve as (value: unknown) => void,
        reject,
        timer,
      });

      this.revitSocket.send(
        JSON.stringify({ jsonrpc: "2.0", id, method, params })
      );
    });
  }

  private onMessage(raw: string): void {
    let response: DataExchangeResponse;
    try {
      response = JSON.parse(raw) as DataExchangeResponse;
    } catch {
      console.error("Received non-JSON message from Revit:", raw);
      return;
    }

    const id = response.id;
    if (id === null || id === undefined) return; // notification, nothing to correlate

    const pending = this.pending.get(String(id));
    if (!pending) return;

    this.pending.delete(String(id));
    clearTimeout(pending.timer);

    if (response.error) {
      pending.reject(
        new Error(`Revit error ${response.error.code}: ${response.error.message}`)
      );
    } else {
      pending.resolve(response.result);
    }
  }
}

export const connectionManager = new RevitConnectionManager();

/**
 * Convenience helper for MCP tools: forwards a method call to Revit over WS.
 */
export async function withRevitConnection<T = unknown>(
  method: string,
  params?: unknown
): Promise<T> {
  return connectionManager.send<T>(method, params);
}
