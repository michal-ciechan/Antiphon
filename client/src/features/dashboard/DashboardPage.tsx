import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import { Container, Title, Button, Group, SimpleGrid, Loader, Box } from '@mantine/core'
import {
  useWorkflows,
  type WorkflowDto,
} from '../../api/workflows'
import { useProjects } from '../../api/projects'
import { WorkflowCard } from './WorkflowCard'
import { DashboardFilters, type FilterState } from './DashboardFilters'
import { EmptyState } from './EmptyState'
import { NewWorkflowDialog } from './NewWorkflowDialog'
import { useToastNotifications } from './useToastNotifications'

const INITIAL_FILTERS: FilterState = {
  search: '',
  status: '',
  project: '',
}

function hasActiveFilters(filters: FilterState): boolean {
  return filters.search !== '' || filters.status !== '' || filters.project !== ''
}

function matchesFilter(wf: WorkflowDto, filters: FilterState): boolean {
  // Search filter: matches name, template, project, or current stage
  if (filters.search) {
    const term = filters.search.toLowerCase()
    const searchable = [wf.name, wf.templateName, wf.projectName, wf.currentStageName ?? '']
      .join(' ')
      .toLowerCase()
    if (!searchable.includes(term)) return false
  }

  // Status filter
  if (filters.status && wf.status !== filters.status) return false

  // Project filter
  if (filters.project && wf.projectId !== filters.project) return false

  return true
}

/**
 * Dashboard page displaying all workflows in a responsive card grid.
 * Replaces the previous table-based layout (Epic 2) with WorkflowCards (Epic 4).
 *
 * Features:
 * - Responsive card grid (SimpleGrid)
 * - Filter bar with search, status, and project filters
 * - Pending Review filter shows count badge
 * - Empty states for no workflows and no filter results
 * - Real-time card updates via SignalR with highlight animation
 * - New workflow fade-in animation
 * - Toast notifications for connection status
 */
export function DashboardPage() {
  const { data: workflows, isLoading } = useWorkflows()
  const { data: projects } = useProjects()
  const [dialogOpened, setDialogOpened] = useState(false)
  const [filters, setFilters] = useState<FilterState>(INITIAL_FILTERS)

  // Track workflow IDs for detecting new workflows and updates
  const prevWorkflowMapRef = useRef<Map<string, WorkflowDto>>(new Map())
  const [highlightedIds, setHighlightedIds] = useState<Set<string>>(new Set())
  const [newIds, setNewIds] = useState<Set<string>>(new Set())

  // Enable toast notifications for connection status
  useToastNotifications()

  // Detect new and updated workflows for animations
  useEffect(() => {
    if (!workflows) return

    const prevMap = prevWorkflowMapRef.current
    const currentMap = new Map(workflows.map((wf) => [wf.id, wf]))
    const newHighlights = new Set<string>()
    const newEntries = new Set<string>()

    for (const wf of workflows) {
      const prev = prevMap.get(wf.id)
      if (!prev) {
        // New workflow appeared
        if (prevMap.size > 0) {
          // Only animate if this isn't the initial load
          newEntries.add(wf.id)
        }
      } else if (
        prev.status !== wf.status ||
        prev.completedStageCount !== wf.completedStageCount ||
        prev.currentStageName !== wf.currentStageName
      ) {
        // Workflow was updated
        newHighlights.add(wf.id)
      }
    }

    if (newHighlights.size > 0) {
      setHighlightedIds(newHighlights)
      // Clear highlights after animation completes
      setTimeout(() => setHighlightedIds(new Set()), 1500)
    }

    if (newEntries.size > 0) {
      setNewIds(newEntries)
      // Clear fade-in tracking after animation completes
      setTimeout(() => setNewIds(new Set()), 500)
    }

    prevWorkflowMapRef.current = currentMap
  }, [workflows])

  // Compute derived data
  const pendingReviewCount = useMemo(
    () => workflows?.filter((wf) => wf.status === 'GateWaiting').length ?? 0,
    [workflows],
  )

  const projectOptions = useMemo(
    () => (projects ?? []).map((p) => ({ value: p.id, label: p.name })),
    [projects],
  )

  const filteredWorkflows = useMemo(
    () => (workflows ?? []).filter((wf) => matchesFilter(wf, filters)),
    [workflows, filters],
  )

  const handleClearFilters = useCallback(() => {
    setFilters(INITIAL_FILTERS)
  }, [])

  const handleOpenDialog = useCallback(() => {
    setDialogOpened(true)
  }, [])

  return (
    <Container size="xl" py="xl">
      <NewWorkflowDialog opened={dialogOpened} onClose={() => setDialogOpened(false)} />

      <Group justify="space-between" mb="lg">
        <Title order={2}>Workflows</Title>
        <Button onClick={handleOpenDialog}>New Workflow</Button>
      </Group>

      {/* Filter bar */}
      {workflows && workflows.length > 0 && (
        <Box mb="lg">
          <DashboardFilters
            filters={filters}
            onChange={setFilters}
            projectOptions={projectOptions}
            pendingReviewCount={pendingReviewCount}
          />
        </Box>
      )}

      {/* Loading state */}
      {isLoading && (
        <Box style={{ display: 'flex', justifyContent: 'center', paddingTop: 60 }}>
          <Loader size="lg" />
        </Box>
      )}

      {/* No workflows at all */}
      {!isLoading && workflows && workflows.length === 0 && (
        <EmptyState variant="no-workflows" onCreateWorkflow={handleOpenDialog} />
      )}

      {/* Workflows exist but filters match nothing */}
      {!isLoading && workflows && workflows.length > 0 && filteredWorkflows.length === 0 && hasActiveFilters(filters) && (
        <EmptyState variant="no-results" onClearFilters={handleClearFilters} />
      )}

      {/* Card grid */}
      {!isLoading && filteredWorkflows.length > 0 && (
        <SimpleGrid
          cols={{ base: 1, sm: 2, lg: 3 }}
          spacing="md"
          verticalSpacing="md"
        >
          {filteredWorkflows.map((wf) => (
            <WorkflowCard
              key={wf.id}
              workflow={wf}
              highlight={highlightedIds.has(wf.id)}
              fadeIn={newIds.has(wf.id)}
            />
          ))}
        </SimpleGrid>
      )}
    </Container>
  )
}
