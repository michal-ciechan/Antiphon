import { Box, Group, Stack, Text, Tooltip } from '@mantine/core'
import type { BlastRadiusDto } from '../../../api/reviewEngine'
import { BLAST_DIMS, blastColor } from '../reviewMeta'

function dimText(blast: BlastRadiusDto, key: (typeof BLAST_DIMS)[number]['key']): string {
  const d = BLAST_DIMS.find((x) => x.key === key)!
  const v = blast[key]
  return `${v} · ${d.levels[v - 1] ?? '?'}`
}

/** Six tiny bars: reach, contract, data, rollback, reversal, exposure. */
export function BlastMini({ blast, height = 18 }: { blast: BlastRadiusDto | null; height?: number }) {
  if (!blast)
    return (
      <Text size="xs" c="dimmed">
        pending
      </Text>
    )
  return (
    <Tooltip
      withArrow
      label={
        <Stack gap={2}>
          {BLAST_DIMS.map((d) => (
            <Group key={d.key} gap={6} justify="space-between" wrap="nowrap">
              <Text size="xs">{d.label}</Text>
              <Text size="xs" fw={600}>
                {dimText(blast, d.key)}
              </Text>
            </Group>
          ))}
        </Stack>
      }
    >
      <Group gap={2} wrap="nowrap" align="flex-end" h={height} aria-label="Blast radius">
        {BLAST_DIMS.map((d) => {
          const v = blast[d.key]
          return <Box key={d.key} w={5} h={Math.max(2, Math.round(((v - 1) / 4) * height))} style={{ background: blastColor(v), borderRadius: 1 }} />
        })}
      </Group>
    </Tooltip>
  )
}

/** Labelled bars, one row per dimension. */
export function BlastPanel({ blast }: { blast: BlastRadiusDto }) {
  return (
    <Stack gap={4}>
      {BLAST_DIMS.map((d) => {
        const v = blast[d.key]
        return (
          <Box key={d.key}>
            <Group justify="space-between" gap={4} wrap="nowrap">
              <Text size="xs" c="dimmed">
                {d.label}
              </Text>
              <Text size="xs" fw={600}>
                {dimText(blast, d.key)}
              </Text>
            </Group>
            <Box h={4} bg="dark.5" style={{ borderRadius: 2 }}>
              <Box h={4} w={`${Math.max(4, (v / 5) * 100)}%`} style={{ background: blastColor(v), borderRadius: 2 }} />
            </Box>
          </Box>
        )
      })}
    </Stack>
  )
}
