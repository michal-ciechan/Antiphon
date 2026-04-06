import { useState } from 'react'
import {
  Modal,
  Select,
  Textarea,
  Button,
  Alert,
  Stack,
  Text,
  Collapse,
  Table,
  TextInput,
  Autocomplete,
  Group,
  ActionIcon,
  Tooltip,
  Checkbox,
  Badge,
  Loader,
} from '@mantine/core'
import { TbChevronDown, TbChevronRight, TbRefresh, TbCheck } from 'react-icons/tb'
import { useNavigate } from 'react-router'
import {
  useTemplates,
  useModelRoutingsByTemplate,
  useProviders,
  useTemplateStages,
  type WorkflowTemplateDto,
} from '../../api/settings'
import { useProjects, useRepoBranches, type ProjectDto } from '../../api/projects'
import { useCreateWorkflow, useFeatureStatus } from '../../api/workflows'
import { ApiError } from '../../api/client'

interface NewWorkflowDialogProps {
  opened: boolean
  onClose: () => void
}

function formatApiError(error: unknown): string {
  if (error instanceof ApiError) {
    if (typeof error.body === 'object' && error.body !== null) {
      const body = error.body as Record<string, unknown>
      if (typeof body.detail === 'string') return body.detail
      if (typeof body.title === 'string') return body.title
      if (typeof body.message === 'string') return body.message
    }
    return `Error ${error.status}: ${error.statusText}`
  }
  if (error instanceof Error) return error.message
  return 'An unexpected error occurred'
}

