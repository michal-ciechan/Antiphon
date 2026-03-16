import { createTheme, type MantineColorsTuple } from '@mantine/core'

// Status colors aligned with UX design spec:
// green (success/approved), orange (pending/warning), red (error/rejected), blue (active/primary)

const success: MantineColorsTuple = [
  '#e6fcf0',
  '#d0f4df',
  '#a3e8bf',
  '#72db9c',
  '#4ad07e',
  '#33c96b',
  '#238551',
  '#1c7a49',
  '#156c3e',
  '#0c5e33',
]

const warning: MantineColorsTuple = [
  '#fff4e0',
  '#ffe9c8',
  '#ffd494',
  '#ffbd5c',
  '#ffa92e',
  '#f59c13',
  '#c87619',
  '#a86214',
  '#8a4f0f',
  '#6c3d0a',
]

const danger: MantineColorsTuple = [
  '#ffe5e5',
  '#fccbcb',
  '#f49898',
  '#ee6262',
  '#e93636',
  '#e61a1a',
  '#cd4246',
  '#b12b2b',
  '#9e2323',
  '#8a1a1a',
]

const active: MantineColorsTuple = [
  '#e0f0ff',
  '#c8dfff',
  '#94bdff',
  '#5c99ff',
  '#3b7bf5',
  '#2d72d2',
  '#2563b8',
  '#1d54a0',
  '#164688',
  '#0f3870',
]

export const theme = createTheme({
  primaryColor: 'active',
  colors: {
    success,
    warning,
    danger,
    active,
  },
  other: {
    statusColors: {
      success: 'success',
      pending: 'warning',
      error: 'danger',
      active: 'active',
    },
  },
})
