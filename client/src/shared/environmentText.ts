function splitLines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
}

/** Converts the environment dictionaries used by launch DTOs into editable KEY=value lines. */
export function envToText(env: Record<string, string>): string {
  return Object.entries(env)
    .map(([key, value]) => `${key}=${value}`)
    .join('\n')
}

/** Parses editable KEY=value lines, ignoring blank lines and malformed entries. */
export function textToEnv(text: string): Record<string, string> {
  const result: Record<string, string> = {}
  for (const line of splitLines(text)) {
    const idx = line.indexOf('=')
    if (idx <= 0) continue
    result[line.slice(0, idx).trim()] = line.slice(idx + 1)
  }
  return result
}
