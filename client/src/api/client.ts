import { pushConsoleEntry } from '../shared/consoleRing'

const BASE_URL = '/api'
const CLIENT_SHA_HEADER = 'X-Antiphon-Client-Sha'

export class ApiError extends Error {
  status: number
  statusText: string
  body: unknown

  constructor(status: number, statusText: string, body: unknown) {
    super(`API Error ${status}: ${statusText}`)
    this.name = 'ApiError'
    this.status = status
    this.statusText = statusText
    this.body = body
  }
}

export function getApiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    const body = error.body
    if (body && typeof body === 'object') {
      const maybeErrors = 'errors' in body ? body.errors : undefined
      if (maybeErrors && typeof maybeErrors === 'object') {
        for (const value of Object.values(maybeErrors)) {
          if (Array.isArray(value) && typeof value[0] === 'string' && value[0].trim()) {
            return value[0]
          }
        }
      }

      const maybeDetail = 'detail' in body ? body.detail : undefined
      if (typeof maybeDetail === 'string' && maybeDetail.trim()) {
        return maybeDetail
      }

      const maybeTitle = 'title' in body ? body.title : undefined
      if (typeof maybeTitle === 'string' && maybeTitle.trim()) {
        return maybeTitle
      }
    }

    if (typeof body === 'string' && body.trim()) {
      return body
    }
  }

  return error instanceof Error ? error.message : fallback
}

/**
 * The problem-details `errors` dict flattened to one message per field, for hanging a validation
 * failure on the input that caused it rather than in a notification.
 *
 * Keys come back exactly as the server sends them — PascalCase C# member names (`Title`,
 * `Description`, `Reason`, `EditedBy`), because that is what `ValidationException` is constructed
 * with (`nameof(request.Description)`). Callers map those names onto their own inputs.
 *
 * Empty for anything that is not a validation failure (a 409, a network error, a non-`ApiError`),
 * so "nothing matched" and "not a validation error" are the same case at the call site: fall back
 * to `getApiErrorMessage` in a notification.
 */
export function getApiFieldErrors(error: unknown): Record<string, string> {
  const fields: Record<string, string> = {}
  if (!(error instanceof ApiError)) return fields

  const body = error.body
  if (!body || typeof body !== 'object') return fields
  const maybeErrors = 'errors' in body ? body.errors : undefined
  if (!maybeErrors || typeof maybeErrors !== 'object') return fields

  for (const [field, value] of Object.entries(maybeErrors)) {
    if (Array.isArray(value)) {
      const first = value.find((item) => typeof item === 'string' && item.trim())
      if (typeof first === 'string') fields[field] = first
    } else if (typeof value === 'string' && value.trim()) {
      fields[field] = value
    }
  }

  return fields
}

function withClientSha(headers?: HeadersInit): Headers {
  const next = new Headers(headers)
  next.set(CLIENT_SHA_HEADER, __ANTIPHON_SHA__)
  return next
}

async function apiFetch(path: string, init: RequestInit): Promise<Response> {
  const started = Date.now()
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: withClientSha(init.headers),
  })
  if (!response.ok) {
    pushConsoleEntry({
      level: 'fetch',
      message: `${init.method ?? 'GET'} ${path} ${response.status}`,
      url: path,
      status: response.status,
      ms: Date.now() - started,
    })
  }
  return response
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text()
    let body: unknown = text
    if (text) {
      try {
        body = JSON.parse(text)
      } catch {
        body = text
      }
    }
    throw new ApiError(response.status, response.statusText, body)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export async function apiGet<T>(path: string): Promise<T> {
  const response = await apiFetch(path, { method: 'GET' })
  return handleResponse<T>(response)
}

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return handleResponse<T>(response)
}

export async function apiPut<T>(path: string, body: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return handleResponse<T>(response)
}

export async function apiPatch<T>(path: string, body: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return handleResponse<T>(response)
}

export async function apiDelete<T = void>(path: string, body?: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: 'DELETE',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  return handleResponse<T>(response)
}
