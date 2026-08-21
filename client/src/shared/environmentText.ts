export interface ParsedEnvironmentText {
  env: Record<string, string>
  warnings: string[]
}

/** Converts the environment dictionaries used by launch DTOs into editable KEY=value lines. */
export function envToText(env: Record<string, string>): string {
  return Object.entries(env)
    .map(([key, value]) => `${key}=${value}`)
    .join('\n')
}

/**
 * Parses editable KEY=value lines. Blank lines are ignored; malformed lines are omitted and
 * reported, and duplicate keys keep their final value so the submitted environment is explicit.
 */
export function parseEnvironmentText(text: string): ParsedEnvironmentText {
  const env: Record<string, string> = {}
  const warnings: string[] = []

  for (const [index, line] of text.split(/\r?\n/).entries()) {
    if (!line.trim()) continue

    const idx = line.indexOf('=')
    const lineNumber = index + 1
    if (idx < 0) {
      warnings.push(`Line ${lineNumber} was ignored because it is not KEY=value.`)
      continue
    }

    const key = line.slice(0, idx).trim()
    if (!key) {
      warnings.push(`Line ${lineNumber} was ignored because its key is empty.`)
      continue
    }

    if (Object.prototype.hasOwnProperty.call(env, key))
      warnings.push(`Line ${lineNumber} repeats ${key}; its value replaces the earlier one.`)

    // Values are verbatim. In particular, leading/trailing whitespace after '=' is meaningful.
    env[key] = line.slice(idx + 1)
  }

  return { env, warnings }
}

/** Parses editable KEY=value lines when the caller does not need validation warnings. */
export function textToEnv(text: string): Record<string, string> {
  return parseEnvironmentText(text).env
}
