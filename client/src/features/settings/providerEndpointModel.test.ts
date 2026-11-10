import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
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
    // CARD-1822 D-2: a prefix in front of one slash, or a slash mixed with a backslash,
    // hides the authority from both new URL and the literal // scan.
    ['a prefix and one slash, because the credential is still in the request', 'endpoint: https:/u:pw@h/v1'],
    ['a one-letter scheme and one slash', 'x:https:/u:pw@h/v1'],
    ['a BASE_URL= prefix and one slash', 'BASE_URL=https:/u:pw@h/v1'],
    ['a prefix and a slash then a backslash', 'endpoint: https:/\\u:pw@h/v1'],
    ['a prefix and a backslash then a slash', 'endpoint: https:\\/u:pw@h/v1'],
    ['a BASE_URL= prefix and a slash then a backslash', 'BASE_URL=https:/\\u:pw@h/v1'],
    ['a prefix and a slash then a backslash with no path', 'endpoint: https:/\\u:pw@h'],
  ])('refuses %s and does not return the URL', (_name, baseUrl) => {
    const result = endpointFromDraft({ baseUrl, apiKeyName: 'claude-proxy-key' }, 'Grok', false)
    expect(result.endpoint).toBeNull()
    expect(result.error).toBe(USERINFO_ERROR)
    expect(result.error).not.toContain(SENTINEL)
    expect(result.error).not.toContain('@')
    expect(result.error).not.toContain('proxy.test')
    expect(JSON.stringify(result)).not.toContain(SENTINEL)
    expect(JSON.stringify(result)).not.toContain('u:pw')
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

describe('prefixed special-scheme userinfo', () => {
  // CARD-1822 D-2. These strings return the endpoint at 1262bc7c1. A special scheme
  // (http, https, ws, wss, ftp, file), 0-4 slashes, backslash mixes, every prefix kind,
  // percent-encoded slashes, uppercase, whitespace, controls, zero-width marks, IPv6,
  // and a second URL are refused. The acceptance rows stay endpoints.
  it.each([
    ['see/http:u:SENTINEL@h', `see/http:u:${SENTINEL}@h`],
    ['see/https:u:SENTINEL@h', `see/https:u:${SENTINEL}@h`],
    ['see/ws:u:SENTINEL@h', `see/ws:u:${SENTINEL}@h`],
    ['see/wss:u:SENTINEL@h', `see/wss:u:${SENTINEL}@h`],
    ['see/ftp:u:SENTINEL@h', `see/ftp:u:${SENTINEL}@h`],
    ['see/file:u:SENTINEL@h', `see/file:u:${SENTINEL}@h`],
    ['pre?https:u:SENTINEL@h', `pre?https:u:${SENTINEL}@h`],
    ['pre#https:u:SENTINEL@h', `pre#https:u:${SENTINEL}@h`],
    ['endpoint: http:/u:SENTINEL@h/v1', `endpoint: http:/u:${SENTINEL}@h/v1`],
    ['endpoint: https:/u:SENTINEL@h/v1', `endpoint: https:/u:${SENTINEL}@h/v1`],
    ['endpoint: ws:/u:SENTINEL@h/v1', `endpoint: ws:/u:${SENTINEL}@h/v1`],
    ['endpoint: wss:/u:SENTINEL@h/v1', `endpoint: wss:/u:${SENTINEL}@h/v1`],
    ['endpoint: ftp:/u:SENTINEL@h/v1', `endpoint: ftp:/u:${SENTINEL}@h/v1`],
    ['endpoint: file:/u:SENTINEL@h/v1', `endpoint: file:/u:${SENTINEL}@h/v1`],
    ['http://prefix https:/u:SENTINEL@h', `http://prefix https:/u:${SENTINEL}@h`],
    ['http://prefix wss:/u:SENTINEL@[::1]', `http://prefix wss:/u:${SENTINEL}@[::1]`],
    ['endpoint: https:///u:SENTINEL@h/v1', `endpoint: https:///u:${SENTINEL}@h/v1`],
    ['endpoint: http:////u:SENTINEL@h/v1', `endpoint: http:////u:${SENTINEL}@h/v1`],
    ['endpoint: ws:///u:SENTINEL@h', `endpoint: ws:///u:${SENTINEL}@h`],
    ['endpoint: wss:////u:SENTINEL@h', `endpoint: wss:////u:${SENTINEL}@h`],
    ['endpoint: ftp:///u:SENTINEL@h/v1', `endpoint: ftp:///u:${SENTINEL}@h/v1`],
    ['endpoint: file:////u:SENTINEL@h', `endpoint: file:////u:${SENTINEL}@h`],
    ['endpoint: http:\\/u:SENTINEL@h/v1', `endpoint: http:\\/u:${SENTINEL}@h/v1`],
    ['endpoint: ws:/\\u:SENTINEL@h/v1', `endpoint: ws:/\\u:${SENTINEL}@h/v1`],
    ['pre/https:\\u:SENTINEL@h', `pre/https:\\u:${SENTINEL}@h`],
    ['pre/wss:\\\\u:SENTINEL@h', `pre/wss:\\\\u:${SENTINEL}@h`],
    ['pre/ftp:\\u:SENTINEL@[::1]', `pre/ftp:\\u:${SENTINEL}@[::1]`],
    ['see/file:\\\\u:SENTINEL@h', `see/file:\\\\u:${SENTINEL}@h`],
    ['endpoint: ftp:/\\/u:SENTINEL@h/v1', `endpoint: ftp:/\\/u:${SENTINEL}@h/v1`],
    ['endpoint: file:\\/\\u:SENTINEL@h', `endpoint: file:\\/\\u:${SENTINEL}@h`],
    ['"https:/u:SENTINEL@h/v1"', `"https:/u:${SENTINEL}@h/v1"`],
    ['<https:/u:SENTINEL@h/v1>', `<https:/u:${SENTINEL}@h/v1>`],
    ['\u200bhttps:/u:SENTINEL@h/v1', `\u200bhttps:/u:${SENTINEL}@h/v1`],
    ['BASE_URL=https:/u:SENTINEL@h/v1', `BASE_URL=https:/u:${SENTINEL}@h/v1`],
    ['note https:/u:SENTINEL@h/v1', `note https:/u:${SENTINEL}@h/v1`],
    ['endpoint:https:/u:SENTINEL@h/v1', `endpoint:https:/u:${SENTINEL}@h/v1`],
    ['data:text/plain,https:/u:SENTINEL@h', `data:text/plain,https:/u:${SENTINEL}@h`],
    ['a:b:https:/u:SENTINEL@h/v1', `a:b:https:/u:${SENTINEL}@h/v1`],
    ['baseUrl:\u0009https:/u:SENTINEL@h/v1', `baseUrl:\u0009https:/u:${SENTINEL}@h/v1`],
    ['endpoint:\u000dhttps:/u:SENTINEL@h/v1', `endpoint:\u000dhttps:/u:${SENTINEL}@h/v1`],
    ['\ufeffhttps:/u:SENTINEL@h/v1', `\ufeffhttps:/u:${SENTINEL}@h/v1`],
    ['url:\u00a0https:/u:SENTINEL@h/v1', `url:\u00a0https:/u:${SENTINEL}@h/v1`],
    ['x:"https:/u:SENTINEL@h/v1"', `x:"https:/u:${SENTINEL}@h/v1"`],
    ['pre/https:%2F%2Fu:SENTINEL@h', `pre/https:%2F%2Fu:${SENTINEL}@h`],
    ['pre/HTTP:%2fu:SENTINEL@h/v1', `pre/HTTP:%2fu:${SENTINEL}@h/v1`],
    ['ENDPOINT: HTTPS:/U:SENTINEL@H/V1', `ENDPOINT: HTTPS:/U:${SENTINEL}@H/V1`],
    ['Note WS:/U:SENTINEL@H', `Note WS:/U:${SENTINEL}@H`],
    ['PATH/FILE:U:SENTINEL@H', `PATH/FILE:U:${SENTINEL}@H`],
    ['endpoint: https: /u:SENTINEL@h/v1', `endpoint: https: /u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u0009/u:SENTINEL@h/v1', `endpoint: https:\u0009/u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u000a/u:SENTINEL@h', `endpoint: https:\u000a/u:${SENTINEL}@h`],
    ['endpoint: https:\u0000/u:SENTINEL@h/v1', `endpoint: https:\u0000/u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u200b/u:SENTINEL@h/v1', `endpoint: https:\u200b/u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u200c/u:SENTINEL@h', `endpoint: https:\u200c/u:${SENTINEL}@h`],
    ['endpoint: https:\u2060/u:SENTINEL@h', `endpoint: https:\u2060/u:${SENTINEL}@h`],
    ['endpoint: https:/u:\u0001SENTINEL@h/v1', `endpoint: https:/u:\u0001${SENTINEL}@h/v1`],
    ['endpoint: https:/u:\u200bSENTINEL@h', `endpoint: https:/u:\u200b${SENTINEL}@h`],
    ['endpoint: https: \u00a0 /u:SENTINEL@h/v1', `endpoint: https: \u00a0 /u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u2028/u:SENTINEL@h/v1', `endpoint: https:\u2028/u:${SENTINEL}@h/v1`],
    ['endpoint: https:\u3000/u:SENTINEL@h/v1', `endpoint: https:\u3000/u:${SENTINEL}@h/v1`],
    ['endpoint: https:/u:SENTINEL@[::1]/v1', `endpoint: https:/u:${SENTINEL}@[::1]/v1`],
    ['endpoint: wss:/u:SENTINEL@[::1]:8443', `endpoint: wss:/u:${SENTINEL}@[::1]:8443`],
    ['see/http:u:SENTINEL@[2001:db8::1]', `see/http:u:${SENTINEL}@[2001:db8::1]`],
    ['https://ok.example/v1 endpoint: https:/u:SENTINEL@h/v1', `https://ok.example/v1 endpoint: https:/u:${SENTINEL}@h/v1`],
    ['https://api.example.com/v1?next=https:/u:SENTINEL@other/v1', `https://api.example.com/v1?next=https:/u:${SENTINEL}@other/v1`],
    ['http://[::1]:8080/v1 plus ws:/u:SENTINEL@h', `http://[::1]:8080/v1 plus ws:/u:${SENTINEL}@h`],
    ['https://safe.example/v1,https:///u:SENTINEL@evil/v1', `https://safe.example/v1,https:///u:${SENTINEL}@evil/v1`],
    ['file:///tmp/ok see/ftp:/u:SENTINEL@h', `file:///tmp/ok see/ftp:/u:${SENTINEL}@h`],
    ['endpoint: https:/SENTINEL@h/v1', `endpoint: https:/${SENTINEL}@h/v1`],
    ['endpoint: https:/ :SENTINEL@h/v1', `endpoint: https:/ :${SENTINEL}@h/v1`],
    ['see/http::SENTINEL@h', `see/http::${SENTINEL}@h`],
    ['endpoint: http:/u:SENTINEL@h', `endpoint: http:/u:${SENTINEL}@h`],
  ])('refuses %s and does not return the URL', (_name, baseUrl) => {
    const result = endpointFromDraft({ baseUrl, apiKeyName: 'claude-proxy-key' }, 'Grok', false)
    expect(result.endpoint).toBeNull()
    expect(result.error).toBe(USERINFO_ERROR)
    expect(result.error).not.toContain(SENTINEL)
    expect(result.error).not.toContain('@')
    expect(JSON.stringify(result)).not.toContain(SENTINEL)
  })

  it.each([
    ['proxy.test:8080', `proxy.test:8080`],
    ['localhost:11434', `localhost:11434`],
    ['localhost:11434/v1', `localhost:11434/v1`],
    ['ollama:11434', `ollama:11434`],
    ['\'ollama:11434\'', `'ollama:11434'`],
    ['http://host:8080/v1', `http://host:8080/v1`],
    ['http://[::1]:8443/v1', `http://[::1]:8443/v1`],
    ['http://@host/v1', `http://@host/v1`],
    ['https://api.x.ai/v1', `https://api.x.ai/v1`],
    ['proxy.test:8080/path@segment', `proxy.test:8080/path@segment`],
    ['https://api.example.com/v1?token=a@b', `https://api.example.com/v1?token=a@b`],
    ['https://api.example.com/v1#a@b', `https://api.example.com/v1#a@b`],
    ['https://api.example.com/v1/a@b', `https://api.example.com/v1/a@b`],
    ['mailto:someone@example.com', `mailto:someone@example.com`],
    ['proxy.test/v1', `proxy.test/v1`],
    ['host:8080/v1//x', `host:8080/v1//x`],
    ['http:host/p@q', `http:host/p@q`],
    ['http://good.example\\@evil', `http://good.example\\@evil`],
    ['https://user%40proxy.test/v1', `https://user%40proxy.test/v1`],
    ['https://proxy.test./v1', `https://proxy.test./v1`],
    ['http://:8080/', `http://:8080/`],
    ['http://127.0.0.1:8080', `http://127.0.0.1:8080`],
    ['https://[::1]:8443/v1', `https://[::1]:8443/v1`],
    ['ws://host/v1', `ws://host/v1`],
    ['wss://realtime.example/socket', `wss://realtime.example/socket`],
    ['ftp://proxy.test/v1', `ftp://proxy.test/v1`],
    ['file:///tmp/model', `file:///tmp/model`],
    ['http://localhost:11434', `http://localhost:11434`],
    ['https://api.example.com/v1?x=1&y=a@b', `https://api.example.com/v1?x=1&y=a@b`],
    ['https://api.example.com/v1#frag@ment', `https://api.example.com/v1#frag@ment`],
    ['ollama:11434/v1', `ollama:11434/v1`],
    ['HOST:8080', `HOST:8080`],
    ['http://proxy.test:8080/v1/items', `http://proxy.test:8080/v1/items`],
    ['https://api.example.com/v1/path/sub', `https://api.example.com/v1/path/sub`],
    ['http://[::1]/v1', `http://[::1]/v1`],
    ['https://api.example.com/v1?token=a@b#frag', `https://api.example.com/v1?token=a@b#frag`],
    ['localhost', `localhost`],
    ['127.0.0.1:11434', `127.0.0.1:11434`],
    ['proxy.test:11434', `proxy.test:11434`],
    ['https://ollama.local:11434/v1', `https://ollama.local:11434/v1`],
    ['http://proxy.test/S3@h/v1', `http://proxy.test/S3@h/v1`],
    ['http://host/v1//not-userinfo', `http://host/v1//not-userinfo`],
    ['https://api.example.com/v1/file:readme', `https://api.example.com/v1/file:readme`],
    ['https://api.example.com/news:today', `https://api.example.com/news:today`],
    ['ftp://proxy.test:21/incoming', `ftp://proxy.test:21/incoming`],
    ['https://api.example.com/v1?next=https://other.example/ok', `https://api.example.com/v1?next=https://other.example/ok`],
    ['file:///var/models/weights', `file:///var/models/weights`],
    ['http://[2001:db8::1]:8080/v1', `http://[2001:db8::1]:8080/v1`],
    ['http://u:1234/S3@h/', `http://u:1234/S3@h/`],
    ['https://proxy.test?x=a@b', `https://proxy.test?x=a@b`],
    ['https://proxy.test#a@b', `https://proxy.test#a@b`],
    ['notes without an at sign', `notes without an at sign`],
    ['https://cdn.example/news:/readme', `https://cdn.example/news:/readme`],
    ['wss://[::1]:8443/v1', `wss://[::1]:8443/v1`],
    // CARD-1827. The @ is path text. WHATWG leaves username and password empty.
    ['percent-encoded nested https with userinfo in the path', `https://proxy/https%3A//u:${SENTINEL}@real/v1`],
    ['percent-encoded nested scheme and slashes', `https://proxy/https%3A%2F%2Fu:${SENTINEL}@h`],
    ['percent-encoded http colon and one slash', `https://proxy/http%3a/u:${SENTINEL}@h`],
    ['socks5 nested in the path', `https://proxy/socks5://u:${SENTINEL}@real:1080`],
    ['redis nested in the path', `https://proxy/redis://u:${SENTINEL}@h`],
  ])('still accepts %s', (_name, baseUrl) => {
    expect(endpointFromDraft({ baseUrl, apiKeyName: 'claude-proxy-key' }, 'Grok', false)).toEqual({
      endpoint: { baseUrl, apiKeyName: 'claude-proxy-key' },
      error: null,
    })
  })

  it('documents that path-embedded userinfo is accepted path text', () => {
    const here = path.dirname(fileURLToPath(import.meta.url))
    const source = readFileSync(path.join(here, 'providerEndpointModel.ts'), 'utf8')
    expect(source).toContain('CARD-1827: path-embedded userinfo is accepted path text')
  })
})
