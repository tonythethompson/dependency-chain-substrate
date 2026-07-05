/** Analysis report JSON consumed by the extension (ADR-006 public API). */
export interface AnalysisReport {
  schema_version: string;
  summary: AnalysisSummary;
  findings: AnalysisFinding[];
}

export interface AnalysisSummary {
  has_errors: boolean;
  error_count?: number;
  leaked_count?: number;
  broken_count?: number;
  duplicate_count?: number;
}

export interface AnalysisFinding {
  finding_id: string;
  category: FindingCategory;
  severity: "error" | "warn";
  tier: "actionable" | "informational" | "parser_limit" | "intentional";
  title: string;
  detail?: string | null;
  sites?: AnalysisSite[];
}

export type FindingCategory =
  | "leaked"
  | "broken"
  | "duplicate"
  | "possible_duplicate"
  | "unresolved"
  | "orphaned"
  | "cycle"
  | "blind_spot";

export interface AnalysisSite {
  file_path: string;
  line: number;
  column?: number;
  display_name?: string;
}
