import type { StorybookConfig } from '@storybook/react-vite'

const PROXY_HOSTS = ['.laptop.codeperf.net', '.desktop.codeperf.net', '.localhost.codeperf.net']

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  addons: [],
  framework: {
    name: '@storybook/react-vite',
    options: {},
  },
  // Storybook's MANAGER server host-checks requests (403 "Invalid host" otherwise), so allow the
  // per-machine reverse-proxy hosts here so it works behind Caddy at storybook.antiphon.<m>.codeperf.net.
  core: {
    allowedHosts: PROXY_HOSTS,
  },
  // The PREVIEW iframe is served by Vite, which has its own allow-list (mirrors client/vite.config.ts).
  viteFinal: (viteConfig) => {
    viteConfig.server ??= {}
    viteConfig.server.allowedHosts = PROXY_HOSTS
    // Force a single React copy — Storybook's pre-bundle and the app deps must share one React or
    // Mantine's hooks fail with "Cannot read properties of null (reading 'useRef')".
    viteConfig.resolve ??= {}
    viteConfig.resolve.dedupe = [...(viteConfig.resolve.dedupe ?? []), 'react', 'react-dom']
    return viteConfig
  },
}

export default config
