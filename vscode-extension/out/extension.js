"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
const vscode = __importStar(require("vscode"));
const dcsRunner_1 = require("./dcsRunner");
const diagnostics_1 = require("./diagnostics");
let diagnosticCollection;
let analyzeInFlight;
function activate(context) {
    diagnosticCollection = vscode.languages.createDiagnosticCollection("dcs");
    context.subscriptions.push(diagnosticCollection);
    context.subscriptions.push(vscode.commands.registerCommand("dcs.analyze", () => triggerAnalyze("command")));
    context.subscriptions.push(vscode.workspace.onDidSaveTextDocument((doc) => {
        if (doc.languageId !== "csharp") {
            return;
        }
        const config = vscode.workspace.getConfiguration("dcs");
        if (config.get("analyzeOnSave", true)) {
            void triggerAnalyze("save");
        }
    }));
    const config = vscode.workspace.getConfiguration("dcs");
    if (config.get("analyzeOnOpen", true)) {
        void triggerAnalyze("open");
    }
}
function deactivate() {
    diagnosticCollection?.dispose();
    diagnosticCollection = undefined;
}
async function triggerAnalyze(reason) {
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
async function runAnalyze(workspaceRoot, reason) {
    const config = vscode.workspace.getConfiguration("dcs");
    const cliPath = config.get("cliPath", "dcs");
    const cacheDir = config.get("cacheDir", "");
    const strict = config.get("strict", false);
    try {
        const report = await (0, dcsRunner_1.runDcsAnalyze)({
            cliPath,
            workspaceRoot,
            cacheDir: cacheDir || undefined,
            strict,
        });
        if (diagnosticCollection) {
            (0, diagnostics_1.applyReportDiagnostics)(report, diagnosticCollection, workspaceRoot);
        }
        const errors = report.summary?.error_count ?? 0;
        vscode.window.setStatusBarMessage(`DCS (${reason}): ${errors} error(s)`, 5000);
    }
    catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        vscode.window.showErrorMessage(`DCS analyze failed: ${message}`);
    }
}
//# sourceMappingURL=extension.js.map