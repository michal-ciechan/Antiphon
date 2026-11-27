const COMMIT_STATUS_WARNING = /^status antiphon\/(?:review|approval) not posted: /

/** The executor appends refused commit statuses to the summary, before any routing failure. */
export function splitExecutionOutput(summary: string | null) {
  const body: string[] = []
  const warnings: string[][] = []
  let part = body
  for (const line of summary?.split(/\r?\n/) ?? []) {
    if (COMMIT_STATUS_WARNING.test(line)) {
      part = []
      warnings.push(part)
    } else if (line.startsWith("No edge leaves '")) {
      part = body
    }
    part.push(line)
  }
  return { body: body.join('\n'), warnings: warnings.map((lines) => lines.join('\n')) }
}
