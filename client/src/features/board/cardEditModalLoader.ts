import type { CardEditModal } from './CardEditModal'

let cardEditModal: Promise<{ default: typeof CardEditModal }> | undefined

/**
 * The edit dialog's lazy chunk, loaded at most once. `CardModal` builds its `lazy` component from
 * this, so anything that awaits it awaits the same promise React does (CARD-1256): `React.lazy`
 * still suspends on its first render, but once this has resolved its retry is only a microtask
 * away, which is what lets a test flush the whole open inside one `act` instead of racing a
 * `findBy*` window against a time-sliced render of a dialog that costs seconds of CPU in jsdom.
 * A rejected load stays rejected, exactly as `lazy` itself caches a failed payload.
 */
export function loadCardEditModal() {
  cardEditModal ??= import('./CardEditModal').then((m) => ({ default: m.CardEditModal }))
  return cardEditModal
}
