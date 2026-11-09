import { describe, expect, it } from 'vitest'
import type { AgentTuiRunnerTypeDto } from '../../api/agentTui'
import { buildProviderEndpointMap, endpointFromDraft } from './providerEndpointModel'

// CARD-1509: a draft base URL with a username or password must not become a request body.
// Expected strings are literals. The sentinel is a synthetic credential, never a real secret.
const SENTINEL = 'SENTINEL1509'
const USERINFO_ERROR =
  'Grok base URL cannot include a username or password. Reference a stored key by name instead.'

function runner(kind: AgentTuiRunnerTypeDto['kind'], displayName: string, keyRequired: boolean): AgentTuiRunnerTypeDto {
  return {
    kind,
    displayName,
    description: '',
    defaultModelArgumentName: null,
    authenticationModes: [],
    curatedModels: [],
    capabilities: [],
    guidance: '',
    providerEndpoint: { supported: true, keyRequired, environmentNames: [], argumentTemplate: [] },
  }
}

describe('endpointFromDraft userinfo', () => {
  it.each([
    ['username and password', `https://operator:${SENTINEL}@proxy.test/v1`],
    ['username only', `https://${SENTINEL}@proxy.test/v1`],
    ['password only', `http://:${SENTINEL}@proxy.test/v1`],
    ['surrounding whitespace', `  http://operator:${SENTINEL}@proxy.test  `],
    ['IPv6 host', `http://operator:${SENTINEL}@[::1]:8080/`],
    ['unparsed http authority', `http://operator:${SENTINEL}@`],
    ['scheme-less user:pass@host', `operator:${SENTINEL}@proxy.test`],
    ['scheme-less username@host', `${SENTINEL}@proxy.test`],
  ])('refuses %s and does not return the URL', (_name, baseUrl) => {
    const result = endpointFromDraft({ baseUrl, apiKeyName: 'claude-proxy-key' }, 'Grok', false)
    expect(result.endpoint).toBeNull()
    expect(result.error).toBe(USERINFO_ERROR)
    expect(result.error).not.toContain(SENTINEL)
    expect(result.error).not.toContain('@')
    expect(result.error).not.toContain('proxy.test')
    expect(JSON.stringify(result)).not.toContain(SENTINEL)
  })

  it('reports userinfo before a missing required key name', () => {
    const result = endpointFromDraft(
      { baseUrl: `https://operator:${SENTINEL}@proxy.test`, apiKeyName: null },
      'Codex',
      true,
    )
    expect(result.endpoint).toBeNull()
    expect(result.error).toBe(
      'Codex base URL cannot include a username or password. Reference a stored key by name instead.',
    )
    expect(result.error).not.toContain(SENTINEL)
  })

  it.each([
    ['http host and port', 'http://proxy.test:8080'],
    ['https path', 'https://proxy.example/v1'],
    ['loopback', 'http://127.0.0.1:8080'],
    ['at sign only in the path', 'http://u:1234/S3@h/'],
    ['empty userinfo', 'http://@host'],
  ])('still accepts %s', (_name, baseUrl) => {
    expect(endpointFromDraft({ baseUrl, apiKeyName: 'claude-proxy-key' }, 'Grok', false)).toEqual({
      endpoint: { baseUrl, apiKeyName: 'claude-proxy-key' },
      error: null,
    })
  })

  it('still asks for a key name when the URL has no userinfo', () => {
    expect(endpointFromDraft({ baseUrl: 'https://proxy.example/v1', apiKeyName: '  ' }, 'Codex', true)).toEqual({
      endpoint: null,
      error: 'Codex endpoint needs a key name.',
    })
  })

  it('still returns null when both fields are blank', () => {
    expect(endpointFromDraft({ baseUrl: '  ', apiKeyName: null }, 'Grok', false)).toEqual({
      endpoint: null,
      error: null,
    })
  })

  it('leaves mailto for the server scheme refusal', () => {
    const baseUrl = 'mailto:someone@example.com'
    expect(endpointFromDraft({ baseUrl, apiKeyName: null }, 'Grok', false)).toEqual({
      endpoint: { baseUrl, apiKeyName: null },
      error: null,
    })
  })

  it('omits a userinfo draft from the project map and keeps a normal https sibling', () => {
    const { map, errors } = buildProviderEndpointMap(
      {
        ClaudeCode: { baseUrl: 'https://claude.example/v1', apiKeyName: null },
        Grok: { baseUrl: `http://operator:${SENTINEL}@proxy.test`, apiKeyName: null },
      },
      [runner('ClaudeCode', 'Claude Code', false), runner('Grok', 'Grok', false)],
    )
    expect(map).toEqual({
      ClaudeCode: { baseUrl: 'https://claude.example/v1', apiKeyName: null },
    })
    expect(errors.Grok).toBe(USERINFO_ERROR)
    expect(JSON.stringify(map)).not.toContain(SENTINEL)
    expect(errors.Grok).not.toContain(SENTINEL)
  })
})
