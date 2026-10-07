import { db } from "./db.js";

export interface ProjectData {
  project_name: string;
  project_path?: string;
  project_number?: string;
  project_address?: string;
  client_name?: string;
  project_status?: string;
  author?: string;
  metadata?: Record<string, string>;
}

export interface MaterialQuantityInput {
  levelName: string;
  elevationM: number | null;
  kind: string;
  name: string;
  materialClass: string | null;
  volumeM3: number;
  areaM2: number;
  lengthM: number;
}

export interface StoredMaterialQuantity {
  id: number;
  project_id: number;
  project_name?: string;
  level_name: string;
  level_elevation: number | null;
  kind: string;
  material_name: string;
  material_class: string | null;
  volume_m3: number;
  area_m2: number;
  length_m: number;
  captured_at: string;
}

export interface MaterialLevelGroup {
  level_name: string;
  level_elevation: number | null;
  materials: StoredMaterialQuantity[];
}

export interface MaterialTakeoffSnapshot {
  captured_at: string | null;
  levels: MaterialLevelGroup[];
}

export interface StoredProject extends ProjectData {
  id: number;
  timestamp: string;
  last_updated: string;
  metadata?: Record<string, string>;
}


interface ProjectRow {
  id: number;
  project_name: string;
  project_path: string | null;
  project_number: string | null;
  project_address: string | null;
  client_name: string | null;
  project_status: string | null;
  author: string | null;
  timestamp: number;
  last_updated: number;
  metadata: string | null;
}

interface MaterialRow {
  id: number;
  project_id: number;
  project_name?: string;
  level_name: string;
  level_elevation: number | null;
  kind: string;
  material_name: string;
  material_class: string | null;
  volume_m3: number;
  area_m2: number;
  length_m: number;
  captured_at: number;
}

const projectColumns = `
  id, project_name, project_path, project_number, project_address,
  client_name, project_status, author, timestamp, last_updated, metadata
`;

const materialColumns = `
  id, project_id, level_name, level_elevation, kind, material_name,
  material_class, volume_m3, area_m2, length_m, captured_at
`;

function parseMetadata(raw: string | null): Record<string, string> | undefined {
  if (!raw) return undefined;
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
    const record: Record<string, string> = {};
    for (const [key, item] of Object.entries(value)) {
      if (typeof item === "string") record[key] = item;
    }
    return record;
  } catch {
    return undefined;
  }
}

function toProject(row: ProjectRow): StoredProject {
  return {
    id: row.id,
    project_name: row.project_name,
    project_path: row.project_path ?? undefined,
    project_number: row.project_number ?? undefined,
    project_address: row.project_address ?? undefined,
    client_name: row.client_name ?? undefined,
    project_status: row.project_status ?? undefined,
    author: row.author ?? undefined,
    timestamp: new Date(row.timestamp).toISOString(),
    last_updated: new Date(row.last_updated).toISOString(),
    metadata: parseMetadata(row.metadata),
  };
}

function toMaterial(row: MaterialRow): StoredMaterialQuantity {
  return {
    id: row.id,
    project_id: row.project_id,
    project_name: row.project_name,
    level_name: row.level_name,
    level_elevation: row.level_elevation,
    kind: row.kind,
    material_name: row.material_name,
    material_class: row.material_class,
    volume_m3: row.volume_m3,
    area_m2: row.area_m2,
    length_m: row.length_m,
    captured_at: new Date(row.captured_at).toISOString(),
  };
}

function groupByLevel(rows: StoredMaterialQuantity[]): MaterialLevelGroup[] {
  const groups: MaterialLevelGroup[] = [];
  for (const row of rows) {
    const current = groups[groups.length - 1];
    if (
      current &&
      current.level_name === row.level_name &&
      current.level_elevation === row.level_elevation
    ) {
      current.materials.push(row);
      continue;
    }

    groups.push({
      level_name: row.level_name,
      level_elevation: row.level_elevation,
      materials: [row],
    });
  }
  return groups;
}

function keep<T>(next: T | undefined, previous: T | null): T | null {
  return next !== undefined ? next : previous;
}

