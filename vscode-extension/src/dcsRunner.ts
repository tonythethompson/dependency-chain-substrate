import { spawn } from "node:child_process";
import * as fs from "node:fs/promises";
import * as os from "node:os";
import * as path from "node:path";
import type { AnalysisReport } from "./reportTypes";

export interface DcsAnalyzeOptions {
  cliPath: string;
  workspaceRoot: string;
  cacheDir?: string;
  strict?: boolean;
}

export async function runDcsAnalyze(options: DcsAnalyzeOptions): Promise<AnalysisReport> {
  const reportPath = path.join(
    os.tmpdir(),
    `dcs-report-${Date.now()}-${Math.random().toString(16).slice(2)}.json`,
  );

  const args = [
    "analyze",
    options.workspaceRoot,
    "--format",
    "json",
    "--report-out",
    reportPath,
  ];

  if (options.cacheDir) {
    args.push("--cache-dir", options.cacheDir);
  }

  if (options.strict) {
    args.push("--strict");
  }

  try {
    await execFileAsync(options.cliPath, args);
    const raw = await fs.readFile(reportPath, "utf8");
    const report = JSON.parse(raw) as AnalysisReport;
    assertSupportedSchema(report.schema_version);
    return report;
  } finally {
    await fs.rm(reportPath, { force: true }).catch(() => undefined);
  }
}

function assertSupportedSchema(schemaVersion: string): void {
  const major = schemaVersion.split(".")[0];
  if (major !== "1") {
    throw new Error(
      `Unsupported analysis report schema_version ${schemaVersion}; extension supports 1.x only.`,
    );
  }
}

function execFileAsync(command: string, args: string[]): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: ["ignore", "pipe", "pipe"] });
    let stderr = "";

    child.stderr?.on("data", (chunk: Buffer) => {
      stderr += chunk.toString();
    });

    child.on("error", reject);
    child.on("close", (code) => {
      // dcs analyze exits 1 when findings include errors — still emits report JSON.
      if (code === 0 || code === 1) {
        resolve();
        return;
      }

      reject(new Error(`dcs analyze failed (exit ${code}): ${stderr.trim()}`));
    });
  });
}
