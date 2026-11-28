import type { WorkflowDiagnosticDto } from '../../../../api/reviewEngine'

export function diagnosticCounts(diagnostics: readonly WorkflowDiagnosticDto[]) {
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'Error').length
  const warnings = diagnostics.filter((diagnostic) => diagnostic.severity === 'Warning').length
  const label = [
    errors > 0 && (errors + (errors === 1 ? ' error' : ' errors')),
    warnings > 0 && (warnings + (warnings === 1 ? ' warning' : ' warnings')),
  ].filter(Boolean).join(', ')
  return { errors, label }
}
