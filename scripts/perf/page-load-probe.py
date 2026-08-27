# CARD-0217 page-load probe (docs/investigations/2026-08-28-card-0217-page-load-sweep.md §1).
#
# Runs INSIDE browser-harness against the CDP Edge on :9222 — this is not a standalone script:
#
#   $env:BU_CDP_URL = 'http://127.0.0.1:9222'
#   browser-harness < scripts/perf/page-load-probe.py
#
# Environment knobs (all optional):
#   AP_BASE      base URL (default http://localhost:17203; the remote domain works too)
#   AP_ONLY      comma list of page keys from PAGES (default: all)
#   AP_MODES     cold,warm (default both; cold = Network.setCacheDisabled)
#   AP_VIEWPORT  e.g. 390x844 to emulate a phone (MobileHomePage renders under 48em)
#   AP_BLOCK     comma list of Network.setBlockedURLs patterns, e.g. '*/api/boards/*' — the A/B knob
#   AP_PROFILE   1 to attach a CPU profile per load and print the top self-time frames
#   AP_OUT       JSONL path for the raw rows (default logs/perf/page-load-probe.jsonl)
#
# It installs a probe before app boot (Page.addScriptToEvaluateOnNewDocument) that stamps, via a
# MutationObserver on #root, the DOM commit in which the page's content marker appears, then dumps
# PerformanceResourceTiming for every request. "content" in its output is that commit, not `load`.
# The automation Edge is usually occluded (visibilityState hidden): its timers run at 1 Hz, so
# the sampler rows are coarse, but the MutationObserver marker is exact.
import json, time, sys, os

BASE = os.environ.get("AP_BASE", "http://localhost:17203")
OUT = os.environ.get("AP_OUT", os.path.join("logs", "perf", "page-load-probe.jsonl"))
os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)
ONLY = os.environ.get("AP_ONLY")  # comma list of page keys
MODES = os.environ.get("AP_MODES", "cold,warm").split(",")
VIEWPORT = os.environ.get("AP_VIEWPORT")
PROFILE = os.environ.get("AP_PROFILE") == "1"
BLOCK = [x for x in os.environ.get("AP_BLOCK", "").split(",") if x]
ACTIVATE = os.environ.get("AP_ACTIVATE", "1") == "1"  # e.g. "390x844" for mobile

BOARD = "8988ca03-7414-47ad-b0b6-51556c701703"
AGENT = "8478998e-f35e-46c7-9d5e-f9330c671474"
PLAN = "docs/superpowers/plans/2026-08-27-card-0216-remote-home-load-and-post-load-spinner-plan.md"

# key -> (path, text marker or None, selector marker or None)
PAGES = {
    "home":        ("/", None, '[data-testid="home-dock"]'),
    "home-mobile": ("/", "NEEDS YOU", None),
    "board":       (f"/boards/{BOARD}", None, '[data-testid="card-row-CARD-0217"]'),
    "boards-all":  ("/boards", "CARD-0217", None),
    "agents":      ("/agents", "Antiphon-Orchestrator", None),
    "settings":    ("/settings", "Full Feature Pipeline", None),
    "settings-projects": ("/settings?tab=projects", "az-care", None),
    "settings-tui": ("/settings?tab=agent-tui", None, '.mantine-Table-root, .mantine-Accordion-root, .mantine-Card-root'),
    "orchestrator": ("/orchestrator", "Running Sessions", None),
    "delegations": ("/orchestrator?tab=delegations", "CARD-0217", None),
    "attention":   ("/orchestrator?tab=attention", "Needs attention", None),
    "plans":       ("/plans", "CARD-0216", None),
    "plan-ref":    (f"/plans?file={PLAN}&ref=master", "Verdict, in one screen", None),
    "plan-head":   (f"/plans?file={PLAN}", "Verdict, in one screen", None),
    "channels":    ("/channels", "AZ Care", None),
    "workflows":   ("/workflows", None, '.mantine-Container-root'),
    "agent-files": (f"/agents/{AGENT}/files", None, '[data-testid="conversation-dock"]'),
}

