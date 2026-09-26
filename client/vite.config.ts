/// <reference types="vitest/config" />
import { execSync } from 'node:child_process'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { createWsProxyTrace } from './scripts/proxy-trace.mjs'

function gitSha(): string {
  try {
    return execSync('git rev-parse HEAD', { encoding: 'utf8' }).trim() || 'unknown'
  } catch {
    return 'unknown'
  }
}

// When running under Aspire, the server URL is injected via environment variable.
// Falls back to the fixed standalone dev backend on localhost:17202.
const serverUrl =
  process.env['services__antiphon-server__http__0'] ??
  process.env['services__server__http__0'] ??
  'http://localhost:17202'

// Shared between the dev server (`server`) and the built-bundle server (`preview`) so port 17203
// behaves the same way under either mode client/scripts/serve.mjs picks (CARD-0216): same allowed
// hosts, same /api and /hubs proxy. strictPort is deliberate — a busy 17203 must fail loudly
// rather than Vite silently drifting onto a port that belongs to the session-runner (17204) or the
// Aspire dashboard (17205).
const sharedServerConfig = {
  port: parseInt(process.env['VITE_PORT'] ?? '17203'),
  strictPort: true,
  // Allow access via the per-machine reverse proxies (antiphon.laptop.codeperf.net /
  // antiphon.desktop.codeperf.net) and the machine-agnostic antiphon.localhost.codeperf.net,
  // in addition to localhost. The leading dot matches the domain and all its subdomains.
  allowedHosts: ['.laptop.codeperf.net', '.desktop.codeperf.net', '.localhost.codeperf.net'],
  proxy: {
    // ws:true is NOT only for /hubs. The phone-home control plane (CARD-0604) reaches the
    // server as GET /api/session-runners/{runnerId}/connect, a WebSocket upgrade under /api,
    // and antiphon.<machine>.codeperf.net fronts 17203 rather than 17202 — so a remote runner's
    // upgrade arrives here first. Without ws:true this proxy forwards the request with the
    // hop-by-hop Upgrade headers dropped, Kestrel sees a plain GET and refuses it with
    // "WebSocket upgrade is required" (409 phone_home_websocket_required) while POST
    // /api/session-runners/register keeps succeeding — the runner then registers forever and
    // never becomes available. Found by CARD-0604 CP-6a against the live server2 runner.
    '/api': {
      target: serverUrl,
      changeOrigin: true,
      ws: true,
      configure: createWsProxyTrace({ label: '/api' }),
    },
    '/hubs': {
      target: serverUrl,
      changeOrigin: true,
      ws: true,
    },
  },
}

// vite preview's `preview.headers` would apply to EVERY response including index.html, which is
// not content-hashed and must stay revalidatable — so the long-lived cache header goes on
// /assets/* only, via preview middleware, never on preview.headers.
function immutableAssetsCache(): Plugin {
  return {
    name: 'antiphon-immutable-assets-cache',
    configurePreviewServer(server) {
      server.middlewares.use((req, res, next) => {
        if (req.url?.startsWith('/assets/')) {
          res.setHeader('Cache-Control', 'public, max-age=31536000, immutable')
        }
        next()
      })
    },
  }
}

// The watcher (`vite build --watch`, client/scripts/serve.mjs) rebuilds in place so a rebuild
// never wipes dist/ out from under a mid-load page or a later lazy-loaded chunk. This only
// changes emptyOutDir when the env var is present; a plain `npm run build` is unaffected and gets
// Vite's normal default behaviour.
const buildConfig: { emptyOutDir?: boolean } = {}
if (process.env['ANTIPHON_VITE_KEEP_OUTDIR']) {
  buildConfig.emptyOutDir = false
}

export default defineConfig({
  plugins: [react(), immutableAssetsCache()],
  define: {
    __ANTIPHON_SHA__: JSON.stringify(gitSha()),
  },
  server: sharedServerConfig,
  preview: sharedServerConfig,
  build: {
    ...buildConfig,
    // Vite 8 / Rolldown: `manualChunks` object form is gone and the function form is
    // deprecated. `codeSplitting.groups` is the replacement that pins vendor packages
    // so a feature-code change does not invalidate the react/mantine/tanstack cache.
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            {
              name: 'vendor-react',
              test: /[\\/]node_modules[\\/](?:react|react-dom|scheduler|react-router)(?:[\\/]|$)/,
            },
            {
              name: 'vendor-mantine',
              test: /[\\/]node_modules[\\/]@mantine[\\/]/,
            },
            {
              name: 'vendor-tanstack',
              test: /[\\/]node_modules[\\/]@tanstack[\\/]/,
            },
          ],
        },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
    // The ONE test budget for the whole suite (CARD-0069). The 5s default had no real headroom:
    // measured on the 2026-08-19 baseline, the slowest interaction tests take 1.9–2.9s alone and
    // 2–4.6s inside a full parallel run on an otherwise-idle 8-thread machine (CardEditModal's
    // worst case hit 4 584ms — 92% of 5 000), so any concurrent build/agent load tips unrelated
    // files over the line and the suite grows a rotating flake cast. Before this was global, 14
    // files carried their own `vi.setConfig({ testTimeout: 20_000 })` — same number, scattered.
    // A timeout here is a hang detector, not a performance budget: do NOT re-add per-file
    // overrides, and do not raise this to absorb a genuinely slow test — make the test cheaper
    // (see docs/superpowers/plans/2026-08-19-card-0069-client-flake-cast-plan.md).
    testTimeout: 20_000,
  },
})
