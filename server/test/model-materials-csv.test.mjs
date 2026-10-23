import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { toModelMaterialsCsv, writeModelMaterialsCsv } from "../build/tools/get_model_materials.js";

test("material quantities become one CSV table", () => {
  const csv = toModelMaterialsCsv({
    projectName: "House, A",
    projectPath: "C:\\Models\\house.rvt",
    units: { volume: "m3", area: "m2", length: "m", elevation: "m" },
    levels: [
      {
        name: "Level 1",
        elevationM: 0,
        materials: [
          {
            name: 'Concrete "B25"',
            materialClass: "Concrete",
            volumeM3: 12.5,
            areaM2: 40,
            lengthM: 0,
          },
        ],
      },
      {
        name: "(no level)",
        elevationM: null,
        materials: [
          {
            name: "A500",
            materialClass: null,
            volumeM3: 0.1,
            areaM2: 0,
            lengthM: 18.25,
          },
        ],
      },
    ],
  });

  assert.equal(
    csv,
    [
      "projectName,projectPath,level,elevationM,material,materialClass,volumeM3,areaM2,lengthM",
      '"House, A",C:\\Models\\house.rvt,Level 1,0,"Concrete ""B25""",Concrete,12.5,40,0',
      '"House, A",C:\\Models\\house.rvt,(no level),,A500,,0.1,0,18.25',
    ].join("\n")
  );
});

test("material quantities are written as a downloadable CSV file", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "model-materials-"));
  try {
    const saved = writeModelMaterialsCsv(
      {
        projectName: "House",
        projectPath: "C:\\Models\\house.rvt",
        units: { volume: "m3", area: "m2", length: "m", elevation: "m" },
        levels: [],
      },
      directory
    );

    assert.match(saved.fileName, /^model-materials-\d{8}-\d{6}\.csv$/);
    assert.equal(saved.filePath, path.join(directory, saved.fileName));
    assert.equal(saved.fileUri.startsWith("file:///"), true);
    const file = fs.readFileSync(saved.filePath, "utf8");
    assert.equal(file, `\uFEFF${saved.csv}`);
    assert.match(file, /^\uFEFFprojectName,projectPath,level,/);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
