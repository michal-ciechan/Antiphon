import { useEditor, EditorContent } from '@tiptap/react'
import { StarterKit } from '@tiptap/starter-kit'
import { Markdown } from 'tiptap-markdown'
import { useEffect } from 'react'
import { Box } from '@mantine/core'
import './tiptap.css'

interface TipTapEditorProps {
  content: string
  editable?: boolean
  onChange?: (markdown: string) => void
}

/**
 * TipTap-based markdown editor/viewer.
 * Parses and renders markdown content using tiptap-markdown.
 * Set editable=false for read-only viewing, editable=true for editing.
 */
export function TipTapEditor({ content, editable = false, onChange }: TipTapEditorProps) {
  const editor = useEditor({
    extensions: [
      StarterKit,
      Markdown.configure({
        html: false,
        transformCopiedText: true,
        transformPastedText: true,
      }),
    ],
    content,
    editable,
    onUpdate: ({ editor: e }) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      onChange?.((e.storage as any).markdown.getMarkdown())
    },
  })

  // Sync content when it changes externally
  useEffect(() => {
    if (!editor || editor.isDestroyed) return
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const current = (editor.storage as any).markdown.getMarkdown()
    if (current !== content) {
      editor.commands.setContent(content)
    }
  }, [editor, content])

  // Sync editable state
  useEffect(() => {
    if (!editor || editor.isDestroyed) return
    editor.setEditable(editable)
  }, [editor, editable])

  return (
    <Box
      style={{
        // Editor styling — mirrors ArtifactViewer prose styles
        lineHeight: 1.7,
        fontSize: '0.95rem',
        color: 'var(--mantine-color-text)',
        flex: 1,
        overflow: 'auto',
        cursor: editable ? 'text' : 'default',
      }}
      className="tiptap-editor-wrapper"
    >
      <EditorContent editor={editor} />
    </Box>
  )
}
