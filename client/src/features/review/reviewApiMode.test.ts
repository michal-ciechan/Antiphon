import { afterEach, describe, expect, it, vi } from 'vitest'
import { REVIEW_API_STORAGE_KEY, reviewApiMode } from '../../api/reviewEngine'
import { currentReviewApiMode, forceReviewApiMode } from './reviewApi'

describe('review API mode selection', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    window.localStorage.removeItem(REVIEW_API_STORAGE_KEY)
    forceReviewApiMode(null)
  })

  it('is HTTP unless the env or localStorage asks for the fake, survives throwing storage, and yields to a forced mode', () => {
    expect(reviewApiMode({})).toBe('http')
    expect(reviewApiMode({ VITE_REVIEW_API: 'http' })).toBe('http')
    expect(reviewApiMode({ VITE_REVIEW_API: 'fake' })).toBe('fake')

    window.localStorage.setItem(REVIEW_API_STORAGE_KEY, 'fake')
    expect(reviewApiMode({})).toBe('fake')
    window.localStorage.setItem(REVIEW_API_STORAGE_KEY, 'http')
    expect(reviewApiMode({ VITE_REVIEW_API: 'fake' })).toBe('http')
    window.localStorage.setItem(REVIEW_API_STORAGE_KEY, 'nonsense')
    expect(reviewApiMode({})).toBe('http')

    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    expect(reviewApiMode({})).toBe('http')
    expect(reviewApiMode({ VITE_REVIEW_API: 'fake' })).toBe('fake')

    forceReviewApiMode('fake')
    expect(currentReviewApiMode()).toBe('fake')
  })
})
