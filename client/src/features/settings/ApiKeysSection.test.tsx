import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { ApiKeysSection } from './ApiKeysSection'

vi.mock('@mantine/notifications', () => ({
  notifications: { show: vi.fn() },
}))

const apiKey = {
  id: 'key-1',
  name: 'anthropic-default',
  projectId: null,
  projectName: null,
  createdAt: '2026-08-20T12:00:00Z',
  updatedAt: '2026-08-20T12:00:00Z',
}

describe('ApiKeysSection', () => {
  it('replaces a global value write-only, then clears the field', async () => {
    let received: unknown = null
    server.use(
      http.get('/api/api-keys/global', () => HttpResponse.json([apiKey])),
      http.put('/api/api-keys/:name', async ({ request }) => {
        received = await request.json()
        return HttpResponse.json(apiKey)
      }),
    )

    renderWithProviders(<ApiKeysSection />)

    const replacement = await screen.findByLabelText('Replacement value for anthropic-default')
    await userEvent.type(replacement, 'new-secret-value')
    await userEvent.click(screen.getByRole('button', { name: 'Replace' }))

    await waitFor(() => expect(received).toEqual({ value: 'new-secret-value' }))
    expect(replacement).toHaveValue('')
    expect(screen.getByText('anthropic-default (configured)')).toBeInTheDocument()
    expect(screen.queryByText('new-secret-value')).not.toBeInTheDocument()
  })

  it('uses the project-scoped endpoints and clears a newly saved value', async () => {
    let received: unknown = null
    server.use(
      http.get('/api/projects/project-1/api-keys', () => HttpResponse.json([])),
      http.put('/api/projects/project-1/api-keys/:name', async ({ request }) => {
        received = await request.json()
        return HttpResponse.json({ ...apiKey, projectId: 'project-1' })
      }),
    )

    renderWithProviders(<ApiKeysSection projectId="project-1" />)

    await screen.findByText('No project API keys configured.')
    await userEvent.type(screen.getByLabelText('Name'), 'project-key')
    const value = screen.getByLabelText('Value (missing)')
    await userEvent.type(value, 'project-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save key' }))

    await waitFor(() => expect(received).toEqual({ value: 'project-secret' }))
    expect(value).toHaveValue('')
  })
})
