/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// When running under Aspire, the server URL is injected via environment variable.
// Falls back to localhost:5000 for standalone dev.
const serverUrl =
  process.env['services__antiphon-server__http__0'] ??
  process.env['services__server__http__0'] ??
  'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env['VITE_PORT'] ?? '5173'),
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