function parseOwnerRepo(url: string): { owner: string; repo: string } | null {
  if (!url) return null
  try {
    const parsed = new URL(url)
    const parts = parsed.pathname.replace(/^\//, '').replace(/\.git$/, '').split('/')
    if (parts.length >= 2 && parts[0] && parts[1]) {
      return { owner: parts[0], repo: parts[1] }
    }
  } catch {
    // not a valid URL — ignore
  }
  return null
}

export function NewWorkflowDialog({ opened, onClose }: NewWorkflowDialogProps) {
  const navigate = useNavigate()
  const { data: templates, isLoading: templatesLoading } = useTemplates()
  const { data: projects, isLoading: projectsLoading } = useProjects()
  const { data: providers } = useProviders()
  const createWorkflow = useCreateWorkflow()

  const [templateId, setTemplateId] = useState<string | null>(null)
  const [projectId, setProjectId] = useState<string | null>(null)
  const [initialContext, setInitialContext] = useState('')
  const [featureName, setFeatureName] = useState('')
  const [overridesOpen, setOverridesOpen] = useState(false)
  const [stageModelOverrides, setStageModelOverrides] = useState<Record<string, string>>({})
  const [selectedStages, setSelectedStages] = useState<string[]>([])

  // Parse owner/repo from project's gitRepositoryUrl for GitHub branch lookup
  const selectedProject = (projects ?? []).find((p: ProjectDto) => p.id === projectId) ?? null
  const ownerRepo = selectedProject ? parseOwnerRepo(selectedProject.gitRepositoryUrl) : null
  const { data: branches, isLoading: branchesLoading } = useRepoBranches(
    ownerRepo?.owner ?? null,
    ownerRepo?.repo ?? null
  )

  const { data: templateRoutings } = useModelRoutingsByTemplate(templateId ?? undefined)

  const selectedTemplate = (templates ?? []).find((t) => t.id === templateId) ?? null

  const { data: templateStages, isLoading: stagesLoading } = useTemplateStages(
    selectedTemplate?.selectableStages ? templateId : null
  )

  const trimmedFeatureName = featureName.trim()
  const { data: featureStatus } = useFeatureStatus(
    projectId,
    trimmedFeatureName.length >= 2 ? trimmedFeatureName : null
  )

  // When stages load, initialise selectedStages to all non-completed stages
  const completedStages = featureStatus?.completedStages ?? []

  function handleClose() {
    if (createWorkflow.isPending) return
    setTemplateId(null)
    setProjectId(null)
    setInitialContext('')
    setFeatureName('')
    setOverridesOpen(false)
    setStageModelOverrides({})
    setSelectedStages([])
    createWorkflow.reset()
    onClose()
  }

  function handleTemplateChange(id: string | null) {
    setTemplateId(id)
    setStageModelOverrides({})
    setSelectedStages([])
  }

  function handleOverrideChange(stageName: string, value: string) {
    setStageModelOverrides((prev) => ({ ...prev, [stageName]: value }))
  }

  function handleResetOverride(stageName: string) {
    setStageModelOverrides((prev) => {
      const next = { ...prev }
      delete next[stageName]
      return next
    })
  }

  // Initialise selected stages when stage data arrives
  function getEffectiveSelectedStages(): string[] {
    if (!templateStages || templateStages.length === 0) return selectedStages
    // If selectedStages hasn't been initialised yet (empty), default to all non-completed
    if (selectedStages.length === 0 && templateStages.length > 0) {
      return templateStages
        .filter((s) => !completedStages.includes(s.name))
        .map((s) => s.name)
    }
    return selectedStages
  }

  function handleStageToggle(stageName: string, checked: boolean) {
    const current = getEffectiveSelectedStages()
    if (checked) {
      setSelectedStages([...current, stageName])
    } else {
      setSelectedStages(current.filter((s) => s !== stageName))
    }
  }

  // When stages load for the first time, seed selectedStages
  function ensureStagesInitialised() {
    if (
      templateStages &&
      templateStages.length > 0 &&
      selectedStages.length === 0
    ) {
      setSelectedStages(
        templateStages
          .filter((s) => !completedStages.includes(s.name))
          .map((s) => s.name)
      )
    }
  }
  // Side-effect-free initialisation: call during render is fine since it only
  // calls setSelectedStages when selectedStages is empty
  if (
    templateStages &&
    templateStages.length > 0 &&
    selectedStages.length === 0 &&
    templateId !== null
  ) {
    ensureStagesInitialised()
  }

  async function handleCreate() {
    if (!templateId || !projectId) return

    const activeOverrides = Object.fromEntries(
      Object.entries(stageModelOverrides).filter(([, v]) => v.trim() !== '')
    )

    const effectiveSelectedStages =
      selectedTemplate?.selectableStages && templateStages && templateStages.length > 0
        ? getEffectiveSelectedStages()
        : undefined

    try {
      const result = await createWorkflow.mutateAsync({
        templateId,
        projectId,
        initialContext: initialContext.trim() || undefined,
        stageModelOverrides: Object.keys(activeOverrides).length > 0 ? activeOverrides : undefined,
        featureName: trimmedFeatureName || undefined,
        selectedStages: effectiveSelectedStages,
      })
      handleClose()
      navigate(`/workflow/${result.id}`)
    } catch {
      // Error is captured in createWorkflow.error
    }
  }

  // Build template options — Mantine v8 grouped format: { group, items }[]
  const templateOptions = (() => {
    const grouped = new Map<string, WorkflowTemplateDto[]>()
    const ungrouped: WorkflowTemplateDto[] = []
    for (const t of templates ?? []) {
      if (t.templateGroupName) {
        if (!grouped.has(t.templateGroupName)) grouped.set(t.templateGroupName, [])
        grouped.get(t.templateGroupName)!.push(t)
      } else {
        ungrouped.push(t)
      }
    }
    const result: ({ value: string; label: string; description: string } | { group: string; items: { value: string; label: string; description: string }[] })[] = ungrouped.map(t => ({
      value: t.id,
      label: t.name,
      description: t.description,
    }))
    for (const [group, items] of grouped) {
      result.push({
        group,
        items: items.map(t => ({ value: t.id, label: t.name, description: t.description })),
      })
    }
    return result
  })()

  const projectOptions = (projects ?? []).map((p: ProjectDto) => ({
    value: p.id,
    label: p.name,
    description: p.gitRepositoryUrl,
  }))

  const canCreate = !!templateId && !!projectId && !createWorkflow.isPending

  const effectiveSelected = getEffectiveSelectedStages()

  return (
    <Modal
      opened={opened}
      onClose={handleClose}
      title="New Workflow"
      size="lg"
      closeOnClickOutside={!createWorkflow.isPending}
      closeOnEscape={!createWorkflow.isPending}
    >
      <Stack gap="md">
        <Select
          label="Workflow Template"
          placeholder="Select a template"
          data={templateOptions}
          value={templateId}
          onChange={handleTemplateChange}
          disabled={createWorkflow.isPending}
          searchable
          nothingFoundMessage={templatesLoading ? 'Loading...' : 'No templates found'}
          renderOption={({ option }) => {
            const tpl = templateOptions.find((t) => 'value' in t && t.value === option.value) as
              | { value: string; label: string; description: string }
              | undefined
            return (
              <div>
                <Text size="sm" fw={500}>{option.label}</Text>
                {tpl?.description && (
                  <Text size="xs" c="dimmed">{tpl.description}</Text>
                )}
              </div>
            )
          }}
          required
        />

        <Select
          label="Project"
          placeholder="Select a project / git repository"
          data={projectOptions}
          value={projectId}
          onChange={setProjectId}
          disabled={createWorkflow.isPending}
          searchable
          nothingFoundMessage={projectsLoading ? 'Loading...' : 'No projects found'}
          renderOption={({ option }) => {
            const proj = projectOptions.find((p) => p.value === option.value)
            return (
              <div>
                <Text size="sm" fw={500}>{option.label}</Text>
                {proj?.description && (
                  <Text size="xs" c="dimmed">{proj.description}</Text>
                )}
              </div>
            )
          }}
          required
        />

        <Textarea
          label="Initial Context"
          placeholder="Describe what you want to accomplish, paste ticket details, or provide any relevant context..."
          value={initialContext}
          onChange={(e) => setInitialContext(e.currentTarget.value)}
          disabled={createWorkflow.isPending}
          minRows={4}
          maxRows={8}
          autosize
        />

        <Autocomplete
          label="Feature Name"
          placeholder={branchesLoading ? 'Loading branches...' : 'e.g. user-authentication, billing-v2'}
          description="Optional — link to a feature branch or name this feature to track stage completion."
          value={featureName}
          onChange={setFeatureName}
          data={branches ? branches.map((b) => b.name) : []}
          disabled={createWorkflow.isPending}
          limit={20}
        />

        {/* Stage selection for selectable templates */}
        {templateId && selectedTemplate?.selectableStages && (
          <Stack gap="xs">
            <Text size="sm" fw={500}>
              Stages
            </Text>
            {stagesLoading ? (
              <Group gap="xs">
                <Loader size="xs" />
                <Text size="sm" c="dimmed">Loading stages...</Text>
              </Group>
            ) : templateStages && templateStages.length > 0 ? (
              <Stack gap="xs">
                {templateStages.map((stage) => {
                  const isCompleted = completedStages.includes(stage.name)
                  const isChecked = !isCompleted && effectiveSelected.includes(stage.name)
                  return (
                    <Group key={stage.name} gap="sm" align="center">
                      <Checkbox
                        checked={isChecked}
                        disabled={isCompleted || createWorkflow.isPending}
                        onChange={(e) => handleStageToggle(stage.name, e.currentTarget.checked)}
                        label={
                          <Group gap="xs" align="center">
                            <Text size="sm">{stage.name}</Text>
                            {isCompleted && (
                              <>
                                <TbCheck size={14} color="var(--mantine-color-green-6)" />
                                <Badge size="xs" color="green" variant="light">
                                  completed
                                </Badge>
                              </>
                            )}
                          </Group>
                        }
                      />
                    </Group>
                  )
                })}
              </Stack>
            ) : (
              <Text size="sm" c="dimmed">No stages defined for this template.</Text>
            )}
          </Stack>
        )}

        {templateId && templateRoutings && templateRoutings.length > 0 && (
          <Stack gap={0}>
            <Button
              variant="subtle"
              size="xs"
              leftSection={
                overridesOpen ? <TbChevronDown size={14} /> : <TbChevronRight size={14} />
              }
              onClick={() => setOverridesOpen((v) => !v)}
              justify="flex-start"
              px={0}
              color="dimmed"
            >
              Override stage models
            </Button>
            <Collapse in={overridesOpen}>
              <Table fz="sm" mt="xs">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Stage</Table.Th>
                    <Table.Th>Default Model</Table.Th>
                    <Table.Th>Provider</Table.Th>
                    <Table.Th>Override Model</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {templateRoutings.map((routing) => {
                    const provider = (providers ?? []).find((p) => p.id === routing.providerId)
                    const override = stageModelOverrides[routing.stageName] ?? ''
                    return (
                      <Table.Tr key={routing.id}>
                        <Table.Td fw={500}>{routing.stageName}</Table.Td>
                        <Table.Td>
                          <Text size="sm" c="dimmed">{routing.modelName}</Text>
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" c="dimmed">{provider?.name ?? '—'}</Text>
                        </Table.Td>
                        <Table.Td>
                          <Group gap="xs">
                            <TextInput
                              size="xs"
                              placeholder={routing.modelName}
                              value={override}
                              onChange={(e) =>
                                handleOverrideChange(routing.stageName, e.currentTarget.value)
                              }
                              style={{ flex: 1 }}
                            />
                            {override && (
                              <Tooltip label="Reset to default">
                                <ActionIcon
                                  size="sm"
                                  variant="subtle"
                                  color="gray"
                                  onClick={() => handleResetOverride(routing.stageName)}
                                >
                                  <TbRefresh size={14} />
                                </ActionIcon>
                              </Tooltip>
                            )}
                          </Group>
                        </Table.Td>
                      </Table.Tr>
                    )
                  })}
                </Table.Tbody>
              </Table>
            </Collapse>
          </Stack>
        )}

        {createWorkflow.isError && (
          <Alert color="red" title="Failed to create workflow" variant="light">
            {formatApiError(createWorkflow.error)}
          </Alert>
        )}

        <Button
          onClick={handleCreate}
          disabled={!canCreate}
          loading={createWorkflow.isPending}
          fullWidth
        >
          Create
        </Button>
      </Stack>
    </Modal>
  )
}
