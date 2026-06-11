import { addons } from 'storybook/manager-api'
import { create } from 'storybook/theming'

// Link the Storybook brand (top-left title) back to the running app. Storybook is served at
// storybook.<app-host> on codeperf, or :17283 locally — so the app is the same host without the
// "storybook." prefix, or :17203 locally.
function appUrl(): string {
  if (typeof window === 'undefined') return '/'
  const { protocol, host, hostname } = window.location
  if (host.startsWith('storybook.')) return `${protocol}//${host.slice('storybook.'.length)}`
  return `${protocol}//${hostname}:17203`
}

addons.setConfig({
  theme: create({
    base: 'dark',
    brandTitle: 'Antiphon Stories ↗',
    brandUrl: appUrl(),
    brandTarget: '_self',
  }),
})
