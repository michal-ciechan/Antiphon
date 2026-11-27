import { act, renderHook } from '@testing-library/react'
import type { HubConnection } from '@microsoft/signalr'
import { notifications } from '@mantine/notifications'
import { expect, it, vi } from 'vitest'
import { useAlertToasts } from './useAlertToasts'

vi.mock('@mantine/notifications', () => ({ notifications: { show: vi.fn() } }))

it('delivers one operator toast per incident across partial publication, replay and remount', () => {
  const desktop = vi.fn(class { static permission = 'granted' })
  Object.defineProperty(desktop, 'permission', { value: 'granted' })
  vi.stubGlobal('Notification', desktop)
  const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
  const callbacks = new Map<string, (value: object) => void>()
  const connection = { on: (name: string, callback: (value: object) => void) => callbacks.set(name, callback),
    off: (name: string) => callbacks.delete(name) }
  const ref = { current: connection as unknown as HubConnection }
  const first = { id: 'test-1101', severity: 'Critical', title: 'Overdue', source: 'ChannelBridge' }
  const second = { ...first, id: 'test-1102' }
  const mount = () => renderHook(() => useAlertToasts(ref))
  const view = mount()
  act(() => callbacks.get('AlertRaised')!(first))
  act(() => callbacks.get('AgentChanged')!({ operatorAlert: first }))
  view.unmount()
  const resumed = mount()
  act(() => callbacks.get('AlertRaised')!(first))
  act(() => callbacks.get('AgentChanged')!({ operatorAlert: second }))
  expect(vi.mocked(notifications.show).mock.calls.map(([toast]) => toast.id)).toEqual([first.id, second.id])
  expect(desktop).toHaveBeenCalledTimes(2)
  resumed.unmount()
  hidden.mockRestore()
  vi.unstubAllGlobals()
})
