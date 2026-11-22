import { Alert, Button, Group, Select, Text, Tooltip } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { TbFlask } from 'react-icons/tb'
import { setReviewApiOverride } from '../../../api/reviewEngine'
import { FAKE_VIEWERS, useFakeViewerStore } from '../fake/fakeViewer'
import { currentReviewApiMode, fakeReviewApi, switchFakeViewer } from '../reviewApi'

/**
 * Shown only while the review pages run on the in-memory fake (asked for by localStorage
 * `antiphon.reviewApi=fake`, `VITE_REVIEW_API=fake`, or a story/test): says so, lets the operator
 * act as another person (author vs reviewer views), reset the data, or go back to the HTTP API.
 * "Viewing as" drives the fake's viewer (`fakeViewer.ts`); the pages then read it back from
 * `GET /api/review-me` like they do in HTTP mode, so every viewer-dependent query is refetched.
 */
export function FakeApiBanner() {
  const queryClient = useQueryClient()
  const login = useFakeViewerStore((s) => s.login)
  if (currentReviewApiMode() !== 'fake') return null
  return (
    <Alert color="grape" variant="light" p={6} icon={<TbFlask size={16} />}>
      <Group gap="sm" justify="space-between" wrap="wrap">
        <Text size="xs">Fake review API: in-memory data that resets on reload. The server is not involved.</Text>
        <Group gap={6} wrap="nowrap">
          <Text size="xs" c="dimmed">
            Viewing as
          </Text>
          <Select
            size="xs"
            w={150}
            aria-label="Viewing as"
            data={FAKE_VIEWERS.map((v) => ({ value: v.login, label: v.displayName }))}
            value={login}
            allowDeselect={false}
            onChange={(next) => {
              if (next && next !== login) switchFakeViewer(queryClient, next)
            }}
          />
          <Button size="compact-xs" variant="subtle" color="grape" onClick={() => fakeReviewApi.reset()}>
            Reset data
          </Button>
          <Button
            size="compact-xs"
            variant="subtle"
            color="gray"
            onClick={() => {
              if (setReviewApiOverride('http')) window.location.reload()
            }}
          >
            Use HTTP API
          </Button>
        </Group>
      </Group>
    </Alert>
  )
}

/** Dev builds on the HTTP API only: a small switch to the fake data (sets localStorage and reloads). */
export function FakeDataSwitch() {
  if (!import.meta.env.DEV || currentReviewApiMode() !== 'http') return null
  return (
    <Tooltip label="Dev only: switch the review pages to the in-memory fake (localStorage antiphon.reviewApi=fake)">
      <Button
        size="xs"
        variant="subtle"
        color="gray"
        leftSection={<TbFlask size={14} />}
        onClick={() => {
          if (setReviewApiOverride('fake')) window.location.reload()
        }}
      >
        Fake data
      </Button>
    </Tooltip>
  )
}
