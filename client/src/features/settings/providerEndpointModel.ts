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

/**
 * True when posting the base URL would send a username or password.
 * `mailto:` stays a server scheme refusal. An `@` that is only in the path, and `http://@host`
 * (empty userinfo), are not credentials.
 */
function baseUrlCarriesUserInfo(baseUrl: string): boolean {
  try {
    const url = new URL(baseUrl)
    if (url.protocol === 'http:' || url.protocol === 'https:')
      return url.username.length > 0 || url.password.length > 0
    if (url.protocol === 'mailto:') return false
    return !baseUrl.includes('://') && baseUrl.includes('@')
  } catch {
    if (/^https?:\/\//i.test(baseUrl)) {
      const authority = /^https?:\/\/([^/?#]*)/i.exec(baseUrl)
      return authority !== null && authority[1].includes('@')
    }
    return /^[^:/@\s]+@[^:/@\s]+$/.test(baseUrl)
  }
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
