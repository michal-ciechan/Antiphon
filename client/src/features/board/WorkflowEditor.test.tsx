import { fireEvent } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { act, renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { server } from '../../test/mocks/server'
import { WorkflowEditor } from './WorkflowEditor'

vi.mock('@monaco-editor/react', () => ({
  default: ({
    value,
    onChange,
  }: {
    value: string
    onChange: (value: string | undefined) => void
  }) => (
    <textarea
      aria-label="WORKFLOW.md editor"
      value={value}
      onChange={(event) => onChange(event.currentTarget.value)}
    />
  ),
}))

describe('WorkflowEditor', () => {
  it('save button puts content', async () => {
    const initialContent = `---
name: Existing
---
Work on {{ issue.title }}`
    const nextContent = `---
name: Updated
---
Commit {{ workspace.branch }}`
    const putSpy = vi.fn()

    server.use(
      http.get('/api/boards/board-1/workflow', () => {
        return HttpResponse.json({
          boardId: 'board-1',
          definitionId: 'definition-1',
          version: 1,
          name: 'Existing',
          content: initialContent,
          filePath: 'D:/repo/.antiphon/boards/board-1/WORKFLOW.md',
          updatedAt: '2026-01-01T00:00:00Z',
        })
      }),
      http.put('/api/boards/board-1/workflow', async ({ request }) => {
        const body = await request.json()
        putSpy(body)
        return HttpResponse.json({
          boardId: 'board-1',
          definitionId: 'definition-2',
          version: 2,
          name: 'Updated',
          content: (body as { content: string }).content,
          filePath: 'D:/repo/.antiphon/boards/board-1/WORKFLOW.md',
          updatedAt: '2026-01-01T00:01:00Z',
        })
      }),
    )

    renderWithProviders(<WorkflowEditor boardId="board-1" opened onClose={() => undefined} />)

    const editor = await screen.findByRole('textbox', { name: 'WORKFLOW.md editor' })
    await waitFor(() => expect(editor).toHaveValue(initialContent))

    fireEvent.change(editor, { target: { value: nextContent } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(putSpy).toHaveBeenCalledWith({ content: nextContent }))
  })

  it('does not overwrite unsaved edits when workflow data refetches', async () => {
    const initialContent = `---
name: Existing
---
Work on {{ issue.title }}`
    const externalContent = `---
name: External
---
Work on {{ issue.identifier }}`
    const draftContent = `---
name: Draft
---
Keep my local edit`
    let serverContent = initialContent

    server.use(
      http.get('/api/boards/board-1/workflow', () => {
        return HttpResponse.json({
          boardId: 'board-1',
          definitionId: 'definition-1',
          version: serverContent === initialContent ? 1 : 2,
          name: 'Existing',
          content: serverContent,
          filePath: 'D:/repo/.antiphon/boards/board-1/WORKFLOW.md',
          updatedAt: '2026-01-01T00:00:00Z',
        })
      }),
    )

    const { queryClient } = renderWithProviders(
      <WorkflowEditor boardId="board-1" opened onClose={() => undefined} />,
    )

    const editor = await screen.findByRole('textbox', { name: 'WORKFLOW.md editor' })
    await waitFor(() => expect(editor).toHaveValue(initialContent))

    fireEvent.change(editor, { target: { value: draftContent } })
    serverContent = externalContent
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: ['boards', 'board-1', 'workflow'] })
    })

    await waitFor(() => expect(editor).toHaveValue(draftContent))
  })
})
