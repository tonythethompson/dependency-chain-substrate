import * as vscode from "vscode";
import { runDcsAnalyze } from "./dcsRunner";
import { applyReportDiagnostics } from "./diagnostics";

let diagnosticCollection: vscode.DiagnosticCollection | undefined;
let analyzeInFlight: Promise<void> | undefined;

export function activate(context: vscode.ExtensionContext): void {
  diagnosticCollection = vscode.languages.createDiagnosticCollection("dcs");
  context.subscriptions.push(diagnosticCollection);

  context.subscriptions.push(
    vscode.commands.registerCommand("dcs.analyze", () => triggerAnalyze("command")),
  );

  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument((doc) => {
      if (doc.languageId !== "csharp") {
        return;
      }
      const config = vscode.workspace.getConfiguration("dcs");
      if (config.get<boolean>("analyzeOnSave", true)) {
        void triggerAnalyze("save");
      }
    }),
  );

  const config = vscode.workspace.getConfiguration("dcs");
  if (config.get<boolean>("analyzeOnOpen", true)) {
    void triggerAnalyze("open");
  }
}

export function deactivate(): void {
  diagnosticCollection?.dispose();
  diagnosticCollection = undefined;
}

async function triggerAnalyze(reason: string): Promise<void> {
  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) {
    return;
  }

  if (analyzeInFlight) {
    await analyzeInFlight;
  }

  analyzeInFlight = runAnalyze(folder.uri.fsPath, reason).finally(() => {
    analyzeInFlight = undefined;
  });

  await analyzeInFlight;
}

async function runAnalyze(workspaceRoot: string, reason: string): Promise<void> {
  const config = vscode.workspace.getConfiguration("dcs");
  const cliPath = config.get<string>("cliPath", "dcs");
  const cacheDir = config.get<string>("cacheDir", "");
  const strict = config.get<boolean>("strict", false);

  try {
    const report = await runDcsAnalyze({
      cliPath,
      workspaceRoot,
      cacheDir: cacheDir || undefined,
      strict,
    });

    if (diagnosticCollection) {
      applyReportDiagnostics(report, diagnosticCollection, workspaceRoot);
    }

    const errors = report.summary?.error_count ?? 0;
    vscode.window.setStatusBarMessage(`DCS (${reason}): ${errors} error(s)`, 5000);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    vscode.window.showErrorMessage(`DCS analyze failed: ${message}`);
  }
}
