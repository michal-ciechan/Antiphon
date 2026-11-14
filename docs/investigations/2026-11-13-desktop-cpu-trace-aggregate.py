"""Aggregate a dotnet-trace speedscope export (dotnet-sampled-thread-time) into CPU-candidate time.

Usage: python -I aggregate.py <file.speedscope.json> <window_seconds> [top]

Thread-time sampling records every managed thread, blocked or not. A sample whose leaf frame is a
known wait/poll primitive is classed as idle; everything else is a CPU candidate (it may still
include short native blocking inside a non-wait frame). Times are reported in seconds and in
cores (seconds / window). Written for CARD task b754552c (2026-11-13 desktop CPU trace).
"""
import json
import re
import sys
from collections import defaultdict

WAIT_LEAF = re.compile(
    r"WaitForSignal|WaitOneNoCheck|Monitor\.Wait|ManualResetEventSlim\.Wait|WaitAnyMultiple|"
    r"WaitMultiple|SemaphoreSlim\.Wait|IOCompletionPoller\.Poll|\]\.Consume\(int32\)|StartPollTask|"
    r"Thread\.Sleep|GC\.RunFinalizers|Interop\+Kernel32\.ReadFile|SleepEx|WaitForSingleObject|"
    r"Thread\.Join|Task\.SpinThenBlockingWait|Task\.InternalWaitCore|LowLevelLifoSemaphore\.Wait\(|"
    r"Interop\+Kernel32\.GetQueuedCompletionStatus|WaitHandle\.WaitOne|Monitor\.Enter|"
    r"LowLevelMonitor\.Wait|Interop\+Winsock\.select|PipeStream.*Read|"
    r"AsyncTaskMethodBuilder.*Wait"
)

BUCKETS = [
    ("GC/finalizer", r"GC\.|Finalize|DestroyScout|ReRegisterForFinalize"),
    ("Serilog/logging", r"Serilog|Microsoft\.Extensions\.Logging|LoggerMessage|Logger`1|\.Log\("),
    ("EF Core", r"EntityFrameworkCore|RelationalDataReader|GetClrValueUsingContainingEntity|QueryCompil|ShapedQuery"),
    ("Npgsql", r"Npgsql"),
    ("JSON", r"System\.Text\.Json|JsonSerializer|Utf8JsonWriter|Utf8JsonReader|ReadConstructorArguments|Newtonsoft"),
    ("SignalR", r"SignalR"),
    ("Hangfire", r"Hangfire"),
    ("Kafka", r"Confluent|Kafka"),
    ("Kestrel/HTTP", r"Kestrel|AspNetCore\.Http|AspNetCore\.Routing|AspNetCore\.Mvc|HttpClient|SocketsHttpHandler"),
    ("Reflection/DynamicMethod", r"DynamicMethod|RuntimeMethodInfo|Reflection\.Emit|Expression\.Compile|LambdaCompiler"),
]


def main():
    path, window = sys.argv[1], float(sys.argv[2])
    top = int(sys.argv[3]) if len(sys.argv) > 3 else 40
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    frames = [fr["name"] for fr in doc["shared"]["frames"]]
    excl = defaultdict(float)
    incl = defaultdict(float)
    bucket = defaultdict(float)
    antiphon_type = defaultdict(float)
    thread_total = {}
    thread_cpu = {}
    kind = defaultdict(float)
    idle_total = 0.0
    cpu_total = 0.0
    for prof in doc["profiles"]:
        name = prof.get("name", "?")
        stack = []
        last = None
        tt = 0.0
        tc = 0.0
        for ev in prof["events"]:
            at = ev["at"]
            if last is not None and stack and at > last:
                dt = (at - last) / 1000.0
                tt += dt
                leaf = frames[stack[-1]]
                pseudo = leaf in ("UNMANAGED_CODE_TIME", "CPU_TIME")
                if pseudo:
                    kind[leaf] += dt
                    if len(stack) > 1:
                        leaf = frames[stack[-2]]
                if WAIT_LEAF.search(leaf):
                    idle_total += dt
                else:
                    tc += dt
                    cpu_total += dt
                    excl[leaf] += dt
                    seen = set()
                    for fi in stack:
                        if fi not in seen:
                            seen.add(fi)
                            incl[frames[fi]] += dt
                    names = [frames[fi] for fi in stack]
                    joined = "\n".join(names)
                    hit = "other"
                    # attribute to the innermost matching bucket
                    for fn in reversed(names):
                        for b, rx in BUCKETS:
                            if re.search(rx, fn):
                                hit = b
                                break
                        if hit != "other":
                            break
                    bucket[hit] += dt
                    # innermost Antiphon frame type
                    for fn in reversed(names):
                        m = re.search(r"(Antiphon\.[\w.]+?)\.[\w<>`]+\(", fn)
                        if m:
                            antiphon_type[m.group(1)] += dt
                            break
                    else:
                        antiphon_type["(no Antiphon frame)"] += dt
            if ev["type"] == "O":
                stack.append(ev["frame"])
            else:
                if stack:
                    stack.pop()
            last = at
        thread_total[name] = tt
        thread_cpu[name] = tc
    def pr(title, d, n):
        print(f"\n## {title}\n")
        print("| # | seconds | cores | frame |\n|---:|---:|---:|---|")
        for i, (k, v) in enumerate(sorted(d.items(), key=lambda kv: -kv[1])[:n], 1):
            k2 = k.replace("|", "/")[:160]
            print(f"| {i} | {v:.1f} | {v / window:.3f} | `{k2}` |")
    print(f"window {window}s; threads {len(thread_total)}; thread-seconds {sum(thread_total.values()):.1f}; "
          f"idle {idle_total:.1f}s; cpu-candidate {cpu_total:.1f}s = {cpu_total / window:.3f} cores")
    print("pseudo-leaf split: " + ", ".join(f"{k}={v:.1f}s" for k, v in kind.items()))
    pr("CPU-candidate buckets (innermost match)", bucket, 20)
    pr("CPU-candidate by innermost Antiphon type", antiphon_type, 30)
    pr(f"Top {top} exclusive (CPU-candidate)", excl, top)
    pr(f"Top {top} inclusive (CPU-candidate)", incl, top)
    pr("Busiest threads (CPU-candidate)", thread_cpu, 15)


if __name__ == "__main__":
    main()
