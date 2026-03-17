import { TextInput, Select, Group, Badge } from '@mantine/core'
import { VscSearch } from 'react-icons/vsc'
import type { WorkflowStatus } from '../../api/workflows'

export interface FilterState {
  search: string
  status: WorkflowStatus | ''
  project: string
}

interface DashboardFiltersProps {
  filters: FilterState
  onChange: (filters: FilterState) => void
  projectOptions: { value: string; label: string }[]
  pendingReviewCount: number
}

const STATUS_OPTIONS: { value: WorkflowStatus | ''; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Running', label: 'Active' },
  { value: 'GateWaiting', label: 'Pending Review' },
  { value: 'Paused', label: 'Paused' },
  { value: 'Completed', label: 'Complete' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Created', label: 'Created' },
  { value: 'Abandoned', label: 'Abandoned' },
]

/**
 * Filter bar for the dashboard: search, status filter, project filter.
 * Pending Review option shows a count badge when gates are waiting.
 */
export function DashboardFilters({ filters, onChange, projectOptions, pendingReviewCount }: DashboardFiltersProps) {
  const allProjectOptions = [{ value: '', label: 'All projects' }, ...projectOptions]

  // Render status options with pending review count
  const statusOptionsWithCount = STATUS_OPTIONS.map((opt) => {
    if (opt.value === 'GateWaiting' && pendingReviewCount > 0) {
      return { ...opt, label: `Pending Review (${pendingReviewCount})` }
    }
    return opt
  })

  return (
    <Group gap="sm" wrap="wrap">
      <TextInput
        placeholder="Search workflows..."
        leftSection={<VscSearch />}
        value={filters.search}
        onChange={(e) => onChange({ ...filters, search: e.currentTarget.value })}
        style={{ flex: 1, minWidth: 200 }}
        aria-label="Search workflows"
      />
      <Select
        data={statusOptionsWithCount}
        value={filters.status}
        onChange={(v) => onChange({ ...filters, status: (v ?? '') as WorkflowStatus | '' })}
        allowDeselect={false}
        style={{ width: 200 }}
        aria-label="Filter by status"
        rightSection={
          filters.status === 'GateWaiting' && pendingReviewCount > 0 ? (
            <Badge size="xs" color="orange" variant="filled" circle>
              {pendingReviewCount}
            </Badge>
          ) : undefined
        }
      />
      <Select
        data={allProjectOptions}
        value={filters.project}
        onChange={(v) => onChange({ ...filters, project: v ?? '' })}
        allowDeselect={false}
        style={{ width: 200 }}
        aria-label="Filter by project"
      />
    </Group>
  )
}
