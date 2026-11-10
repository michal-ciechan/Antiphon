"""Register the plain snapshot Windmill script and schedule.

The bearer value comes from the environment and is never printed.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

WORKSPACE = "mc"
HOST = "server2.tail62cf02.ts.net"
PAIRS = (
    ("antiphon-postgres-snapshot.json", "antiphon-postgres-snapshot.schedule.json"),
    ("antiphon-postgres-snapshot-stale.json", "antiphon-postgres-snapshot-stale.schedule.json"),
)


def main():
    token = os.environ.get("WM_TOKEN", "")
    if not token:
        print("apply_error=no-auth")
        return 1
    root = sys.argv[1]
    enable = sys.argv[2] == "enable"
    run_once = len(sys.argv) > 3 and sys.argv[3] == "run"

    def call(method, path, payload=None, timeout=60):
        data = None
        headers = {"Host": HOST, "Authorization": "Bearer " + token}
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(
            "http://localhost" + path, data=data, headers=headers, method=method
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return resp.status, resp.read().decode("utf-8", "replace"), resp.headers
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", "replace")
            return exc.code, body, exc.headers

    def emit(text):
        for line in text.splitlines():
            if token and token in line:
                continue
            lowered = line.lower()
            if "password=" in lowered or "postgres://" in lowered or "bearer " in lowered:
                continue
            print(line)

    code, _body, _headers = call("GET", "/api/users/whoami")
    print("whoami=%s" % code)
    if code != 200:
        return 1

    for script_name, schedule_name in PAIRS:
        with open(os.path.join(root, script_name), "r", encoding="utf-8") as handle:
            script_def = json.load(handle)
        with open(os.path.join(root, schedule_name), "r", encoding="utf-8") as handle:
            schedule_def = json.load(handle)
        path = script_def["path"]
        get_code, get_body, _headers = call(
            "GET", "/api/w/%s/scripts/get/p/%s" % (WORKSPACE, path)
        )
        payload = {
            "path": path,
            "summary": script_def.get("summary", ""),
            "description": script_def.get("description", ""),
            "content": script_def["content"],
            "language": script_def["language"],
            "tag": script_def.get("tag", "desktop"),
            "schema": script_def.get("schema", {"properties": {}, "required": []}),
        }
        if get_code == 200:
            live = json.loads(get_body)
            parent = live.get("hash") or ""
            if parent:
                payload["parent_hash"] = parent
        post_code, post_body, _headers = call(
            "POST", "/api/w/%s/scripts/create" % WORKSPACE, payload
        )
        print("script_create %s %s" % (path, post_code))
        if post_code not in (200, 201):
            emit(post_body[:400])
            return 1
        back_code, back_body, _headers = call(
            "GET", "/api/w/%s/scripts/get/p/%s" % (WORKSPACE, path)
        )
        if back_code != 200:
            print("script_readback %s %s" % (path, back_code))
            return 1
        back = json.loads(back_body)
        if back.get("content") != script_def["content"]:
            print("script_drift %s" % path)
            return 1
        print("script_readback %s ok" % path)

        sched_path = schedule_def["path"]
        sched_payload = {
            "path": sched_path,
            "schedule": schedule_def["schedule"],
            "timezone": schedule_def["timezone"],
            "script_path": schedule_def["script_path"],
            "is_flow": bool(schedule_def.get("is_flow", False)),
            "args": schedule_def.get("args") or {},
            "enabled": bool(enable),
            "tag": schedule_def.get("tag", "desktop"),
        }
        sched_get, _sched_body, _headers = call(
            "GET", "/api/w/%s/schedules/get/%s" % (WORKSPACE, sched_path)
        )
        if sched_get == 200:
            edit_payload = {
                "schedule": sched_payload["schedule"],
                "timezone": sched_payload["timezone"],
                "script_path": sched_payload["script_path"],
                "is_flow": sched_payload["is_flow"],
                "args": sched_payload["args"],
                "tag": sched_payload["tag"],
            }
            upd, ubody, _headers = call(
                "POST",
                "/api/w/%s/schedules/update/%s" % (WORKSPACE, sched_path),
                edit_payload,
            )
            print("schedule_update %s %s" % (sched_path, upd))
            if upd not in (200, 201):
                emit(ubody[:400])
                return 1
        else:
            created, cbody, _headers = call(
                "POST", "/api/w/%s/schedules/create" % WORKSPACE, sched_payload
            )
            print("schedule_create %s %s" % (sched_path, created))
            if created not in (200, 201):
                emit(cbody[:400])
                return 1
        set_code, _sbody, _headers = call(
            "POST",
            "/api/w/%s/schedules/setenabled/%s" % (WORKSPACE, sched_path),
            {"enabled": bool(enable)},
        )
        print("schedule_setenabled %s %s" % (sched_path, set_code))
        check, check_body, _headers = call(
            "GET", "/api/w/%s/schedules/get/%s" % (WORKSPACE, sched_path)
        )
        if check != 200:
            print("schedule_readback %s %s" % (sched_path, check))
            return 1
        live_sched = json.loads(check_body)
        enabled = bool(live_sched.get("enabled"))
        cron = live_sched.get("schedule", "")
        zone = live_sched.get("timezone", "")
        print(
            "schedule_readback path=%s enabled=%s schedule=%s timezone=%s"
            % (sched_path, str(enabled).lower(), cron, zone)
        )
        if enabled != bool(enable):
            return 1
        if cron != schedule_def["schedule"] or zone != schedule_def["timezone"]:
            print("schedule_drift %s" % sched_path)
            return 1

    peers = (
        "u/lndcobra/antiphon_nightly_tests",
        "u/lndcobra/antiphon_nightly_readiness",
        "u/lndcobra/antiphon_release_candidates",
        "u/lndcobra/telegram_notify",
        "u/lndcobra/desktop_heartbeat",
        "u/lndcobra/antiphon_build_junk_cleanup",
        "u/lndcobra/browser_overnight_close",
    )
    for peer in peers:
        script_code, script_body, _headers = call(
            "GET", "/api/w/%s/scripts/get/p/%s" % (WORKSPACE, peer)
        )
        mentions = "no"
        if script_code == 200:
            try:
                content = json.loads(script_body).get("content") or ""
            except ValueError:
                content = ""
            lowered = content.lower()
            if "telegram" in lowered or "tg-send" in lowered:
                mentions = "yes"
        sched_code, sched_body, _headers = call(
            "GET", "/api/w/%s/schedules/get/%s" % (WORKSPACE, peer)
        )
        enabled = "absent"
        cron = ""
        if sched_code == 200:
            try:
                live = json.loads(sched_body)
            except ValueError:
                live = {}
            enabled = str(bool(live.get("enabled"))).lower()
            cron = live.get("schedule") or ""
        print(
            "notify_peer path=%s script=%s schedule=%s enabled=%s cron=%s telegram=%s"
            % (peer, script_code, sched_code, enabled, cron, mentions)
        )

    if run_once:
        time.sleep(2)
        run_code, run_body, run_headers = call(
            "POST",
            "/api/w/%s/jobs/run_wait_result/p/%s"
            % (WORKSPACE, "u/lndcobra/antiphon_postgres_snapshot"),
            {},
            timeout=1800,
        )
        print("job_http=%s" % run_code)
        for key in run_headers.keys():
            if "job" in key.lower():
                value = run_headers.get(key)
                if value and token not in value:
                    print("job_header %s=%s" % (key, value))
        emit(run_body)
        if run_code not in (200, 201):
            return 1
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        print("apply_error=1")
        sys.exit(1)
