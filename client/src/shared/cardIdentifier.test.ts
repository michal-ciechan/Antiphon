import { describe, expect, it } from 'vitest'
import {
  cardNumber,
  displayIdentifier,
  identifierQueryDigits,
  matchesIdentifierQuery,
} from './cardIdentifier'

describe('displayIdentifier', () => {
  it('renders a stored CARD identifier as its short form', () => {
    expect(displayIdentifier('CARD-0041')).toBe('#41')
    expect(displayIdentifier('CARD-0001')).toBe('#1')
    expect(displayIdentifier('CARD-1234')).toBe('#1234')
    expect(displayIdentifier('CARD-12345')).toBe('#12345')
  })

  it('is case insensitive about the stored prefix', () => {
    expect(displayIdentifier('card-0007')).toBe('#7')
  })

  it('passes anything that is not CARD-<digits> through verbatim', () => {
    expect(displayIdentifier('LIN-441')).toBe('LIN-441')
    expect(displayIdentifier('#41')).toBe('#41')
    expect(displayIdentifier('')).toBe('')
  })
})

describe('cardNumber', () => {
  it('parses the numeric part and drops the zero padding', () => {
    expect(cardNumber('CARD-0041')).toBe(41)
    expect(cardNumber('CARD-0000')).toBe(0)
  })

  it('returns null for a foreign scheme', () => {
    expect(cardNumber('GH-19')).toBeNull()
    expect(cardNumber('CARD-abc')).toBeNull()
  })
})

describe('identifierQueryDigits', () => {
  it('accepts every form a human types for the same card', () => {
    for (const form of ['#41', '41', 'CARD-0041', 'card-41', 'Card-0041', ' 41 ']) {
      expect(identifierQueryDigits(form)).toBe('41')
    }
  })

  it('rejects a query that is not an identifier reference', () => {
    expect(identifierQueryDigits('pty')).toBeNull()
    expect(identifierQueryDigits('card')).toBeNull()
    expect(identifierQueryDigits('')).toBeNull()
    expect(identifierQueryDigits('41a')).toBeNull()
  })
})

describe('matchesIdentifierQuery', () => {
  it('treats all identifier forms as equivalent', () => {
    for (const form of ['#41', '41', 'CARD-0041', 'card-41']) {
      expect(matchesIdentifierQuery('CARD-0041', form)).toBe(true)
      expect(matchesIdentifierQuery('CARD-0007', form)).toBe(false)
    }
  })

  it('narrows by decimal prefix so incremental typing keeps working', () => {
    expect(matchesIdentifierQuery('CARD-0004', '4')).toBe(true)
    expect(matchesIdentifierQuery('CARD-0041', '4')).toBe(true)
    expect(matchesIdentifierQuery('CARD-0041', '41')).toBe(true)
    expect(matchesIdentifierQuery('CARD-0014', '41')).toBe(false)
  })

  it('falls back to a substring of the stored form for foreign schemes', () => {
    expect(matchesIdentifierQuery('LIN-441', '441')).toBe(true)
    expect(matchesIdentifierQuery('LIN-441', 'lin')).toBe(true)
    expect(matchesIdentifierQuery('LIN-441', 'xyz')).toBe(false)
  })

  it('matches everything on an empty query', () => {
    expect(matchesIdentifierQuery('CARD-0041', '   ')).toBe(true)
  })
})
