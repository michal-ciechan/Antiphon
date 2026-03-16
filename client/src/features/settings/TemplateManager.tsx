import { useState } from 'react'
import {
  Button,
  Group,
  Modal,
  Paper,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
  Badge,
  ActionIcon,
  Tooltip,
  Alert,
  Loader,
} from '@mantine/core'
import { TbPlus, TbEdit, TbTrash, TbAlertCircle } from 'react-icons/tb'
import {
  useTemplates,
  useCreateTemplate,
  useUpdateTemplate,
  useDeleteTemplate,
  type WorkflowTemplateDto,
} from '../../api/settings'

export function TemplateManager() {
  const { data: templates, isLoading, error } = useTemplates()
  const createMutation = useCreateTemplate()
  const updateMutation = useUpdateTemplate()
  const deleteMutation = useDeleteTemplate()

  const [editModalOpen, setEditModalOpen] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [editingTemplate, setEditingTemplate] = useState<WorkflowTemplateDto | null>(null)
  const [deletingTemplate, setDeletingTemplate] = useState<WorkflowTemplateDto | null>(null)

  const [formName, setFormName] = useState('')
  const [formDescription, setFormDescription] = useState('')
  const [formYaml, setFormYaml] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const openCreateModal = () => {
    setEditingTemplate(null)
    setFormName('')
    setFormDescription('')
    setFormYaml('')
    setFormError(null)
    setEditModalOpen(true)
  }

  const openEditModal = (template: WorkflowTemplateDto) => {
    setEditingTemplate(template)
    setFormName(template.name)
    setFormDescription(template.description)
    setFormYaml(template.yamlDefinition)
    setFormError(null)
    setEditModalOpen(true)
  }

  const openDeleteModal = (template: WorkflowTemplateDto) => {
    setDeletingTemplate(template)
    setDeleteModalOpen(true)
  }

  const handleSave = async () => {
    setFormError(null)
    const data = {
      name: formName,
      description: formDescription,
      yamlDefinition: formYaml,
    }

    try {
      if (editingTemplate) {
        await updateMutation.mutateAsync({ id: editingTemplate.id, data })
      } else {
        await createMutation.mutateAsync(data)
      }
      setEditModalOpen(false)
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'body' in err) {
        const apiErr = err as { body: unknown }
        if (apiErr.body && typeof apiErr.body === 'object' && 'errors' in apiErr.body) {
          const errors = (apiErr.body as { errors: Record<string, string[]> }).errors
          const messages = Object.values(errors).flat().join('. ')
          setFormError(messages)
          return
        }
        if (apiErr.body && typeof apiErr.body === 'object' && 'detail' in apiErr.body) {
          setFormError((apiErr.body as { detail: string }).detail)
          return
        }
      }
      setFormError('An unexpected error occurred.')
    }
  }

  const handleDelete = async () => {
    if (!deletingTemplate) return
    try {
      await deleteMutation.mutateAsync(deletingTemplate.id)
      setDeleteModalOpen(false)
      setDeletingTemplate(null)
    } catch {
      setDeleteModalOpen(false)
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  if (isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (error) {
    return (
      <Alert color="red" icon={<TbAlertCircle />} title="Error loading templates">
        {error.message}
      </Alert>
    )
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          Manage workflow templates that define stage progression, executor types, and gate configuration.
        </Text>
        <Button leftSection={<TbPlus />} onClick={openCreateModal}>
          Add Template
        </Button>
      </Group>

      {templates && templates.length > 0 ? (
        <Paper withBorder>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Description</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th w={100}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {templates.map((template) => (
                <Table.Tr key={template.id}>
                  <Table.Td fw={500}>{template.name}</Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed" lineClamp={2}>
                      {template.description}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {template.isBuiltIn ? (
                      <Badge variant="light" color="blue" size="sm">
                        Built-in
                      </Badge>
                    ) : (
                      <Badge variant="light" color="gray" size="sm">
                        Custom
                      </Badge>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Tooltip label="Edit">
                        <ActionIcon
                          variant="subtle"
                          onClick={() => openEditModal(template)}
                        >
                          <TbEdit />
                        </ActionIcon>
                      </Tooltip>
                      {!template.isBuiltIn && (
                        <Tooltip label="Delete">
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            onClick={() => openDeleteModal(template)}
                          >
                            <TbTrash />
                          </ActionIcon>
                        </Tooltip>
                      )}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      ) : (
        <Paper withBorder p="xl">
          <Text ta="center" c="dimmed">
            No templates found. Click "Add Template" to create one.
          </Text>
        </Paper>
      )}

      {/* Create / Edit Modal */}
      <Modal
        opened={editModalOpen}
        onClose={() => setEditModalOpen(false)}
        title={editingTemplate ? 'Edit Template' : 'Add Template'}
        size="lg"
      >
        <Stack>
          {formError && (
            <Alert color="red" icon={<TbAlertCircle />}>
              {formError}
            </Alert>
          )}
          <TextInput
            label="Name"
            placeholder="My Workflow Template"
            required
            value={formName}
            onChange={(e) => setFormName(e.currentTarget.value)}
          />
          <Textarea
            label="Description"
            placeholder="Describe what this workflow template does..."
            rows={2}
            value={formDescription}
            onChange={(e) => setFormDescription(e.currentTarget.value)}
          />
          <Textarea
            label="YAML Definition"
            placeholder={`stages:\n  - name: my-stage\n    executorType: ai-agent\n    modelRouting: opus`}
            required
            rows={14}
            autosize
            minRows={10}
            maxRows={24}
            styles={{ input: { fontFamily: 'monospace', fontSize: '13px' } }}
            value={formYaml}
            onChange={(e) => setFormYaml(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setEditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} loading={isSaving}>
              {editingTemplate ? 'Save Changes' : 'Create Template'}
            </Button>
          </Group>
        </Stack>
      </Modal>

      {/* Delete Confirmation Modal (UX-DR25) */}
      <Modal
        opened={deleteModalOpen}
        onClose={() => setDeleteModalOpen(false)}
        title="Delete Template"
        size="sm"
      >
        <Stack>
          <Text>
            Are you sure you want to delete the template "{deletingTemplate?.name}"? This action
            cannot be undone.
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setDeleteModalOpen(false)}>
              Cancel
            </Button>
            <Button color="red" onClick={handleDelete} loading={deleteMutation.isPending}>
              Delete
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
