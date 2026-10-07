import Database from "better-sqlite3";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const here = dirname(fileURLToPath(import.meta.url));

// Compiled file lives in server/build/database. Two levels up is server/,
// so the snapshot survives `npm run clean` (that command deletes build/).
const databasePath = join(here, "..", "..", "revit-data.db");

export const db = new Database(databasePath);

db.pragma("foreign_keys = ON");

export function initializeDatabase(): void {
  db.exec(`
    CREATE TABLE IF NOT EXISTS projects (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_name TEXT NOT NULL UNIQUE,
      project_path TEXT,
      project_number TEXT,
      project_address TEXT,
      client_name TEXT,
      project_status TEXT,
      author TEXT,
      timestamp INTEGER NOT NULL,
      last_updated INTEGER NOT NULL,
      metadata TEXT
    );

    CREATE TABLE IF NOT EXISTS material_quantities (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_id INTEGER NOT NULL,
      level_name TEXT NOT NULL,
      level_elevation REAL,
      kind TEXT NOT NULL,
      material_name TEXT NOT NULL,
      material_class TEXT,
      volume_m3 REAL NOT NULL DEFAULT 0,
      area_m2 REAL NOT NULL DEFAULT 0,
      length_m REAL NOT NULL DEFAULT 0,
      captured_at INTEGER NOT NULL,
      FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
      UNIQUE(project_id, level_name, kind, material_name)
    );

    CREATE INDEX IF NOT EXISTS idx_projects_timestamp ON projects(timestamp);
    CREATE INDEX IF NOT EXISTS idx_materials_project_id ON material_quantities(project_id);

    DROP TABLE IF EXISTS rooms;
  `);
}

initializeDatabase();
