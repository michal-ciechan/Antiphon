import { describe, expect, it } from 'vitest'

import viteConfig from '../vite.config'

/**
 * CARD-0604 CP-6a. `antiphon.<machine>.codeperf.net` fronts the client on 17203, not the server
 * on 17202, so every `/api` request a remote phone-home runner makes is proxied by this config
 * first. `GET /api/session-runners/{runnerId}/connect` is a WebSocket upgrade under `/api`; with
 * `ws` unset the proxy strips the hop-by-hop upgrade headers, Kestrel refuses the plain GET with
 * `phone_home_websocket_required`, and the runner registers in a loop but never goes available.
 * Guarding both contexts because `server` (dev) and `preview` (built bundle) are the same object
 * and 17203 serves either one depending on logs/client.mode.
 */
describe('vite 17203 proxy', () => {
  const contexts = [
    ['server', viteConfig.server],
    ['preview', viteConfig.preview],
  ] as const

  for (const [name, config] of contexts) {
    it(`${name} proxies /api with websocket upgrades enabled`, () => {
      const api = config?.proxy?.['/api']
      expect(api, `${name}.proxy['/api'] must exist`).toBeDefined()
      expect(typeof api).toBe('object')
      expect((api as { ws?: boolean }).ws).toBe(true)
    })

    it(`${name} proxies /hubs with websocket upgrades enabled`, () => {
      const hubs = config?.proxy?.['/hubs']
      expect(hubs, `${name}.proxy['/hubs'] must exist`).toBeDefined()
      expect((hubs as { ws?: boolean }).ws).toBe(true)
    })

    it(`${name} points both proxy contexts at the same server target`, () => {
      const api = config?.proxy?.['/api'] as { target?: string } | undefined
      const hubs = config?.proxy?.['/hubs'] as { target?: string } | undefined
      expect(api?.target).toBeTruthy()
      expect(api?.target).toBe(hubs?.target)
    })
  }
})
