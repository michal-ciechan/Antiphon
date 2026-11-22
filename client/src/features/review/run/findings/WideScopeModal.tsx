import { Alert, Badge, Button, Code, Group, Modal, Stack, Table, Text, Textarea } from '@mantine/core'
import { useState, type ReactNode } from 'react'
import { TbInfoCircle } from 'react-icons/tb'
import type { DismissScope, ReasonCategory, ReviewFindingDto, ReviewSubjectDto } from '../../../../api/reviewEngine'
import { REASON_META } from '../../reviewMeta'
import { ruleFor } from './drafts'

function Row({ k, v }: { k: string; v: ReactNode }) {
  return (
    <Table.Tr>
      <Table.Td w={130} c="dimmed" style={{ verticalAlign: 'top' }}>
        {k}
      </Table.Td>
      <Table.Td>{v}</Table.Td>
    </Table.Tr>
  )
}

/**
 * The confirmation every path/repo/team dismissal goes through (design §11.7, D-34): the target
 * layer, the exact rule text, the file it changes, and an optional steer for the agent that drafts
 * the rule. UI only in this slice: confirming marks the draft confirmed; no learn PR is opened.
 */
export function WideScopeModal({
  opened,
  finding,
  subject,
  reason,
  scope,
  text,
  actorName,
  initialSteer,
  onClose,
  onConfirm,
}: {
  opened: boolean
  finding: ReviewFindingDto
  subject: ReviewSubjectDto
  reason: ReasonCategory
  scope: DismissScope
  text: string
  actorName: string
  initialSteer?: string
  onClose: () => void
  onConfirm: (steer: string) => void
}) {
  const [steer, setSteer] = useState(initialSteer ?? '')
  const rule = ruleFor(finding, subject, reason, scope, text, actorName, steer)
  const team = scope === 'Team'
  return (
    <Modal opened={opened} onClose={onClose} size={720} title={<Text fw={700}>Dismiss {finding.key} at {scope.toLowerCase()} scope: confirm what changes</Text>}>
      <Stack gap="sm">
        <Table fz="xs" verticalSpacing={3} withRowBorders={false}>
          <Table.Tbody>
            <Row k="Finding" v={`${finding.key} · ${finding.checkId ?? 'unprompted'} · ${finding.title}`} />
            <Row k="Target" v={rule.target} />
            <Row k="Rule" v={`${rule.kind} · reason: ${REASON_META[reason].label.toLowerCase()}`} />
            <Row k="File it changes" v={<Code fz={11}>{rule.file}</Code>} />
            {scope === 'Path' && <Row k="Paths" v={<Code fz={11}>{rule.dir}</Code>} />}
            {team && (
              <Row
                k="Team scope"
                v={
                  <Group gap={6} wrap="wrap">
                    <Badge color="grape" variant="light" tt="none">
                      queued until 2 repos
                    </Badge>
                    <Text size="xs" c="dimmed">
                      The PR to the {rule.team} layer opens when a second repo dismisses this check the same way.
                    </Text>
                  </Group>
                }
              />
            )}
          </Table.Tbody>
        </Table>
        <div>
          <Text size="xs" c="dimmed" mb={2}>
            Rule text ({rule.kind})
          </Text>
          <Code block fz={11} style={{ whiteSpace: 'pre-wrap' }}>
            {rule.lines.join('\n')}
          </Code>
        </div>
        <Textarea
          size="xs"
          autosize
          minRows={2}
          label="Steer the agent that drafts the rule (optional)"
          placeholder="e.g. only until the TimeProvider migration lands; keep flagging new files"
          value={steer}
          onChange={(e) => setSteer(e.currentTarget.value)}
        />
        <Alert color="gray" variant="light" p={6} icon={<TbInfoCircle size={14} />}>
          <Text size="xs">
            Preview only in this slice: confirming keeps the {scope.toLowerCase()} scope on your reply. The learn PR (and the two-way links
            with this thread) arrive with the learn API.
          </Text>
        </Alert>
        <Group justify="flex-end" gap={6}>
          <Button size="xs" variant="subtle" color="gray" onClick={onClose}>
            Cancel
          </Button>
          <Button size="xs" color="cyan" onClick={() => onConfirm(steer)}>
            Confirm {scope.toLowerCase()}-scope dismissal
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
