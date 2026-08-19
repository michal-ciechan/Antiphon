/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// When running under Aspire, the server URL is injected via environment variable.
// Falls back to the fixed standalone dev backend on localhost:17202.
const serverUrl =
  process.env['services__antiphon-server__http__0'] ??
  process.env['services__server__http__0'] ??
  'http://localhost:17202'

export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env['VITE_PORT'] ?? '17203'),
    // Allow access via the per-machine reverse proxies (antiphon.laptop.codeperf.net /
    // antiphon.desktop.codeperf.net) and the machine-agnostic antiphon.localhost.codeperf.net,
    // in addition to localhost. The leading dot matches the domain and all its subdomains.
    allowedHosts: ['.laptop.codeperf.net', '.desktop.codeperf.net', '.localhost.codeperf.net'],
    proxy: {
      '/api': {
        target: serverUrl,
        changeOrigin: true,
      },
      '/hubs': {
        target: serverUrl,
        changeOrigin: true,
        ws: true,
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
