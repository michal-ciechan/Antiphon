/**
 * `CARD-0041` renders as `#41` everywhere in the UI.
 *
 * This is a DISPLAY form of the stored identifier, not a new identifier scheme (feature 011 §4):
 * storage, the API, agent prompts and prose all keep `CARD-0041`, which is what lands in commit
 * messages and docs and must stay greppable. Anything that does not look like `CARD-<digits>`
 * renders verbatim, so a board on a foreign tracker keeps its own identifiers untouched.
 */
const CARD_IDENTIFIER = /^\s*card-0*(\d+)\s*$/i

/** The numeric part of a `CARD-nnnn` identifier, or null when it is some other scheme. */
export function cardNumber(identifier: string): number | null {
  const match = CARD_IDENTIFIER.exec(identifier)
  return match ? Number(match[1]) : null
}

/** `CARD-0041` → `#41`. Non-matching identifiers pass through unchanged. */
export function displayIdentifier(identifier: string): string {
  const number = cardNumber(identifier)
  return number === null ? identifier : `#${number}`
}

/** Dimmed tag for a linked tracker issue: GitHub `#3` → `GH #3`. */
export function externalIssueTag(issue: { trackerKind: string; key: string }): string {
  if (issue.trackerKind === 'GitHubIssues') {
    return issue.key.startsWith('#') ? `GH ${issue.key}` : `GH #${issue.key}`
  }
  return issue.key
}

/**
 * The digits an identifier-shaped query is asking for, across every form a human types:
 * `#41`, `41`, `CARD-0041`, `card-41`. Returns null when the query is not an identifier
 * reference at all, in which case the caller should fall back to a plain substring match.
 */
export function identifierQueryDigits(query: string): string | null {
  const stripped = query.trim().replace(/^#/, '').replace(/^card[-\s]?/i, '')
  if (!/^\d+$/.test(stripped)) return null
  return stripped.replace(/^0+(?=\d)/, '')
}

/**
 * Does this identifier answer that query? Identifier-shaped queries match on the decimal number
 * by PREFIX (`4` finds `#4`, `#40`, `#41`) so incremental typing narrows rather than jumping
 * straight to a single card. Everything else is a case-insensitive substring of the stored form.
 */
export function matchesIdentifierQuery(identifier: string, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true

  const digits = identifierQueryDigits(query)
  const number = cardNumber(identifier)
  if (digits !== null && number !== null) return String(number).startsWith(digits)

  return identifier.toLowerCase().includes(needle)
}
