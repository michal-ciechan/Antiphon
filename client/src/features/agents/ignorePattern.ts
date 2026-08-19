export type IgnoreScope = 'name' | 'path'

/**
 * The .gitignore line for a target, for each scope.
 *
 * - `name` — matches anywhere in the repo. Bare, unanchored: gitignore matches a pattern with no
 *   slash against the basename at every level, which is what "all the bin-check folders" means.
 * - `path` — matches only this one. Leading slash anchors it to the .gitignore's own directory,
 *   so `/server/bin` cannot also catch `client/server/bin`.
 *
 * Folders get a trailing slash so the pattern can never match a FILE that happens to share the
 * name. Lives outside IgnorePathModal.tsx so the component file only exports components
 * (react-refresh) — getting these two lines right is the whole feature, and the tests import
 * from here.
 */
export function ignorePatternFor(path: string, isFolder: boolean, scope: IgnoreScope): string {
  const clean = path.replace(/^\/+|\/+$/g, '')
  if (scope === 'name') {
    const name = clean.split('/').pop() ?? clean
    return isFolder ? `${name}/` : name
  }
  return isFolder ? `/${clean}/` : `/${clean}`
}
