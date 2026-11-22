import { ActionIcon, Box, Group, Paper, Text } from '@mantine/core'
import { useRef, useState, type PointerEvent, type ReactNode } from 'react'
import { TbGripHorizontal, TbX } from 'react-icons/tb'

type Anchor = 'top-right' | 'bottom-left'

/**
 * A small window floating over the fullscreen graph. Drag it by its title bar; it keeps its place
 * while you click other nodes. It sits beside the React Flow canvas, not inside it, so scrolling
 * its body never zooms the graph.
 */
export function FloatingWindow({
  title,
  badge,
  onClose,
  children,
  width = 400,
  anchor = 'top-right',
  testId,
}: {
  title: string
  badge?: ReactNode
  onClose: () => void
  children: ReactNode
  width?: number
  anchor?: Anchor
  testId?: string
}) {
  const [offset, setOffset] = useState({ x: 0, y: 0 })
  const drag = useRef<{ sx: number; sy: number; ox: number; oy: number } | null>(null)

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if ((e.target as HTMLElement).closest('button')) return
    drag.current = { sx: e.clientX, sy: e.clientY, ox: offset.x, oy: offset.y }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const d = drag.current
    if (d) setOffset({ x: d.ox + e.clientX - d.sx, y: d.oy + e.clientY - d.sy })
  }
  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    drag.current = null
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId)
  }

  const place = anchor === 'top-right' ? { top: 12, right: 12 } : { bottom: 12, left: 12 }
  return (
    <Paper
      withBorder
      shadow="xl"
      radius="md"
      data-testid={testId}
      style={{
        position: 'absolute',
        ...place,
        zIndex: 10,
        width: `min(${width}px, calc(100% - 24px))`,
        maxHeight: 'calc(100% - 24px)',
        display: 'flex',
        flexDirection: 'column',
        transform: `translate(${offset.x}px, ${offset.y}px)`,
        background: 'var(--mantine-color-dark-7)',
      }}
    >
      <Group
        justify="space-between"
        wrap="nowrap"
        px="xs"
        py={6}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        style={{ cursor: 'grab', userSelect: 'none', touchAction: 'none', borderBottom: '1px solid var(--mantine-color-dark-4)' }}
      >
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          <TbGripHorizontal size={14} style={{ opacity: 0.5, flexShrink: 0 }} />
          <Text size="sm" fw={600} ff="monospace" truncate>
            {title}
          </Text>
          {badge}
        </Group>
        <ActionIcon size="sm" variant="subtle" color="gray" onClick={onClose} aria-label={`Close ${title}`}>
          <TbX size={14} />
        </ActionIcon>
      </Group>
      <Box p="sm" style={{ overflowY: 'auto', minHeight: 0 }}>
        {children}
      </Box>
    </Paper>
  )
}
