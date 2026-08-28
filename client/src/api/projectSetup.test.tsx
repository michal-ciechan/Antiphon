import { HttpResponse, http } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderHookWithProviders, waitFor } from '../test/utils'
import { server } from '../test/mocks/server'
import { useProjectReadinessList } from './projectSetup'

describe('useProjectReadinessList', () => {
  it('uses one batch request and retries it once at most', async () => {
    const requests: string[] = []
    server.use(
      http.get('/api/projects/readiness', ({ request }) => {
        requests.push(new URL(request.url).searchParams.get('ids') ?? '')
        return HttpResponse.json([
          { projectId: 'project-a', canDispatch: true, checks: [] },
          { projectId: 'project-b', canDispatch: true, checks: [] },
          { projectId: 'project-c', canDispatch: true, checks: [] },
        ])
      }),
    )

    const { result } = renderHookWithProviders(() =>
      useProjectReadinessList(['project-a', 'project-b', 'project-c']),
    )

    await waitFor(() => expect(result.current.data).toHaveLength(3))
    expect(requests).toEqual(['project-a,project-b,project-c'])
    expect(result.current.failureCount).toBe(0)
  })
})
