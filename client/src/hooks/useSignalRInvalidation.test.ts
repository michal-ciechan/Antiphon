import type { HubConnection } from '@microsoft/signalr'
import { act, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { server } from '../test/mocks/server'
import { useCardFileStatus, usePrivateNotes } from '../api/cardFiles'
import { expect, it, vi } from 'vitest'
import { renderHookWithProviders } from '../test/utils'
import { useSignalRInvalidation } from './useSignalRInvalidation'

it('ID events invalidate policy, projects, card and explicit notes without copying payload text', () => {
  const callbacks = new Map<string, (p: object) => void>()
  const connection = { on: vi.fn((event: string, callback: (p: object) => void) => callbacks.set(event, callback)), off: vi.fn() }
  const ref = { current: connection as unknown as HubConnection }
  const view = renderHookWithProviders(() => useSignalRInvalidation(ref))
  for (const key of [['boards', 'b1'], ['projects', 'p1'], ['card-file-status', 'b1'], ['cards', 'c1'], ['private-notes', 'c1', 'current']]) view.queryClient.setQueryData(key, { marker: 'existing' })
  act(() => callbacks.get('BoardChanged')!({ projectId: 'p1', boardId: 'b1' }))
  expect(view.queryClient.getQueryState(['projects', 'p1'])?.isInvalidated).toBe(true)
  expect(view.queryClient.getQueryState(['card-file-status', 'b1'])?.isInvalidated).toBe(true)
  act(() => callbacks.get('CardChanged')!({ cardId: 'c1', boardId: 'b1' }))
  expect(view.queryClient.getQueryState(['private-notes', 'c1', 'current'])?.isInvalidated).toBe(true)
  expect(view.queryClient.getQueryState(['cards', 'c1'])?.isInvalidated).toBe(true)
  expect(view.queryClient.getQueryData(['boards', 'b1'])).toEqual({ marker: 'existing' })
  view.unmount()
  expect(connection.off).toHaveBeenCalled()
})

it('mounted policy and explicit notes refetch after ID-only events', async () => {
  const callbacks = new Map<string, (p: object) => void>()
  const ref = { current: { on: (name: string, fn: (p: object) => void) => callbacks.set(name, fn), off: vi.fn() } as unknown as HubConnection }
  let version = 1
  server.use(http.get('/api/boards/b1/card-files/status', () => HttpResponse.json({ syncCardFiles: version === 2 })),
    http.get('/api/cards/c1/private-notes', () => HttpResponse.json({ cardId: 'c1', privateNotes: `C408_NOTE_${version}` })))
  const view = renderHookWithProviders(() => {
    useSignalRInvalidation(ref)
    return { policy: useCardFileStatus('b1'), notes: usePrivateNotes('c1', true) }
  })
  await waitFor(() => expect(view.result.current.notes.data?.privateNotes).toBe('C408_NOTE_1'))
  expect(view.result.current.policy.data?.syncCardFiles).toBe(false)
  version = 2
  act(() => callbacks.get('BoardChanged')!({ projectId: 'p1' }))
  act(() => callbacks.get('CardChanged')!({ boardId: 'b1', cardId: 'c1' }))
  await waitFor(() => expect(view.result.current.policy.data?.syncCardFiles).toBe(true))
  await waitFor(() => expect(view.result.current.notes.data?.privateNotes).toBe('C408_NOTE_2'))
  expect(view.queryClient.getQueryCache().getAll().filter(q => q.queryKey[0] !== 'private-notes').map(q => q.state.data).some(data => JSON.stringify(data).includes('C408_NOTE'))).toBe(false)
})
