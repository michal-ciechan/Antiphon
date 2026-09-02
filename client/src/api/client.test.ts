import { describe, expect, it } from 'vitest'
import { ApiError, getApiErrorMessage, getApiFieldErrors } from './client'

/**
 * Validation on this API is **422 with a PascalCase-keyed `errors` dict**, not 400 with a flat
 * message — `ValidationException` has mapped that way across the codebase all along. These pin the
 * shape the card edit/create dialogs hang their inline errors on.
 */
describe('getApiFieldErrors', () => {
  it('flattens a 422 errors dict to one message per field, keys untouched', () => {
    const error = new ApiError(422, 'Unprocessable Content', {
      title: 'One or more validation errors occurred.',
      status: 422,
      errors: {
        Description: ['Description must be at most 20,000 characters; got 20,001.'],
        Reason: ['Reason is required.', 'and a second message nobody shows'],
      },
    })

    expect(getApiFieldErrors(error)).toEqual({
      // The keys are C# member names, because that is what `nameof(request.Description)` produces.
      Description: 'Description must be at most 20,000 characters; got 20,001.',
      Reason: 'Reason is required.',
    })
  })

  it('is empty for a 409 — a concurrency conflict belongs in a notification, not on an input', () => {
    const error = new ApiError(409, 'Conflict', {
      title: 'Conflict',
      detail: 'Cannot archive a card with a live owner session.',
      status: 409,
    })

    expect(getApiFieldErrors(error)).toEqual({})
    // …and the notification path still has something to say.
    expect(getApiErrorMessage(error, 'fallback'))
      .toBe('Cannot archive a card with a live owner session.')
  })

  it('is empty for anything that is not an ApiError at all', () => {
    expect(getApiFieldErrors(new Error('offline'))).toEqual({})
    expect(getApiFieldErrors(undefined)).toEqual({})
    expect(getApiFieldErrors(new ApiError(500, 'Server Error', 'plain text body'))).toEqual({})
  })

  it('skips fields whose message array is empty or blank rather than mapping an empty string', () => {
    const error = new ApiError(422, 'Unprocessable Content', {
      errors: { Title: [], Importance: ['  '], Reason: ['Reason is required.'] },
    })

    expect(getApiFieldErrors(error)).toEqual({ Reason: 'Reason is required.' })
  })
})
