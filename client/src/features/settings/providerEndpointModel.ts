import { useMemo } from 'react'
import type { ApiKeyDto } from '../../api/apiKeys'
import type { AgentTuiRunnerTypeDto } from '../../api/agentTui'
import { useAgentTuiRunnerTypes } from '../../api/agentTui'
import type { ProviderEndpointDto, ProviderEndpointMap } from '../../api/projects'

// CARD-0167 S5: the provider endpoint form model shared by the project and agent settings modals.
// URL and key NAME only; a key value never enters this code.

export interface ProviderEndpointDraft {
  baseUrl: string
  apiKeyName: string | null
}

/** Keyed by AgentKind name, like the server's map. */
export type ProviderEndpointDrafts = Record<string, ProviderEndpointDraft>

export const EMPTY_PROVIDER_ENDPOINT_DRAFT: ProviderEndpointDraft = { baseUrl: '', apiKeyName: null }

export function draftFromEndpoint(endpoint?: ProviderEndpointDto | null): ProviderEndpointDraft {
  return endpoint
    ? { baseUrl: endpoint.baseUrl, apiKeyName: endpoint.apiKeyName ?? null }
    : EMPTY_PROVIDER_ENDPOINT_DRAFT
}

export function draftsFromMap(map?: ProviderEndpointMap | null): ProviderEndpointDrafts {
  return Object.fromEntries(Object.entries(map ?? {}).map(([kind, endpoint]) => [kind, draftFromEndpoint(endpoint)]))
}

/**
 * The request value for one draft: null when both fields are blank (no endpoint for that kind), an
 * error when the draft cannot be saved. The server re-validates the URL. This catches a missing
 * base URL, a missing required key name, and a username or password in the base URL.
 */
export function endpointFromDraft(
  draft: ProviderEndpointDraft | undefined,
  displayName: string,
  keyRequired: boolean,
): { endpoint: ProviderEndpointDto | null; error: string | null } {
  const baseUrl = draft?.baseUrl.trim() ?? ''
  const apiKeyName = draft?.apiKeyName?.trim() || null
  if (!baseUrl && !apiKeyName) return { endpoint: null, error: null }
  if (!baseUrl) return { endpoint: null, error: `${displayName} endpoint needs a base URL.` }
  // CARD-1509: project PUT and agent PATCH both come through here. Refuse before either request.
  // The message names the field only; it does not repeat the submitted URL.
  if (baseUrlCarriesUserInfo(baseUrl)) return { endpoint: null, error: userInfoError(displayName) }
  if (keyRequired && !apiKeyName) return { endpoint: null, error: `${displayName} endpoint needs a key name.` }
  return { endpoint: { baseUrl, apiKeyName }, error: null }
}

function userInfoError(displayName: string): string {
  return `${displayName} base URL cannot include a username or password. Reference a stored key by name instead.`
}

// `\` ends an authority for these schemes only. Other schemes keep `\` inside the authority.
const SPECIAL_URL_SCHEMES = new Set(['http', 'https', 'ws', 'wss', 'ftp', 'file'])

/**
 * True when posting the base URL would send a username or password.
 * Any scheme is refused, not only http(s). A `mailto:` value with no password
 * and no `//` stays a server scheme refusal. Text with no recognised scheme,
 * and a recognised scheme that is not followed by `//`, is still checked for
 * userinfo between the first `//` and the next delimiter.
 * A special scheme anywhere in the text (http, https, ws, wss, ftp, file) is checked
 * again. Slashes, backslashes, whitespace, controls, and zero-width marks after its
 * colon are only a separator, including one slash and a run of none.
 * An `@` only in the path, query, or fragment, and `http://@host` (empty userinfo),
 * are not credentials.
 */
function baseUrlCarriesUserInfo(baseUrl: string): boolean {
  const scheme = schemeOf(baseUrl)
  if (scheme === 'mailto') return mailtoCarriesPasswordOrSlashes(baseUrl)
  if (parsedUrlCarriesCredential(baseUrl)) return true
  if (rawAuthorityCarriesCredential(baseUrl)) return true
  if (embeddedSpecialAuthorityCarriesCredential(baseUrl)) return true
  return (scheme === null || !baseUrl.startsWith('//', scheme.length + 1))
    && doubleSlashAuthorityCarriesCredential(baseUrl)
}

