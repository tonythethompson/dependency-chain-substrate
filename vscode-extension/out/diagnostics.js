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
exports.applyReportDiagnostics = applyReportDiagnostics;
const path = __importStar(require("node:path"));
const vscode = __importStar(require("vscode"));
const DIAGNOSTIC_SOURCE = "dependency-chain-substrate";
function applyReportDiagnostics(report, collection, workspaceRoot) {
    collection.clear();
    const byFile = new Map();
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
function toDiagnostic(finding, line, column) {
    const startLine = Math.max(0, line - 1);
    const startChar = Math.max(0, (column ?? 1) - 1);
    const range = new vscode.Range(startLine, startChar, startLine, startChar + 1);
    const severity = finding.severity === "error"
        ? vscode.DiagnosticSeverity.Error
        : vscode.DiagnosticSeverity.Warning;
    const diagnostic = new vscode.Diagnostic(range, formatMessage(finding), severity);
    diagnostic.source = DIAGNOSTIC_SOURCE;
    diagnostic.code = finding.category;
    return diagnostic;
}
function formatMessage(finding) {
    const detail = finding.detail?.trim();
    return detail ? `${finding.title}: ${detail}` : finding.title;
}
//# sourceMappingURL=diagnostics.js.map