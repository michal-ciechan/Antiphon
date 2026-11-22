import { Button, Group, Modal, NumberInput, SegmentedControl, Stack, Text, TextInput } from '@mantine/core'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { getApiFieldErrors } from '../../../api/client'
import type { CreateReviewRunRequest, ReviewHost } from '../../../api/reviewEngine'
import { useCreateReviewRun } from '../useReview'

/** The server's validation keys (`repo`, `prNumber`, `baseSha`) matched without regard to case. */
function fieldError(fields: Record<string, string>, name: string): string | undefined {
  return Object.entries(fields).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]
}

function IntakeForm({ onDone }: { onDone: () => void }) {
  const navigate = useNavigate()
  const create = useCreateReviewRun()
  const [host, setHost] = useState<ReviewHost>('GitHub')
  const [repo, setRepo] = useState('')
  const [prNumber, setPrNumber] = useState<string | number>('')
  const [baseRef, setBaseRef] = useState('')
  const [headRef, setHeadRef] = useState('HEAD')
  const [title, setTitle] = useState('')
  const fields = getApiFieldErrors(create.error)

  const pr = typeof prNumber === 'number' ? prNumber : Number.parseInt(prNumber, 10)
  const ready = repo.trim() !== '' && (host === 'GitHub' ? Number.isInteger(pr) && pr > 0 : baseRef.trim() !== '')
  const request: CreateReviewRunRequest = {
    host,
    repo: repo.trim(),
    ...(host === 'GitHub' ? { prNumber: pr } : { baseSha: baseRef.trim(), ...(headRef.trim() ? { headSha: headRef.trim() } : {}) }),
    ...(title.trim() ? { title: title.trim() } : {}),
  }

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        if (!ready) return
        create.mutate(request, {
          onSuccess: (run) => {
            onDone()
            navigate(`/reviews/${run.id}`)
          },
        })
      }}
    >
      <Stack gap="sm">
        <SegmentedControl
          size="xs"
          value={host}
          onChange={(v) => setHost(v as ReviewHost)}
          data={[
            { value: 'GitHub', label: 'GitHub PR' },
            { value: 'Local', label: 'Local git' },
          ]}
        />
        <TextInput
          size="xs"
          label="Repository"
          placeholder="owner/name"
          description={host === 'Local' ? 'A repo configured under Review:CodeHosts:Repos with its local path.' : undefined}
          required
          data-autofocus
          value={repo}
          onChange={(e) => setRepo(e.currentTarget.value)}
          error={fieldError(fields, 'repo')}
        />
        {host === 'GitHub' ? (
          <NumberInput
            size="xs"
            label="PR number"
            placeholder="612"
            required
            min={1}
            allowDecimal={false}
            allowNegative={false}
            hideControls
            value={prNumber}
            onChange={setPrNumber}
            error={fieldError(fields, 'prNumber')}
          />
        ) : (
          <Group grow align="flex-start">
            <TextInput
              size="xs"
              label="Base ref"
              placeholder="main or a SHA"
              required
              value={baseRef}
              onChange={(e) => setBaseRef(e.currentTarget.value)}
              error={fieldError(fields, 'baseSha')}
            />
            <TextInput
              size="xs"
              label="Head ref"
              placeholder="HEAD"
              value={headRef}
              onChange={(e) => setHeadRef(e.currentTarget.value)}
              error={fieldError(fields, 'headSha')}
            />
          </Group>
        )}
        <TextInput
          size="xs"
          label="Title"
          placeholder={host === 'GitHub' ? "Defaults to the PR's title" : "Defaults to the head commit's subject"}
          value={title}
          onChange={(e) => setTitle(e.currentTarget.value)}
        />
        <Text size="xs" c="dimmed">
          Antiphon reads the change through the code host, then runs the default workflow. Refresh on the run picks up new pushes.
        </Text>
        <Group justify="flex-end" gap="xs">
          <Button size="xs" variant="subtle" color="gray" onClick={onDone}>
            Cancel
          </Button>
          <Button size="xs" type="submit" disabled={!ready} loading={create.isPending}>
            Start review
          </Button>
        </Group>
      </Stack>
    </form>
  )
}

/** Manual intake (`POST /api/review-runs`): GitHub needs a PR number; Local needs a base ref and defaults head to HEAD. */
export function NewReviewModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  return (
    <Modal opened={opened} onClose={onClose} title={<Text fw={700}>New review</Text>} size="md">
      {/* mounted only while open, so each opening starts from a clean form */}
      {opened && <IntakeForm onDone={onClose} />}
    </Modal>
  )
}
