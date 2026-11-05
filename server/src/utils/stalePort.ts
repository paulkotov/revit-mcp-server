import { execFile } from "child_process";
import { readFileSync, unlinkSync, writeFileSync } from "fs";
import { tmpdir } from "os";
import path from "path";

const SERVER_SCRIPT = /[\\/]build[\\/]index\.js(?:"|')?(?:\s|$)/i;

function lockPath(port: number): string {
  return path.join(tmpdir(), `revit-mcp-server-${port}.pid`);
}

export function isRevitMcpServerCommand(commandLine: string): boolean {
  return SERVER_SCRIPT.test(commandLine);
}

export function writePidLock(port: number): void {
  writeFileSync(lockPath(port), `${process.pid}\n`);
}

export function clearPidLock(port: number): void {
  const file = lockPath(port);
  try {
    const owner = readFileSync(file, "utf8").trim();
    if (owner === String(process.pid)) unlinkSync(file);
  } catch {
    // The lock is already gone, or another process replaced it.
  }
}

function execText(command: string, args: string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(
      command,
      args,
      { windowsHide: true, timeout: 15000, encoding: "utf8" },
      (error, stdout) => {
        if (error) reject(error);
        else resolve(String(stdout).replace(/^\uFEFF/, ""));
      }
    );
  });
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return typeof error === "object" && error !== null && "code" in error && error.code === "EPERM";
  }
}

function readLockPid(port: number): number | undefined {
  try {
    const pid = Number(readFileSync(lockPath(port), "utf8").trim());
    return Number.isInteger(pid) && pid > 0 ? pid : undefined;
  } catch {
    return undefined;
  }
}

async function commandLineOf(pid: number): Promise<string | undefined> {
  try {
    const stdout =
      process.platform === "win32"
        ? await execText("powershell.exe", [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").CommandLine`,
          ])
        : await execText("ps", ["-p", String(pid), "-o", "args="]);
    const text = stdout.trim();
    return text || undefined;
  } catch {
    return undefined;
  }
}

async function isOurLiveServer(pid: number): Promise<boolean> {
  if (!Number.isInteger(pid) || pid <= 0 || pid === process.pid || !isAlive(pid)) return false;
  const command = await commandLineOf(pid);
  return command !== undefined && isRevitMcpServerCommand(command);
}

async function listeningPids(port: number): Promise<number[]> {
  try {
    if (process.platform === "win32") {
      const stdout = await execText("powershell.exe", [
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        `(Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue).OwningProcess`,
      ]);
      return [
        ...new Set(
          stdout
            .split(/\s+/)
            .map((value) => Number(value))
            .filter((pid) => Number.isInteger(pid) && pid > 0 && pid !== process.pid)
        ),
      ];
    }

    const stdout = await execText("lsof", ["-nP", `-iTCP:${port}`, "-sTCP:LISTEN", "-t"]);
    return [
      ...new Set(
        stdout
          .split(/\s+/)
          .map((value) => Number(value))
          .filter((pid) => Number.isInteger(pid) && pid > 0 && pid !== process.pid)
      ),
    ];
  } catch {
    return [];
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Stops a previous revit-mcp-server that is still listening on `port`.
 * Unrelated listeners are left running.
 */
export async function releaseStaleServer(port: number): Promise<boolean> {
  const targets = new Set<number>();
  const locked = readLockPid(port);
  if (locked !== undefined && (await isOurLiveServer(locked))) {
    targets.add(locked);
  } else {
    for (const pid of await listeningPids(port)) {
      if (await isOurLiveServer(pid)) targets.add(pid);
    }
  }

  if (targets.size === 0) return false;

  for (const pid of targets) {
    console.error(`Stopping stale revit-mcp-server process ${pid} so port ${port} can be used.`);
    try {
      process.kill(pid);
    } catch {
      // Already exited.
    }
  }

  const deadline = Date.now() + 2000;
  while (Date.now() < deadline) {
    if ([...targets].every((pid) => !isAlive(pid))) return true;
    await delay(50);
  }
  return false;
}
