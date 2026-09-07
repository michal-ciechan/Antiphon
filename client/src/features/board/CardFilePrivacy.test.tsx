import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import type { CardFileStatus } from '../../api/cardFiles'
import { BoardCardFileSettings } from './BoardCardFileSettings'
import { CardFilePolicyText, PrivateNotesPanel } from './CardFilePrivacy'

const status: CardFileStatus = { boardId: 'b1', enabled: true, syncCardFiles: false, repositoryVisibility: 'Unknown', visibilitySource: 'Unknown', repositoryPath: 'C:\\src\\example', directory: 'docs/cards/example', eligible: false, reason: 'board_not_opted_in', warnings: ['repository_visibility_unknown'], ignored: null, workingTreeRemovalPending: false, gitRemovalPending: false, removalPending: false, autoCommit: false, intervalSeconds: 60 }

describe('card-file privacy controls', () => {
  it('loads notes only on explicit open and displays inert text without resource elements', async () => {
    const get = vi.fn()
    const sentinel = 'C408_PRIVATE <img src="https://example.invalid/leak"> ![x](https://example.invalid/x) <script>x</script>'
    server.use(http.get('/api/cards/c1/private-notes', () => { get(); return HttpResponse.json({ cardId: 'c1', privateNotes: sentinel, concurrencyToken: 't1', revisionNumber: null }) }))
    const view = renderWithProviders(<PrivateNotesPanel cardId="c1" />)
    expect(get).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Open private notes' }))
    await waitFor(() => expect(screen.queryByText('Loading private notes...')).not.toBeInTheDocument())
    expect(view.container.querySelector('img, iframe, script')).toBeNull()
    expect(await screen.findByText(sentinel)).toBeInTheDocument()
    const ordinary = view.queryClient.getQueryCache().getAll().filter((q) => q.queryKey[0] !== 'private-notes').map((q) => q.state.data)
    expect(JSON.stringify(ordinary)).not.toContain('C408_PRIVATE')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    await userEvent.click(screen.getByRole('button', { name: 'Hide private notes' }))
    expect(screen.queryByText(sentinel)).not.toBeInTheDocument()
  })

  it.each([null, ''])('distinguishes unknown historical notes from a known empty snapshot (%s)', async (text) => {
    server.use(http.get('/api/cards/c1/private-notes', ({ request }) => {
      expect(new URL(request.url).searchParams.get('revisionNumber')).toBe('3')
      return HttpResponse.json({ cardId: 'c1', privateNotes: text, concurrencyToken: 't1', revisionNumber: 3 })
    }))
    renderWithProviders(<PrivateNotesPanel cardId="c1" revisionNumber={3} />)
    await userEvent.click(screen.getByRole('button', { name: 'Inspect private-note snapshot' }))
    expect(await screen.findByText(text === null ? 'Private-note history unknown' : 'No private notes')).toBeInTheDocument()
  })

  it('starts publishing off, makes no mutation on mount, and sends the expected state on explicit save', async () => {
    const put = vi.fn()
    const post = vi.fn()
    server.use(http.get('/api/boards/b1/card-files/status', () => HttpResponse.json(status)),
      http.put('/api/boards/b1/card-files/settings', async ({ request }) => { put(await request.json()); return HttpResponse.json({ detail: 'Unknown repository', code: 'card_file_policy_refused' }, { status: 409 }) }),
      http.post('/api/boards/b1/card-files/sync', () => { post(); return HttpResponse.json({}) }))
    renderWithProviders(<BoardCardFileSettings boardId="b1" />)
    await waitFor(() => expect(screen.getByRole('switch')).toBeEnabled())
    expect(screen.getByRole('switch')).not.toBeChecked()
    expect(put).not.toHaveBeenCalled()
    expect(post).not.toHaveBeenCalled()
    expect(screen.getByText(/Unknown repository visibility; not checked/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('switch'))
    await userEvent.click(screen.getByRole('button', { name: 'Save publishing policy' }))
    await waitFor(() => expect(put).toHaveBeenCalledWith({ syncCardFiles: true, expectedSyncCardFiles: false }))
    expect(await screen.findByText(/Unknown repository$/)).toBeInTheDocument()
    expect(screen.getByRole('switch')).toBeChecked()
  })

  it.each([
    { workingTreeRemovalPending: true, gitRemovalPending: true, expected: 'reconcile previously exported' },
    { workingTreeRemovalPending: false, gitRemovalPending: true, expected: 'Git index/HEAD cleanup required' },
    { workingTreeRemovalPending: null, gitRemovalPending: null, expected: 'erasure not confirmed' },
  ])('reports pending cleanup without claiming erasure: $expected', (row) => {
    renderWithProviders(<CardFilePolicyText status={{ ...status, ...row, removalPending: true }} />)
    expect(screen.getByText(new RegExp(row.expected))).toBeInTheDocument()
  })

  it('shows configured Public warning even when blocked and retains the safe target', () => {
    renderWithProviders(<CardFilePolicyText status={{ ...status, repositoryVisibility: 'Public' }} />)
    expect(screen.getByText('Not written: board_not_opted_in')).toBeInTheDocument()
    expect(screen.getByText('Target: C:\\src\\example\\docs\\cards\\example')).toBeInTheDocument()
    expect(screen.getByText('PUBLIC REPOSITORY: public card fields will be written on sync')).toBeInTheDocument()
  })
  it.each([409, 422])('preserves the enable choice and refreshes policy after rejection %s', async (code) => {
    const get = vi.fn()
    server.use(http.get('/api/boards/b1/card-files/status', () => { get(); return HttpResponse.json(status) }),
      http.put('/api/boards/b1/card-files/settings', () => HttpResponse.json({ detail: 'Policy rejected', errors: { syncCardFiles: ['Choose a valid policy'] } }, { status: code })))
    renderWithProviders(<BoardCardFileSettings boardId="b1" />)
    await waitFor(() => expect(screen.getByRole('switch')).toBeEnabled())
    await userEvent.click(screen.getByRole('switch'))
    await userEvent.click(screen.getByRole('button', { name: 'Save publishing policy' }))
    expect(await screen.findByText(/Choose a valid policy/)).toBeInTheDocument()
    await waitFor(() => expect(get.mock.calls.length).toBeGreaterThan(1))
    expect(screen.getByRole('switch')).toBeChecked()
  })

  it('enables a configured Public board directly and covers current and future Inherit cards', async () => {
    let current = { ...status, repositoryVisibility: 'Public' as const }
    const put = vi.fn()
    server.use(http.get('/api/boards/b1/card-files/status', () => HttpResponse.json(current)),
      http.put('/api/boards/b1/card-files/settings', async ({ request }) => {
        put(await request.json()); current = { ...current, syncCardFiles: true }; return HttpResponse.json(current)
      }))
    renderWithProviders(<BoardCardFileSettings boardId="b1" />)
    await waitFor(() => expect(screen.getByRole('switch')).toBeEnabled())
    expect(screen.getByText(/Applies to current and future Inherit cards/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('switch'))
    await userEvent.click(screen.getByRole('button', { name: 'Save publishing policy' }))
    await waitFor(() => expect(put).toHaveBeenCalledExactlyOnceWith({ syncCardFiles: true, expectedSyncCardFiles: false }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save publishing policy' })).toBeDisabled())
    expect(screen.getByRole('switch')).toBeChecked()
  })

  it.each([true, false])('shows HTTP 200 policy refusal with separate eligible and written counts (preview=%s)', async (dryRun) => {
    const post = vi.fn()
    server.use(http.get('/api/boards/b1/card-files/status', () => HttpResponse.json(status)),
      http.post('/api/boards/b1/card-files/sync', ({ request }) => {
        post(new URL(request.url).searchParams.get('dryRun'))
        return HttpResponse.json({ dryRun, eligibleCards: 2, written: 0, deleted: 1, writeSkipReason: 'card_file_path_ignored', error: null })
      }))
    renderWithProviders(<BoardCardFileSettings boardId="b1" />)
    await userEvent.click(screen.getByRole('button', { name: dryRun ? 'Preview sync' : 'Sync card files' }))
    expect(await screen.findByText(new RegExp(`Not written: card_file_path_ignored; eligible 2; ${dryRun ? 'would write' : 'written'} 0; ${dryRun ? 'would delete' : 'deleted'} 1`))).toBeInTheDocument()
    expect(post).toHaveBeenCalledExactlyOnceWith(String(dryRun))
  })

  it('switching the keyed card panel clears the prior note and requires another explicit open', async () => {
    const second = vi.fn()
    server.use(http.get('/api/cards/c1/private-notes', () => HttpResponse.json({ cardId: 'c1', privateNotes: 'C408_FIRST_PRIVATE' })),
      http.get('/api/cards/c2/private-notes', () => { second(); return HttpResponse.json({ cardId: 'c2', privateNotes: 'C408_SECOND_PRIVATE' }) }))
    const view = renderWithProviders(<PrivateNotesPanel key="c1" cardId="c1" />)
    await userEvent.click(screen.getByRole('button', { name: 'Open private notes' }))
    expect(await screen.findByText('C408_FIRST_PRIVATE')).toBeInTheDocument()
    view.rerender(<PrivateNotesPanel key="c2" cardId="c2" />)
    expect(screen.queryByText('C408_FIRST_PRIVATE')).not.toBeInTheDocument()
    expect(second).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Open private notes' }))
    expect(await screen.findByText('C408_SECOND_PRIVATE')).toBeInTheDocument()
    expect(screen.queryByText('C408_FIRST_PRIVATE')).not.toBeInTheDocument()
  })

})
