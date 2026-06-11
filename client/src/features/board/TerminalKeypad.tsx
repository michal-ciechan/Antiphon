import { Button, Group, Paper } from '@mantine/core'
import {
  TbArrowBackUp,
  TbArrowDown,
  TbArrowLeft,
  TbArrowRight,
  TbArrowUp,
  TbCornerDownLeft,
} from 'react-icons/tb'

// Raw key sequences sent straight to the PTY (xterm-compatible). Mobile soft keyboards
// can't produce arrows/esc/tab/ctrl, so these buttons stand in for them.
const KEY = {
  esc: '\x1b',
  tab: '\t',
  shiftTab: '\x1b[Z',
  ctrlC: '\x03',
  up: '\x1b[A',
  down: '\x1b[B',
  left: '\x1b[D',
  right: '\x1b[C',
  enter: '\r',
  backspace: '\x7f',
  slash: '/',
  question: '?',
} as const

interface TerminalKeypadProps {
  onKey: (sequence: string) => void
}

/**
 * On-screen key buttons for driving a TUI (e.g. Claude Code) from a touch device where the
 * special keys don't exist on the soft keyboard. Each button sends its escape sequence to the
 * PTY directly, so it works without the xterm textarea being focused.
 */
export function TerminalKeypad({ onKey }: TerminalKeypadProps) {
  const key = (label: React.ReactNode, sequence: string, ariaLabel: string) => (
    <Button
      variant="default"
      size="sm"
      px={10}
      h={40}
      miw={44}
      aria-label={ariaLabel}
      onClick={() => onKey(sequence)}
      style={{ touchAction: 'manipulation', flex: '0 0 auto' }}
    >
      {label}
    </Button>
  )

  return (
    <Paper withBorder p={6} bg="dark.6" data-testid="terminal-keypad">
      <Group gap={6} wrap="wrap" justify="center">
        {key('Esc', KEY.esc, 'Escape')}
        {key('Tab', KEY.tab, 'Tab')}
        {key('⇧Tab', KEY.shiftTab, 'Shift Tab')}
        {key('/', KEY.slash, 'Slash')}
        {key('?', KEY.question, 'Question mark')}
        {key('^C', KEY.ctrlC, 'Control C')}
        {key(<TbArrowLeft size={18} />, KEY.left, 'Arrow left')}
        {key(<TbArrowUp size={18} />, KEY.up, 'Arrow up')}
        {key(<TbArrowDown size={18} />, KEY.down, 'Arrow down')}
        {key(<TbArrowRight size={18} />, KEY.right, 'Arrow right')}
        {key(<TbArrowBackUp size={18} />, KEY.backspace, 'Backspace')}
        {key(<TbCornerDownLeft size={18} />, KEY.enter, 'Enter')}
      </Group>
    </Paper>
  )
}
