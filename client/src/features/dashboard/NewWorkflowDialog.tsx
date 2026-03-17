import { useState } from 'react'
import { Modal, Select, Textarea, Button, Alert, Stack, Text } from '@mantine/core'
import { useNavigate } from 'react-router'
import { useTemplates, type WorkflowTemplateDto } from '../../api/settings'
import { useProjects, type ProjectDto } from '../../api/projects'
import { useCreateWorkflow } from '../../api/workflows'
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

export function NewWorkflowDialog({ opened, onClose }: NewWorkflowDialogProps) {
  const navigate = useNavigate()
  const { data: templates, isLoading: templatesLoading } = useTemplates()
  const { data: projects, isLoading: projectsLoading } = useProjects()
  const createWorkflow = useCreateWorkflow()

  const [templateId, setTemplateId] = useState<string | null>(null)
  const [projectId, setProjectId] = useState<string | null>(null)
  const [initialContext, setInitialContext] = useState('')

  function handleClose() {
    if (createWorkflow.isPending) return
    setTemplateId(null)
    setProjectId(null)
    setInitialContext('')
    createWorkflow.reset()
    onClose()
  }

  async function handleCreate() {
    if (!templateId || !projectId) return

    try {
      const result = await createWorkflow.mutateAsync({
        templateId,
        projectId,
        initialContext: initialContext.trim() || undefined,
      })
      handleClose()
      navigate(`/workflow/${result.id}`)
    } catch {
      // Error is captured in createWorkflow.error
    }
  }

  const templateOptions = (templates ?? []).map((t: WorkflowTemplateDto) => ({
    value: t.id,
    label: t.name,
    description: t.description,
  }))

  const projectOptions = (projects ?? []).map((p: ProjectDto) => ({
    value: p.id,
    label: p.name,
    description: p.gitRepositoryUrl,
  }))

  const canCreate = !!templateId && !!projectId && !createWorkflow.isPending

  return (
    <Modal
      opened={opened}
      onClose={handleClose}
      title="New Workflow"
      size="md"
      closeOnClickOutside={!createWorkflow.isPending}
      closeOnEscape={!createWorkflow.isPending}
    >
      <Stack gap="md">
        <Select
          label="Workflow Template"
          placeholder="Select a template"
          data={templateOptions}
          value={templateId}
          onChange={setTemplateId}
          disabled={createWorkflow.isPending}
          searchable
          nothingFoundMessage={templatesLoading ? 'Loading...' : 'No templates found'}
          renderOption={({ option }) => {
            const tpl = templateOptions.find((t) => t.value === option.value)
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
