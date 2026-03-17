import { useEffect, useRef, useState } from 'react'
import { Box, Loader, Text } from '@mantine/core'

interface MermaidDiagramProps {
  /** The raw Mermaid diagram source code */
  chart: string
}

/**
 * Renders a Mermaid diagram from source text.
 * The mermaid library (~200KB) is lazy-loaded via dynamic import
 * so it only loads when a diagram is actually present (UX-DR17).
 */
export default function MermaidDiagram({ chart }: MermaidDiagramProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    async function renderDiagram() {
      try {
        const mermaid = (await import('mermaid')).default
        mermaid.initialize({
          startOnLoad: false,
          theme: 'dark',
          securityLevel: 'strict',
        })

        if (cancelled || !containerRef.current) return

        const id = `mermaid-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`
        const { svg } = await mermaid.render(id, chart)

        if (cancelled || !containerRef.current) return

        containerRef.current.innerHTML = svg
        setLoading(false)
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to render diagram')
          setLoading(false)
        }
      }
    }

    renderDiagram()

    return () => {
      cancelled = true
    }
  }, [chart])

  if (error) {
    return (
      <Box
        style={{
          padding: 'var(--mantine-spacing-sm)',
          border: '1px solid var(--mantine-color-red-7)',
          borderRadius: 'var(--mantine-radius-sm)',
          backgroundColor: 'var(--mantine-color-dark-7)',
        }}
      >
        <Text size="sm" c="red">
          Mermaid diagram error: {error}
        </Text>
        <Box
          component="pre"
          style={{
            fontSize: '0.8rem',
            marginTop: 'var(--mantine-spacing-xs)',
            whiteSpace: 'pre-wrap',
            color: 'var(--mantine-color-dimmed)',
          }}
        >
          {chart}
        </Box>
      </Box>
    )
  }

  return (
    <Box style={{ position: 'relative', minHeight: 60 }}>
      {loading && (
        <Box
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: 'var(--mantine-spacing-md)',
          }}
        >
          <Loader size="sm" />
        </Box>
      )}
      <Box
        ref={containerRef}
        style={{
          display: loading ? 'none' : 'flex',
          justifyContent: 'center',
          overflow: 'auto',
        }}
      />
    </Box>
  )
}