PROBE = r"""
(function(){
  const M = %s; const S = %s;
  const ap = window.__ap = {samples:[], longtasks:[], firstText:null, marker:null};
  try { new PerformanceObserver(l=>{ for (const e of l.getEntries()) ap.longtasks.push([Math.round(e.startTime), Math.round(e.duration)]); }).observe({type:'longtask', buffered:true}); } catch(e){}
  const check = ()=>{
    if (ap.marker!==null) return;
    const now = Math.round(performance.now());
    const hit = (M && document.body && document.body.textContent.indexOf(M) >= 0) || (S && !!document.querySelector(S));
    if (ap.firstText===null && document.body && document.body.textContent.length>0) ap.firstText = now;
    if (hit) { ap.marker = now; }
  };
  const arm = ()=>{ const root=document.getElementById('root'); if(!root){ setTimeout(arm,1); return; } new MutationObserver(check).observe(root,{childList:true,subtree:true,characterData:true}); check(); };
  arm();
  const iv = setInterval(()=>{
    const root = document.getElementById('root');
    const txt = root ? (root.innerText||'') : '';
    const loaders = document.querySelectorAll('.mantine-Loader-root, .mantine-Skeleton-root').length;
    const now = Math.round(performance.now());
    const hit = (M && txt.indexOf(M) >= 0) || (S && !!document.querySelector(S));
    ap.samples.push([now, txt.length, loaders]);
    if (ap.firstText===null && txt.length>0) ap.firstText = now;
    if (ap.marker===null && hit) ap.marker = now;
    if (window.__apVis === undefined || window.__apVis !== document.visibilityState) { window.__apVis = document.visibilityState; ap.samples.push([now, -1, document.visibilityState==="visible"?1:0]); }
    if (ap.samples.length > 1200) clearInterval(iv);
  }, 50);
})();
"""


def summarise_profile(key, mode, prof):
    nodes = {n["id"]: n for n in prof["nodes"]}
    selfus = {}
    t = prof["startTime"]
    for sid, dt in zip(prof["samples"], prof["timeDeltas"]):
        selfus[sid] = selfus.get(sid, 0) + dt
    total = sum(selfus.values()) / 1000.0
    agg = {}
    for nid, us in selfus.items():
        cf = nodes[nid]["callFrame"]
        name = f"{cf['functionName'] or '(anon)'} {cf['url'].split('/')[-1]}:{cf['lineNumber']}:{cf['columnNumber']}"
        agg[name] = agg.get(name, 0) + us
    top = sorted(agg.items(), key=lambda x: -x[1])[:25]
    idle = sum(v for k, v in agg.items() if k.startswith("(idle)")) / 1000.0
    print(f"  PROFILE {key} {mode}: wall={total:.0f}ms idle={idle:.0f}ms busy={total-idle:.0f}ms")
    for name, us in top:
        if "(idle)" in name: continue
        print(f"     {us/1000:8.0f}ms  {name[:120]}")
    with open(OUT + f".{key}.{mode}.cpuprofile", "w", encoding="utf-8") as f:
        json.dump(prof, f)

