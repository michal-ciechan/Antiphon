// jsdom 29 keeps the sheet of a <style> that leaves the document inside a removed subtree:
// Node._remove detaches the descendants before it clears their cached root, so the element
// still reads as connected and re-adds its sheet. Each disposed xterm DOM renderer leaves
// ~790 rules behind that way, and every later getComputedStyle matches all of them: the
// CARD-0148 mouse file grew to 134 sheets and 34,982 rules and its cases passed the 20 s
// cap (rc run f40ad905). Re-running the style block update on a disconnected owner drops
// the sheet for good.
export function releaseDetachedStyleSheets(): void {
  for (const sheet of Array.from(document.styleSheets)) {
    const owner = sheet.ownerNode;
    if (!(owner instanceof HTMLStyleElement) || owner.isConnected) continue;
    const css = owner.textContent;
    owner.textContent = css;
  }
}
