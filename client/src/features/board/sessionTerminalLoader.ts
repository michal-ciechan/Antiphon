import type { SessionTerminal } from './SessionTerminal'

let sessionTerminal: Promise<{ default: typeof SessionTerminal }> | undefined

/**
 * The session terminal's lazy chunk, loaded at most once. `SessionTabs` builds its `lazy`
 * component from this, so a test that awaits it awaits the same promise React does (CARD-1246,
 * the CARD-1256 pattern in `cardEditModalLoader.ts`): `React.lazy` still suspends on its first
 * render, but its retry is then a microtask away and lands inside an async `act`, instead of a
 * `findBy*` window racing it on a loaded host. A rejected load stays rejected, as `lazy` caches it.
 */
export function loadSessionTerminal() {
  sessionTerminal ??= import('./SessionTerminal').then((m) => ({ default: m.SessionTerminal }))
  return sessionTerminal
}
