// Copy-selection handling for the xterm session terminal. Lives outside SessionTerminal.tsx so
// the component file only exports components (react-refresh); the tests import from here.

export interface TerminalSelectionApi {
  hasSelection: () => boolean
  getSelection: () => string
}

async function writeClipboardText(text: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }

  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', 'true')
  textarea.style.position = 'fixed'
  textarea.style.left = '-9999px'
  document.body.append(textarea)
  textarea.select()
  document.execCommand('copy')
  textarea.remove()
}

export function createTerminalCopyKeyHandler(terminal: TerminalSelectionApi) {
  return (event: KeyboardEvent) => {
    const isCopyKey = (event.ctrlKey || event.metaKey)
      && !event.altKey
      && !event.shiftKey
      && (event.code === 'KeyC' || event.key.toLowerCase() === 'c')

    if (event.type !== 'keydown' || !isCopyKey || !terminal.hasSelection()) {
      return true
    }

    const selection = terminal.getSelection()
    if (!selection) {
      return true
    }

    void writeClipboardText(selection)
    return true
  }
}
