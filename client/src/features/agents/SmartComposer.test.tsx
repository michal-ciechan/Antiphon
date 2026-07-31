import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen, userEvent, waitFor } from '../../test/utils'
import { SmartComposer } from './SmartComposer'

describe('SmartComposer', () => {
  it('renders the textbox immediately when not collapsible', () => {
    renderWithProviders(<SmartComposer sessionId="s1" />)
    expect(screen.getByPlaceholderText(/Message to the agent/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Message the agent…/ })).not.toBeInTheDocument()
  })

  // The dispatch hint used to be a permanent text row under the composer; it now lives in a
  // tooltip on the send button so the composer spends no vertical space on it.
  it('shows the dispatch hint as a tooltip on the send button, not as an inline row', async () => {
    const user = userEvent.setup()
    renderWithProviders(<SmartComposer sessionId="s1" />)
    expect(screen.queryByText(/Enter to send, Shift\+Enter for a newline/)).not.toBeInTheDocument()

    await user.hover(screen.getByRole('button', { name: /Send now/ }))
    await waitFor(() =>
      expect(
        screen.getByText(/Delivered immediately as a message, even mid-task\. Enter to send/),
      ).toBeInTheDocument(),
    )
  })

  it('starts as a single action row when collapsible and expands on press', async () => {
    const user = userEvent.setup()
    renderWithProviders(<SmartComposer sessionId="s1" collapsible actions={<span>status-pill</span>} />)

    // Collapsed: no textbox, but the action row (status slot + mode picker + opener) is there.
    expect(screen.queryByPlaceholderText(/Message to the agent/)).not.toBeInTheDocument()
    expect(screen.getByText('status-pill')).toBeInTheDocument()
    expect(screen.getByLabelText('Send now')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Message the agent…/ }))
    const textarea = await screen.findByPlaceholderText(/Message to the agent/)
    expect(textarea).toHaveFocus()
    // The status slot stays on the action row below the textbox.
    expect(screen.getByText('status-pill')).toBeInTheDocument()
  })

  it('collapses back on Escape when the box is empty, but not while it has text', async () => {
    const user = userEvent.setup()
    renderWithProviders(<SmartComposer sessionId="s1" collapsible />)
    await user.click(screen.getByRole('button', { name: /Message the agent…/ }))
    const textarea = await screen.findByPlaceholderText(/Message to the agent/)

    await user.type(textarea, 'keep me')
    await user.keyboard('{Escape}')
    expect(screen.getByPlaceholderText(/Message to the agent/)).toBeInTheDocument()

    await user.clear(textarea)
    await user.keyboard('{Escape}')
    expect(screen.queryByPlaceholderText(/Message to the agent/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Message the agent…/ })).toBeInTheDocument()
  })

  it('does not send when the box is empty (data-disabled keeps the tooltip alive)', async () => {
    const user = userEvent.setup()
    renderWithProviders(<SmartComposer sessionId="s1" />)
    const send = screen.getByRole('button', { name: /Send now/ })
    expect(send).toHaveAttribute('data-disabled', 'true')
    // No MSW handler is registered for the enqueue endpoint — a dispatch here would throw
    // onUnhandledRequest and fail the test.
    await user.click(send)
  })
})
