"""Load the 4D menu part labels (labels.json) into an Axis API.

Usage:
    AXIS_API=https://<host>/api AXIS_EMAIL=<admin email> AXIS_PASSWORD=<password> python3 apply_labels.py [--check]

--check only reports whether the API has the 4D endpoints yet. Items that already have
labels are left alone (staff edits win), so running it again is safe.
"""
import json, os, sys, urllib.request, urllib.error

API = os.environ.get("AXIS_API", "").rstrip("/")
HERE = os.path.dirname(os.path.abspath(__file__))


def call(method, path, body=None, token=None):
    req = urllib.request.Request(API + path, method=method, data=json.dumps(body).encode() if body is not None else None)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e:
        return e.code, None


def main():
    if not API:
        sys.exit("Set AXIS_API")
    status, _ = call("GET", "/menu-4d/public")
    if status != 200:
        print(f"NOT_READY: /menu-4d/public returned {status}")
        return 2
    if "--check" in sys.argv:
        print("READY")
        return 0
    status, login = call("POST", "/Auth/login", {"email": os.environ["AXIS_EMAIL"], "password": os.environ["AXIS_PASSWORD"]})
    token = (login or {}).get("token")
    if not token:
        print(f"LOGIN_FAILED: {status}")
        return 3
    status, rows = call("GET", "/menu-4d", token=token)
    if status != 200:
        print(f"LIST_FAILED: {status}")
        return 4
    existing = {r["itemId"]: r for r in rows}
    labels = json.load(open(os.path.join(HERE, "labels.json"), encoding="utf-8"))
    done = skipped = missing = failed = 0
    for it in labels:
        row = existing.get(it["id"])
        if row is None:
            missing += 1
            continue
        if row["parts"]:
            skipped += 1
            continue
        body = {"parts": it["parts"], "published": bool(it["parts"]), "realSizeCm": it.get("realSizeCm")}
        s, r = call("PUT", f"/menu-4d/{it['id']}", body, token)
        if s == 200 and r and r.get("success"):
            done += 1
        else:
            failed += 1
            print(f"  item {it['id']} {it.get('name', '')}: HTTP {s} {r and r.get('message')}")
    print(f"APPLIED {done}, already labelled {skipped}, not on menu {missing}, failed {failed}")
    return 0 if failed == 0 else 5


if __name__ == "__main__":
    sys.exit(main())
