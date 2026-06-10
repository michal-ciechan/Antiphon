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
    // antiphon.desktop.codeperf.net) in addition to localhost. The leading dot matches the
    // domain and all its subdomains.
    allowedHosts: ['.laptop.codeperf.net', '.desktop.codeperf.net'],
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
  },
})
