import { useCallback, useEffect, useMemo, useState } from 'react'

const STORAGE_KEY = 'antiphon.reviewedFiles.v1'

export interface ReviewableFile {
  filename: string
  additions: number
  deletions: number
  patch: string
}

interface ReviewedFileRecord {
  fingerprint: string
  reviewedAt: string
}

type ReviewedFileRecords = Record<string, ReviewedFileRecord>

function readRecords(): ReviewedFileRecords {
  if (typeof window === 'undefined') return {}

  try {
    const stored = window.localStorage.getItem(STORAGE_KEY)
    if (!stored) return {}

    const parsed = JSON.parse(stored) as unknown
    if (!parsed || typeof parsed !== 'object') return {}

    return parsed as ReviewedFileRecords
  } catch {
    return {}
  }
}

function writeRecords(records: ReviewedFileRecords) {
  if (typeof window === 'undefined') return

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(records))
  } catch {
    // Local storage is best-effort UI state.
  }
}

function makeStorageKey(scope: string, filename: string) {
  return `${scope}::${filename}`
}

function hashString(value: string) {
  let hash = 2166136261
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }

  return (hash >>> 0).toString(16)
}

export function reviewedFileFingerprint(file: ReviewableFile) {
  return hashString([
    file.filename,
    file.additions.toString(),
    file.deletions.toString(),
    file.patch,
  ].join('\u0000'))
}

export function useReviewedFiles<TFile extends ReviewableFile>(scope: string, files: TFile[]) {
  const [records, setRecords] = useState<ReviewedFileRecords>(() => readRecords())
  const normalizedScope = scope.trim() || 'default'
  const fileEntries = useMemo(
    () => files.map((file) => ({
      file,
      fingerprint: reviewedFileFingerprint(file),
      storageKey: makeStorageKey(normalizedScope, file.filename),
    })),
    [files, normalizedScope],
  )
  const fileSignature = useMemo(
    () => fileEntries.map((entry) => `${entry.storageKey}:${entry.fingerprint}`).join('|'),
    [fileEntries],
  )

  const updateRecords = useCallback((updater: (records: ReviewedFileRecords) => ReviewedFileRecords) => {
    setRecords((current) => {
      const next = updater(current)
      if (next !== current) {
        writeRecords(next)
      }
      return next
    })
  }, [])

  useEffect(() => {
    updateRecords((current) => {
      let changed = false
      const next = { ...current }

      for (const entry of fileEntries) {
        const record = next[entry.storageKey]
        if (record && record.fingerprint !== entry.fingerprint) {
          delete next[entry.storageKey]
          changed = true
        }
      }

      return changed ? next : current
    })
  }, [fileEntries, fileSignature, updateRecords])

  const isReviewed = useCallback((file: TFile) => {
    const fingerprint = reviewedFileFingerprint(file)
    const record = records[makeStorageKey(normalizedScope, file.filename)]
    return record?.fingerprint === fingerprint
  }, [normalizedScope, records])

  const markReviewed = useCallback((file: TFile) => {
    const storageKey = makeStorageKey(normalizedScope, file.filename)
    const fingerprint = reviewedFileFingerprint(file)
    updateRecords((current) => ({
      ...current,
      [storageKey]: {
        fingerprint,
        reviewedAt: new Date().toISOString(),
      },
    }))
  }, [normalizedScope, updateRecords])

  const markUnreviewed = useCallback((file: TFile) => {
    const storageKey = makeStorageKey(normalizedScope, file.filename)
    updateRecords((current) => {
      if (!current[storageKey]) return current

      const next = { ...current }
      delete next[storageKey]
      return next
    })
  }, [normalizedScope, updateRecords])

  const reviewedFiles = useMemo(() => files.filter((file) => isReviewed(file)), [files, isReviewed])
  const unreviewedFiles = useMemo(() => files.filter((file) => !isReviewed(file)), [files, isReviewed])

  return {
    reviewedFiles,
    unreviewedFiles,
    isReviewed,
    markReviewed,
    markUnreviewed,
  }
}