/** CARD-1822: `mailto:user@host` is not userinfo. A password, or `//`, is. */
function mailtoCarriesPasswordOrSlashes(value: string): boolean {
  if (value.includes('//')) return true
  const scheme = schemeOf(value)
  const opaque = value.slice((scheme?.length ?? 0) + 1)
  const cut = opaque.search(/[?#]/)
  const address = cut < 0 ? opaque : opaque.slice(0, cut)
  const at = address.lastIndexOf('@')
  if (at <= 0) return false
  const userinfo = address.slice(0, at)
  const colon = userinfo.indexOf(':')
  if (colon < 0) return false
  return userinfo.slice(0, colon).length > 0 || userinfo.slice(colon + 1).length > 0
}

/** CARD-1822: a prefix can hide the scheme, including one that looks like a scheme. The authority after the first `//` still counts. */
function doubleSlashAuthorityCarriesCredential(value: string): boolean {
  const slash = value.indexOf('//')
  if (slash < 0) return false
  return userinfoIsCredential(segmentBeforeDelimiter(value, slash + 2, false))
}

/** CARD-1822 D-2: one slash, a backslash, or no slash after a prefixed special scheme still carries userinfo. */
function embeddedSpecialAuthorityCarriesCredential(value: string): boolean {
  for (const match of value.matchAll(/(?:https?|wss?|ftp|file):/gi)) {
    let start = match.index + match[0].length
    while (start < value.length && isEmbeddedAuthoritySeparator(value[start])) start++
    if (userinfoIsCredential(segmentBeforeDelimiter(value, start, true))) return true
  }
  return false
}

function isEmbeddedAuthoritySeparator(c: string): boolean {
  if (c === '/' || c === '\\') return true
  const code = c.charCodeAt(0)
  if (code <= 0x20 || code === 0x7f) return true
  if (code === 0x00a0 || code === 0x1680 || code === 0x180e) return true
  if (code >= 0x2000 && code <= 0x200d) return true
  if (code === 0x2028 || code === 0x2029 || code === 0x202f || code === 0x205f || code === 0x2060) return true
  return code === 0x3000 || code === 0xfeff
}

function schemeOf(value: string): string | null {
  const match = /^([A-Za-z][A-Za-z0-9+.-]*):/.exec(value)
  return match ? match[1].toLowerCase() : null
}

function parsedUrlCarriesCredential(baseUrl: string): boolean {
  try {
    const url = new URL(baseUrl)
    return url.protocol !== 'mailto:' && (url.username.length > 0 || url.password.length > 0)
  } catch {
    return false
  }
}

function rawAuthorityCarriesCredential(value: string): boolean {
  const scheme = schemeOf(value)
  if (scheme === 'mailto') return false
  const schemePrefixLength = scheme === null ? 0 : scheme.length + 1
  if (value.startsWith('//')) return userinfoIsCredential(segmentBeforeDelimiter(value, 2, false))
  if (scheme !== null && value.startsWith('//', schemePrefixLength)) {
    return userinfoIsCredential(
      segmentBeforeDelimiter(value, schemePrefixLength + 2, SPECIAL_URL_SCHEMES.has(scheme)),
    )
  }
  return userinfoIsCredential(segmentBeforeDelimiter(value, 0, false))
}

function segmentBeforeDelimiter(value: string, start: number, backslashEndsAuthority: boolean): string {
  for (let i = start; i < value.length; i++) {
    const c = value[i]
    if (c === '/' || c === '?' || c === '#') return value.slice(start, i)
    if (backslashEndsAuthority && c === '\\') return value.slice(start, i)
  }
  return value.slice(start)
}

function userinfoIsCredential(authority: string): boolean {
  const at = authority.lastIndexOf('@')
  if (at <= 0) return false
  const userinfo = authority.slice(0, at)
  const colon = userinfo.indexOf(':')
  if (colon < 0) return true
  const username = userinfo.slice(0, colon)
  const password = userinfo.slice(colon + 1)
  return username.length > 0 || password.length > 0
}

/** The project's `providerEndpoints` request map, and per-kind errors that block the save. */
export function buildProviderEndpointMap(
  drafts: ProviderEndpointDrafts,
  runnerTypes: AgentTuiRunnerTypeDto[],
): { map: Record<string, ProviderEndpointDto>; errors: Record<string, string> } {
  const map: Record<string, ProviderEndpointDto> = {}
  const errors: Record<string, string> = {}
  for (const [kind, draft] of Object.entries(drafts)) {
    const runnerType = runnerTypes.find((type) => type.kind === kind)
    const { endpoint, error } = endpointFromDraft(
      draft,
      runnerType?.displayName ?? kind,
      runnerType?.providerEndpoint?.keyRequired ?? false,
    )
    if (error) errors[kind] = error
    else if (endpoint) map[kind] = endpoint
  }
  return { map, errors }
}

/** Global key names plus the given project's own, sorted and unique. */
export function providerEndpointKeyNames(keys: ApiKeyDto[] | undefined, projectId: string | null | undefined): string[] {
  const names = (keys ?? [])
    .filter((key) => key.projectId === null || (!!projectId && key.projectId === projectId))
    .map((key) => key.name)
  return [...new Set(names)].sort((a, b) => a.localeCompare(b))
}

/** The runner kinds a provider endpoint applies to, per GET /api/agent-tui/runner-types. */
export function useProviderEndpointKinds(): AgentTuiRunnerTypeDto[] {
  const runnerTypes = useAgentTuiRunnerTypes()
  return useMemo(
    () => (runnerTypes.data ?? []).filter((type) => type.providerEndpoint?.supported),
    [runnerTypes.data],
  )
}