/** Inserts a project or updates only the fields present in `data`. */
export function storeProject(data: ProjectData): number {
  const timestamp = Date.now();
  const existing = db
    .prepare("SELECT " + projectColumns + " FROM projects WHERE project_name = ?")
    .get(data.project_name) as ProjectRow | undefined;

  if (existing) {
    db.prepare(`
      UPDATE projects SET
        project_path = ?,
        project_number = ?,
        project_address = ?,
        client_name = ?,
        project_status = ?,
        author = ?,
        last_updated = ?,
        metadata = ?
      WHERE id = ?
    `).run(
      keep(data.project_path, existing.project_path),
      keep(data.project_number, existing.project_number),
      keep(data.project_address, existing.project_address),
      keep(data.client_name, existing.client_name),
      keep(data.project_status, existing.project_status),
      keep(data.author, existing.author),
      timestamp,
      data.metadata !== undefined ? JSON.stringify(data.metadata) : existing.metadata,
      existing.id
    );
    return existing.id;
  }

  const result = db.prepare(`
    INSERT INTO projects (
      project_name, project_path, project_number, project_address,
      client_name, project_status, author, timestamp, last_updated, metadata
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).run(
    data.project_name,
    data.project_path ?? null,
    data.project_number ?? null,
    data.project_address ?? null,
    data.client_name ?? null,
    data.project_status ?? null,
    data.author ?? null,
    timestamp,
    timestamp,
    data.metadata ? JSON.stringify(data.metadata) : null
  );
  return Number(result.lastInsertRowid);
}

/** Replaces the saved takeoff for a project with the quantities just read from Revit. */
export function replaceMaterialQuantities(projectId: number, rows: MaterialQuantityInput[]): number {
  const capturedAt = Date.now();
  const replace = db.transaction((items: MaterialQuantityInput[]) => {
    db.prepare("DELETE FROM material_quantities WHERE project_id = ?").run(projectId);
    const insert = db.prepare(`
      INSERT INTO material_quantities (
        project_id, level_name, level_elevation, kind, material_name,
        material_class, volume_m3, area_m2, length_m, captured_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);
    for (const item of items) {
      insert.run(
        projectId,
        item.levelName,
        item.elevationM,
        item.kind,
        item.name,
        item.materialClass,
        item.volumeM3,
        item.areaM2,
        item.lengthM,
        capturedAt
      );
    }
    return items.length;
  });
  return replace(rows);
}

export function getAllProjects(): StoredProject[] {
  const rows = db
    .prepare(`SELECT ${projectColumns} FROM projects ORDER BY last_updated DESC`)
    .all() as ProjectRow[];
  return rows.map(toProject);
}

export function getProjectById(projectId: number): StoredProject | null {
  const row = db
    .prepare(`SELECT ${projectColumns} FROM projects WHERE id = ?`)
    .get(projectId) as ProjectRow | undefined;
  return row ? toProject(row) : null;
}

export function getProjectByName(projectName: string): StoredProject | null {
  const row = db
    .prepare(`SELECT ${projectColumns} FROM projects WHERE project_name = ?`)
    .get(projectName) as ProjectRow | undefined;
  return row ? toProject(row) : null;
}

const materialOrder = `
  ORDER BY level_elevation IS NULL, level_elevation, level_name, kind, material_name
`;

export function getMaterialTakeoff(projectId: number): MaterialTakeoffSnapshot {
  const rows = db
    .prepare(`SELECT ${materialColumns} FROM material_quantities WHERE project_id = ? ${materialOrder}`)
    .all(projectId) as MaterialRow[];
  const materials = rows.map(toMaterial);
  return {
    captured_at: materials[0]?.captured_at ?? null,
    levels: groupByLevel(materials),
  };
}

export function getAllMaterialsWithProject(): StoredMaterialQuantity[] {
  const rows = db.prepare(`
    SELECT
      q.id, q.project_id, q.level_name, q.level_elevation, q.kind, q.material_name,
      q.material_class, q.volume_m3, q.area_m2, q.length_m, q.captured_at,
      p.project_name
    FROM material_quantities q
    JOIN projects p ON q.project_id = p.id
    ORDER BY p.project_name, q.level_elevation IS NULL, q.level_elevation, q.level_name, q.kind, q.material_name
  `).all() as MaterialRow[];
  return rows.map(toMaterial);
}

export function getStats(): { total_projects: number; total_material_rows: number } {
  const projectCount = db.prepare("SELECT COUNT(*) AS count FROM projects").get() as { count: number };
  const materialCount = db.prepare("SELECT COUNT(*) AS count FROM material_quantities").get() as { count: number };
  return {
    total_projects: projectCount.count,
    total_material_rows: materialCount.count,
  };
}
