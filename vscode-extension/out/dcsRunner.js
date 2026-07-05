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
exports.runDcsAnalyze = runDcsAnalyze;
const node_child_process_1 = require("node:child_process");
const fs = __importStar(require("node:fs/promises"));
const os = __importStar(require("node:os"));
const path = __importStar(require("node:path"));
async function runDcsAnalyze(options) {
    const reportPath = path.join(os.tmpdir(), `dcs-report-${Date.now()}-${Math.random().toString(16).slice(2)}.json`);
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
        const report = JSON.parse(raw);
        assertSupportedSchema(report.schema_version);
        return report;
    }
    finally {
        await fs.rm(reportPath, { force: true }).catch(() => undefined);
    }
}
function assertSupportedSchema(schemaVersion) {
    const major = schemaVersion.split(".")[0];
    if (major !== "1") {
        throw new Error(`Unsupported analysis report schema_version ${schemaVersion}; extension supports 1.x only.`);
    }
}
function execFileAsync(command, args) {
    return new Promise((resolve, reject) => {
        const child = (0, node_child_process_1.spawn)(command, args, { stdio: ["ignore", "pipe", "pipe"] });
        let stderr = "";
        child.stderr?.on("data", (chunk) => {
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
//# sourceMappingURL=dcsRunner.js.map