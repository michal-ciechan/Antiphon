/**
 * The goal a selection send queues: the passage as a quote so the delegate sees exactly what the
 * reader was pointing at, then the instruction.
 */
export function buildSelectionGoal(
  path: string,
  selection: string,
  instruction: string,
  context?: string,
): string {
  const quoted = selection
    .trim()
    .split('\n')
    .map((line) => `> ${line}`)
    .join('\n')
  return `${context ? `${context.trim()}\n\n` : ''}In ${path}:\n\n${quoted}\n\n${instruction.trim()}`
}