def run_page(key, path, marker, sel, mode):
    url = BASE + path
    src = PROBE % (json.dumps(marker), json.dumps(sel))
    ident = cdp("Page.addScriptToEvaluateOnNewDocument", source=src)["identifier"]
    cdp("Network.setCacheDisabled", cacheDisabled=(mode == "cold"))
    cdp("Network.setBlockedURLs", urls=BLOCK)
    goto_url("about:blank")
    time.sleep(0.3)
    t_start = time.time()
    if PROFILE: cdp("Profiler.enable"); cdp("Profiler.setSamplingInterval", interval=500); cdp("Profiler.start")
    goto_url(url)
    deadline = t_start + 45
    marker_at = None
    while time.time() < deadline:
        time.sleep(0.5)
        try:
            st = js("JSON.stringify({m: window.__ap && window.__ap.marker, rs: document.readyState})")
            st = json.loads(st) if isinstance(st, str) else st
        except Exception as e:
            continue
        if st and st.get("m") is not None:
            marker_at = st["m"]
            break
    # let trailing requests land
    time.sleep(3)
    prof = None
    if PROFILE:
        prof = cdp("Profiler.stop")["profile"]; cdp("Profiler.disable")
    dump = js(r"""(function(){
      const nav = performance.getEntriesByType('navigation')[0] || {};
      const res = performance.getEntriesByType('resource').map(r=>({
        n: r.name.replace(location.origin,''), it: r.initiatorType, s: Math.round(r.startTime),
        e: Math.round(r.responseEnd), d: Math.round(r.duration), ts: r.transferSize, eb: r.encodedBodySize, db: r.decodedBodySize,
        rs: Math.round(r.responseStart), fb: Math.round(r.responseStart - r.startTime)}));
      const ap = window.__ap || {};
      return JSON.stringify({nav:{di:Math.round(nav.domInteractive||0), dcl:Math.round(nav.domContentLoadedEventEnd||0), le:Math.round(nav.loadEventEnd||0), ts: nav.transferSize},
        res, firstText: ap.firstText, marker: ap.marker, longtasks: ap.longtasks, samples: ap.samples, url: location.href, mem: (performance.memory? Math.round(performance.memory.usedJSHeapSize/1048576):null)});
    })()""")
    data = json.loads(dump) if isinstance(dump, str) else dump
    cdp("Page.removeScriptToEvaluateOnNewDocument", identifier=ident)
    data.update({"page": key, "mode": mode, "path": path, "viewport": VIEWPORT, "base": BASE, "block": BLOCK, "visibility": js("document.visibilityState")})
    if prof: summarise_profile(key, mode, prof)
    with open(OUT, "a", encoding="utf-8") as f:
        f.write(json.dumps(data) + "\n")
    res = data["res"]
    api = [r for r in res if r["n"].startswith("/api") or r["n"].startswith("/hubs")]
    scripts = [r for r in res if r["it"] in ("script", "link", "other") and "/assets/" in r["n"]]
    lt = sum(d for _, d in data["longtasks"])
    print(f"{key:18s} {mode:4s} marker={data['marker']} firstText={data['firstText']} DI={data['nav']['di']} load={data['nav']['le']} "
          f"res={len(res)} api={len(api)} apiBytes={sum(r['db'] or 0 for r in api)} assetXfer={sum(r['ts'] or 0 for r in scripts)} longtask={lt}ms mem={data['mem']}MB")
    for r in api:
        print(f"    {r['s']:6d} +{r['d']:5d} fb={r['fb']:5d} {r['db'] or 0:8d}B {r['n'][:110]}")
    return data

cdp("Network.enable")
cdp("Page.enable")
if ACTIVATE:
    try: activate_tab(page_info().get("target_id") or list_tabs()[0]["target_id"])
    except Exception as e: print("activate failed", e)
    try: cdp("Page.bringToFront")
    except Exception as e: print("bringToFront failed", e)
    try:
        w = cdp("Browser.getWindowForTarget")
        cdp("Browser.setWindowBounds", windowId=w["windowId"], bounds={"windowState": "normal"})
        cdp("Browser.setWindowBounds", windowId=w["windowId"], bounds={"left": 0, "top": 0, "width": 1600, "height": 1000})
        cdp("Page.bringToFront")
        print("window", w, "visibility", js("document.visibilityState"))
    except Exception as e: print("window failed", e)

cdp("Emulation.clearDeviceMetricsOverride")
if VIEWPORT:
    w, h = [int(x) for x in VIEWPORT.split("x")]
    cdp("Emulation.setDeviceMetricsOverride", width=w, height=h, deviceScaleFactor=2, mobile=True)
keys = ONLY.split(",") if ONLY else list(PAGES.keys())
for key in keys:
    path, marker, sel = PAGES[key]
    for mode in MODES:
        try:
            run_page(key, path, marker, sel, mode)
        except Exception as e:
            print(f"{key} {mode} FAILED: {e!r}")
cdp("Network.setCacheDisabled", cacheDisabled=False)
print("DONE")
