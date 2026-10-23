import { describe, expect, it } from 'vitest'
import type { AttentionItemDto } from '../../api/attention'
import { visualOf, targetOf } from './attentionVisuals'
import { LIVENESS_KINDS } from '../home/tasks/homeTasksModel'

describe('C1518 provider input decision', () => {
  it('C1518 maps the server kind and retains task navigation', () => {
    expect(visualOf('ProviderInputWait').label).toBe('Provider input')
    expect(visualOf('ProviderInputWait').color).toBe('warning')
    expect(LIVENESS_KINDS.has('ProviderInputWait')).toBe(true)
    const item = { kind: 'ProviderInputWait', taskId: 'task', actions: ['OpenDrawer', 'OpenAgent', 'OpenCard'] } as AttentionItemDto
    expect(targetOf(item)).toBe('/orchestrator?tab=delegations&task=task')
  })
})
