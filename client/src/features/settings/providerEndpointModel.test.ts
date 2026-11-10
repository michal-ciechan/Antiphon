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
    ['ftp scheme userinfo', `ftp://operator:${SENTINEL}@proxy.test/v1`],
    ['websocket scheme userinfo', `ws://operator:${SENTINEL}@proxy.test`],
    ['file scheme userinfo', `file://operator:${SENTINEL}@host/`],
    ['git+https scheme userinfo', `git+https://operator:${SENTINEL}@proxy.test`],
    ['scheme-relative userinfo', `//operator:${SENTINEL}@proxy.test/v1`],
    ['scheme-less user at host with a path', `${SENTINEL}@proxy.test/v1`],
    ['scheme-less user at host with a port', `${SENTINEL}@proxy.test:8080`],
    ['scheme typo htps userinfo', `htps://u:${SENTINEL}@h`],
    ['a tab inside a non-http authority, because the username is still a credential', `ftp://opera\ttor:${SENTINEL}@proxy.test/v1`],
    ['a newline inside a non-http authority, because the password is still a credential', `ws://operator:${SENTINEL}@pro\nxy.test`],
    ['an uppercase FTP scheme, because the scheme case does not remove userinfo', `FTP://operator:${SENTINEL}@proxy.test/v1`],
    ['userinfo on an IPv6 host, because brackets do not remove the username or password', `wss://operator:${SENTINEL}@[::1]:8443/v1`],
    // CARD-1822 A-1: a prefix hides the scheme, so the @ after // must still be refused.
    ['a quoted paste, because the quote hides the scheme', `"https://u:${SENTINEL}@proxy.test/v1"`],
    ['an angle-bracket paste, because the bracket hides the scheme', `<https://u:${SENTINEL}@proxy.test/v1>`],
    ['a zero-width space before the scheme', `\u200Bhttps://u:${SENTINEL}@proxy.test/v1`],
    ['a BASE_URL= prefix before the scheme', `BASE_URL=https://u:${SENTINEL}@proxy.test/v1`],
    ['a space before the scheme slashes', `https ://u:${SENTINEL}@proxy.test/v1`],
    // CARD-1822 A-2: mailto stays allowed only without a password and without //.
    ['mailto with a password', `mailto:operator:${SENTINEL}@proxy.test`],
    ['uppercase mailto with an authority', `MAILTO://operator:${SENTINEL}@proxy.test/v1`],
    // CARD-1822 D-1: a prefix that itself looks like a scheme hides the real // authority.
    ['a baseUrl: prefix that looks like a scheme', `baseUrl: https://u:${SENTINEL}@proxy.test/v1`],
    ['a url: prefix that looks like a scheme', `url: https://u:${SENTINEL}@.`],
    ['an endpoint: prefix with no space before the real scheme', `endpoint:https://u:${SENTINEL}@.`],
    ['a quoted URL behind a one-letter scheme', `x:"https://u:${SENTINEL}@."`],
    ['an https: prefix before another https URL', `https:https://u:${SENTINEL}@.`],
    ['a data: prefix before an https URL', `data:text/plain,https://u:${SENTINEL}@h`],
    ['a tab inside a scheme-shaped prefix', `baseUrl:\thttps://u:${SENTINEL}@proxy.test/v1`],
    ['a space inside a scheme-shaped prefix', `endpoint: https://u:${SENTINEL}@proxy.test/v1`],
    ['an uppercase scheme-shaped prefix', `BASEURL: HTTPS://u:${SENTINEL}@PROXY.TEST/V1`],
    ['a percent-encoded colon behind a scheme-shaped prefix', `baseUrl: https%3A//u:${SENTINEL}@proxy.test/v1`],
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
    ['percent-encoded %40 in the host because it is not a username or password', 'https://user%40proxy.test/v1'],
    ['a special-scheme backslash that places @ in the path', 'http://good.example\\@evil'],
    ['a trailing-dot host because the dot is not userinfo', 'https://proxy.test./v1'],
    ['a port-only http URL because a port is not a password', 'http://:8080/'],
    ['an @ only in the query because it is not in the authority', 'https://proxy.test?x=a@b'],
    ['an @ only in the fragment because it is not in the authority', 'https://proxy.test#a@b'],
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
