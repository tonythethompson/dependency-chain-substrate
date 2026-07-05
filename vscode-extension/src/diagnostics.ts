import * as path from "node:path";
import * as vscode from "vscode";
import type { AnalysisFinding, AnalysisReport } from "./reportTypes";

const DIAGNOSTIC_SOURCE = "dependency-chain-substrate";

export function applyReportDiagnostics(
  report: AnalysisReport,
  collection: vscode.DiagnosticCollection,
  workspaceRoot: string,
): void {
  collection.clear();

  const byFile = new Map<string, vscode.Diagnostic[]>();

  for (const finding of report.findings) {
    if (finding.tier !== "actionable") {
      continue;
    }

    for (const site of finding.sites ?? []) {
      if (!site.file_path || !site.line) {
        continue;
      }

      const absolute = path.isAbsolute(site.file_path)
        ? site.file_path
        : path.join(workspaceRoot, site.file_path);

      const uri = vscode.Uri.file(absolute);
      const key = uri.toString();
      const list = byFile.get(key) ?? [];
      list.push(toDiagnostic(finding, site.line, site.column));
      byFile.set(key, list);
    }
  }

  for (const [fileKey, diagnostics] of byFile) {
    collection.set(vscode.Uri.parse(fileKey), diagnostics);
  }
}

function toDiagnostic(
  finding: AnalysisFinding,
  line: number,
  column?: number,
): vscode.Diagnostic {
  const startLine = Math.max(0, line - 1);
  const startChar = Math.max(0, (column ?? 1) - 1);
  const range = new vscode.Range(startLine, startChar, startLine, startChar + 1);

  const severity =
    finding.severity === "error"
      ? vscode.DiagnosticSeverity.Error
      : vscode.DiagnosticSeverity.Warning;

  const diagnostic = new vscode.Diagnostic(range, formatMessage(finding), severity);
  diagnostic.source = DIAGNOSTIC_SOURCE;
  diagnostic.code = finding.category;
  return diagnostic;
}

function formatMessage(finding: AnalysisFinding): string {
  const detail = finding.detail?.trim();
  return detail ? `${finding.title}: ${detail}` : finding.title;
}
