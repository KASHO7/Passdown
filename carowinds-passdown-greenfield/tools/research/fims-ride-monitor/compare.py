#!/usr/bin/env python3
"""Compare saved FIMS ride-schedule observations and write a Markdown study report.

  python compare.py --date 2026-10-24

Reads only this tool's output folder; makes no network requests. Every difference found is a
*published schedule change*. Nothing here decides that a ride was closed or down.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from pathlib import Path

import fims_common as fc

# An observation made at/after this Eastern time on a later calendar day is treated as a "later
# historical lookup" of the finished day rather than part of the operating-day study.
HISTORICAL_LOOKUP_FROM = dt.time(6, 0)

SCHEDULE_CHANGE_LABELS = {
    "blank_to_populated": "Blank → populated",
    "populated_to_blank": "Populated → blank",
    "opening_changed": "Opening time changed",
    "closing_changed": "Closing time changed",
    "ride_added": "Ride added to response",
    "ride_removed": "Ride removed from response",
    "ride_renamed": "Ride renamed (same official ID)",
}
SCHEDULE_STATES = {"populated", "blank"}

NO_CHANGE_SENTENCE = ("**No ride schedule changes were observed during the sampled intervals. "
                      "This does not establish that no rides experienced downtime.**")


# --- Comparison --------------------------------------------------------------------------------------


def _gate_map(obs: dict) -> dict:
    return {g["type_id"]: (g.get("label"), g.get("from"), g.get("to")) for g in obs.get("gate", [])}


def _schedule_signature(obs: dict) -> tuple:
    return (obs.get("park_closed"), sorted(_gate_map(obs).items()),
            [(r["id"], r["name"], r["state"], r["from"], r["to"]) for r in obs.get("rides", [])])


def compare_pair(prev: dict, cur: dict) -> dict:
    """Differences between two comparable observations, keyed by official ride ID."""
    result = {"ride_changes": [], "gate_changes": [], "park_changes": [], "data_quality": [],
              "metadata": []}
    if prev.get("park_closed") != cur.get("park_closed"):
        result["park_changes"].append({"old": prev.get("park_closed"), "new": cur.get("park_closed")})

    old_gate, new_gate = _gate_map(prev), _gate_map(cur)
    for type_id in sorted(set(old_gate) | set(new_gate)):
        if old_gate.get(type_id) != new_gate.get(type_id):
            result["gate_changes"].append({"type_id": type_id, "old": old_gate.get(type_id),
                                           "new": new_gate.get(type_id)})

    # A closed-day observation carries no ride rows; that is not "every ride removed".
    if not prev.get("park_closed") and not cur.get("park_closed"):
        old_rides = {r["id"]: r for r in prev.get("rides", [])}
        new_rides = {r["id"]: r for r in cur.get("rides", [])}
        for ride_id in sorted(set(old_rides) | set(new_rides)):
            old, new = old_rides.get(ride_id), new_rides.get(ride_id)
            name = (new or old)["name"]

            def add(kind: str, **extra) -> None:
                result["ride_changes"].append({"kind": kind, "id": ride_id, "name": name,
                                               "old": old, "new": new, **extra})

            if old is None:
                add("ride_added")
                continue
            if new is None:
                add("ride_removed")
                continue
            if old["name"] != new["name"]:
                add("ride_renamed", old_name=old["name"], new_name=new["name"])
            if old["state"] not in SCHEDULE_STATES or new["state"] not in SCHEDULE_STATES:
                if (old["state"], old["from"], old["to"]) != (new["state"], new["from"], new["to"]):
                    result["data_quality"].append({"id": ride_id, "name": name, "old": old, "new": new})
                continue
            if old["state"] == "blank" and new["state"] == "populated":
                add("blank_to_populated")
            elif old["state"] == "populated" and new["state"] == "blank":
                add("populated_to_blank")
            elif old["state"] == "populated":
                if old["from"] != new["from"]:
                    add("opening_changed")
                if old["to"] != new["to"]:
                    add("closing_changed")

    schedule_identical = _schedule_signature(prev) == _schedule_signature(cur)
    for header in ("etag", "last_modified"):
        if prev.get(header) != cur.get(header):
            result["metadata"].append({"header": header, "old": prev.get(header), "new": cur.get(header),
                                       "schedule_identical": schedule_identical})
    return result


def _is_historical_lookup(obs: dict, operating_date: dt.date) -> bool:
    local = fc.to_eastern(obs["_observed"])
    return local.date() > operating_date and (
        local.date() > operating_date + dt.timedelta(days=1) or local.time() >= HISTORICAL_LOOKUP_FROM)


def analyze(observations: list[dict], operating_date: dt.date) -> dict:
    """observations must already be sorted by observed UTC (fims_common.load_observations)."""
    study = [o for o in observations if not _is_historical_lookup(o, operating_date)]
    lookups = [o for o in observations if _is_historical_lookup(o, operating_date)]
    good = [o for o in study if o.get("comparable")]
    failed = [o for o in study if not o.get("comparable")]

    pairs = []
    previous = None
    for obs in good:  # failed observations are skipped; the last good one is kept for comparison
        if previous is not None:
            pairs.append((previous, obs, compare_pair(previous, obs)))
        previous = obs

    lookup_results = []
    final = good[-1] if good else None
    for lookup in lookups:
        if not lookup.get("comparable") or final is None:
            lookup_results.append((lookup, None))
        else:
            lookup_results.append((lookup, _schedule_signature(lookup) == _schedule_signature(final)))

    gaps = [(b["_observed"] - a["_observed"]) for a, b in zip(study, study[1:])]
    return {"study": study, "good": good, "failed": failed, "pairs": pairs,
            "lookups": lookup_results, "gaps": gaps, "final": final, "first": good[0] if good else None}


# --- Report --------------------------------------------------------------------------------------------


def _et(obs: dict) -> str:
    return fc.format_eastern(obs["_observed"])


def _ride_schedule(ride: dict | None) -> str:
    return "Not in response" if ride is None else fc.format_schedule(ride)


def _gate_text(value) -> str:
    if value is None:
        return "Not listed"
    label, time_from, time_to = value
    if not time_from or not time_to:
        return f"{label}: no hours"
    return f"{label}: {fc.format_clock(time_from)} – {fc.format_clock(time_to)}"


def _main_gate_close(obs: dict) -> dt.datetime | None:
    gate = next((g for g in obs.get("gate", []) if g["type_id"] == fc.MAIN_GATE_TYPE_ID), None)
    if not gate or not fc.is_hhmm(gate.get("from")) or not fc.is_hhmm(gate.get("to")):
        return None
    day = fc.parse_operating_date(obs["operating_date"])
    if fc.crosses_midnight(gate["from"], gate["to"]):
        day += dt.timedelta(days=1)
    hour, minute = int(gate["to"][:2]), int(gate["to"][3:])
    return dt.datetime.combine(day, dt.time(hour, minute), tzinfo=fc.EASTERN)


def _md(text: object) -> str:
    return str(text).replace("|", "\\|")


def render_report(operating_date: dt.date, analysis: dict, unreadable: list[str],
                  notes: list[dict], generated_utc: dt.datetime) -> str:
    study, good, failed, pairs = analysis["study"], analysis["good"], analysis["failed"], analysis["pairs"]
    out: list[str] = []
    w = out.append

    w(f"# FIMS intraday ride schedule study — {operating_date.isoformat()}")
    w("")
    w(f"Generated {fc.format_eastern(generated_utc)} by `compare.py` from saved observations. "
      "Source: official Carowinds FIMS park-hours feed (park ID 30), published *scheduled* hours only.")
    w("")
    w("> Every change below is a **published schedule change**. The feed has no live status or downtime "
      "field, so no change here establishes that a ride was closed, down or delayed, and a blank schedule "
      "is not a closure.")
    w("")

    w("## 1. Study summary")
    w("")
    rows = [("Operating date", operating_date.isoformat())]
    if study:
        rows.append(("Study start", _et(study[0])))
        rows.append(("Study end", _et(study[-1])))
    else:
        rows.append(("Study start / end", "No observations"))
    rows.append(("Successful observations", str(len(good))))
    rows.append(("Failed observations", str(len(failed))))
    if analysis["gaps"]:
        minutes = sorted(round(g.total_seconds() / 60) for g in analysis["gaps"])
        rows.append(("Observation interval", f"median {minutes[len(minutes) // 2]} min "
                                             f"(range {minutes[0]}–{minutes[-1]} min)"))
    else:
        rows.append(("Observation interval", "n/a (fewer than two observations)"))
    all_ids = {r["id"] for o in good for r in o.get("rides", [])}
    rows.append(("Unique rides (by official ID)", str(len(all_ids))))
    final = analysis["final"]
    if final:
        rows.append(("Rides with scheduled hours (final observation)", str(final["rides_with_hours"])))
        rows.append(("Rides with blank schedules (final observation)", str(final["rides_without_hours"])))
        if final["rides_unreadable"]:
            rows.append(("Rides with unreadable hours (final observation)", str(final["rides_unreadable"])))
    changed_ids = {c["id"] for _, _, d in pairs for c in d["ride_changes"]}
    rows.append(("Rides whose published schedule changed", str(len(changed_ids))))
    if unreadable:
        rows.append(("Unreadable observation files (ignored)", str(len(unreadable))))
    w("| Item | Value |")
    w("|---|---|")
    for key, value in rows:
        w(f"| {_md(key)} | {_md(value)} |")
    w("")

    if failed:
        w("### Failed observations")
        w("")
        w("Failed or incomplete responses are never compared; the previous successful observation is kept.")
        w("")
        w("| Observed (ET) | Outcome | Detail |")
        w("|---|---|---|")
        for obs in failed:
            w(f"| {_et(obs)} | {_md(obs['outcome'])} | {_md(obs.get('detail') or '')} |")
        w("")

    w("## 2. Ride schedule changes")
    w("")
    ride_changes = [(a, b, c) for a, b, d in pairs for c in d["ride_changes"]]
    if not ride_changes:
        w(NO_CHANGE_SENTENCE)
        w("")
    else:
        w("Old seen = the last observation showing the old value. New seen = the first observation "
          "showing the new value. The change was published somewhere between the two.")
        w("")
        w("| Old seen (ET) | New seen (ET) | Ride | Old schedule | New schedule | Change |")
        w("|---|---|---|---|---|---|")
        for a, b, c in ride_changes:
            old_text = c.get("old_name") if c["kind"] == "ride_renamed" else _ride_schedule(c["old"])
            new_text = c.get("new_name") if c["kind"] == "ride_renamed" else _ride_schedule(c["new"])
            w(f"| {_et(a)} | {_et(b)} | {_md(c['name'])} (`{c['id']}`) | {_md(old_text)} | "
              f"{_md(new_text)} | {SCHEDULE_CHANGE_LABELS[c['kind']]} |")
        w("")
        w("### Changes grouped by ride")
        w("")
        by_ride: dict[str, list] = {}
        for a, b, c in ride_changes:
            by_ride.setdefault(c["id"], []).append((a, b, c))
        for ride_id in sorted(by_ride, key=lambda i: (by_ride[i][0][2]["name"] or "", i)):
            entries = by_ride[ride_id]
            w(f"#### {_md(entries[0][2]['name'])} (`{ride_id}`)")
            w("")
            for a, b, c in entries:
                w(f"- {_et(a)}: {_md(_ride_schedule(c['old']))}")
                w(f"  → {_et(b)}: {_md(_ride_schedule(c['new']))}")
                w(f"  - Change: {SCHEDULE_CHANGE_LABELS[c['kind']]}")
                w("  - Interpretation: FIMS changed the published schedule between these observations. "
                  "Actual downtime or closure is not established.")
            w("")

    quality = [(a, b, q) for a, b, d in pairs for q in d["data_quality"]]
    if quality:
        w("### Data-quality differences (not schedule changes)")
        w("")
        w("| Old seen (ET) | New seen (ET) | Ride | Old value | New value |")
        w("|---|---|---|---|---|")
        for a, b, q in quality:
            w(f"| {_et(a)} | {_et(b)} | {_md(q['name'])} (`{q['id']}`) | {_md(_ride_schedule(q['old']))} | "
              f"{_md(_ride_schedule(q['new']))} |")
        w("")

    w("## 3. Earliest and latest observed values per ride")
    w("")
    if not good:
        w("No successful observations.")
    else:
        w("| Ride | Official ID | First observed schedule | Last observed schedule | Changed |")
        w("|---|---|---|---|---|")
        first_seen: dict[str, dict] = {}
        last_seen: dict[str, dict] = {}
        for obs in good:
            for ride in obs.get("rides", []):
                first_seen.setdefault(ride["id"], ride)
                last_seen[ride["id"]] = ride
        for ride_id in sorted(last_seen, key=lambda i: ((last_seen[i]["name"] or "").lower(), i)):
            w(f"| {_md(last_seen[ride_id]['name'])} | `{ride_id}` | {_md(_ride_schedule(first_seen[ride_id]))} | "
              f"{_md(_ride_schedule(last_seen[ride_id]))} | {'Yes' if ride_id in changed_ids else 'No'} |")
    w("")

    w("## 4. Main gate and park-level changes")
    w("")
    park_level = [(a, b, d) for a, b, d in pairs if d["gate_changes"] or d["park_changes"]]
    if not park_level:
        w("No main-gate, water-park gate or park-closed changes were observed.")
    else:
        w("| Old seen (ET) | New seen (ET) | Item | Old | New |")
        w("|---|---|---|---|---|")
        for a, b, d in park_level:
            for p in d["park_changes"]:
                w(f"| {_et(a)} | {_et(b)} | Park closed flag | {p['old']} | {p['new']} |")
            for g in d["gate_changes"]:
                name = fc.GATE_NAMES.get(g["type_id"], str(g["type_id"]))
                w(f"| {_et(a)} | {_et(b)} | {name} gate (id {g['type_id']}) | {_md(_gate_text(g['old']))} | "
                  f"{_md(_gate_text(g['new']))} |")
    w("")

    w("## 5. Source ETag / Last-Modified behaviour")
    w("")
    if not good:
        w("No successful observations.")
    else:
        etags = [o.get("etag") for o in good]
        distinct = len({e for e in etags if e})
        meta = [m for _, _, d in pairs for m in d["metadata"] if m["header"] == "etag"]
        etag_only = sum(1 for m in meta if m["schedule_identical"])
        w(f"- Distinct ETags across successful observations: {distinct}.")
        w(f"- ETag changes between consecutive observations: {len(meta)}; of those, {etag_only} "
          "with ride, gate and park data identical (source file rewritten without a schedule change).")
        lm_only = sum(1 for _, _, d in pairs
                      if any(m["header"] == "last_modified" for m in d["metadata"])
                      and not any(m["header"] == "etag" for m in d["metadata"]))
        w(f"- Last-Modified changes with the ETag unchanged: {lm_only} (file re-saved with identical content).")
        w("")
        w("| Observed (ET) | ETag | Last-Modified |")
        w("|---|---|---|")
        for obs in good:
            w(f"| {_et(obs)} | {_md(obs.get('etag') or '—')} | {_md(obs.get('last_modified') or '—')} |")
    w("")

    w("## 6. First vs final observation, after closing, and later historical lookup")
    w("")
    if analysis["first"] is None:
        w("No successful observations.")
    else:
        first = analysis["first"]
        same = _schedule_signature(first) == _schedule_signature(final)
        w(f"- Final observation ({_et(final)}) vs first ({_et(first)}): "
          + ("**identical** published schedule." if same else "**different** published schedule (see section 2)."))
        close = _main_gate_close(final)
        if close is None:
            w("- After closing: main-gate closing time not published, so post-closing stability cannot be assessed.")
        else:
            after = [o for o in good if o["_observed"] > close]
            late_changes = [c for a, b, d in pairs if b["_observed"] > close for c in d["ride_changes"]]
            if not after:
                w(f"- After closing ({fc.format_eastern(close)}): no observation was made after the published "
                  "main-gate closing time.")
            else:
                w(f"- After closing ({fc.format_eastern(close)}): {len(after)} observation(s); "
                  f"{len(late_changes)} ride schedule change(s) first seen after closing.")
        if not analysis["lookups"]:
            w("- Later historical lookup: none recorded. Run `python monitor.py snapshot --date "
              f"{operating_date.isoformat()}` on a later day (after 6:00 AM ET) and re-run this report.")
        for lookup, matches in analysis["lookups"]:
            if matches is None:
                w(f"- Later historical lookup {_et(lookup)}: not comparable ({lookup.get('outcome')}: "
                  f"{_md(lookup.get('detail') or '')}).")
            else:
                w(f"- Later historical lookup {_et(lookup)}: "
                  + ("**matches** the final study observation." if matches
                     else "**differs** from the final study observation."))
    w("")

    w("## 7. User-reported operational observations (not from FIMS)")
    w("")
    if not notes:
        w("None recorded. Add with `python notes.py add` (see README). These are kept separate from "
          "official data and are never used to classify FIMS changes.")
    else:
        w("Source for every row: user-reported operational observation. Not verified against FIMS, and not "
          "linked to any schedule change automatically.")
        w("")
        w("| Ride | Date | Approx. time (ET) | Description | Recorded |")
        w("|---|---|---|---|---|")
        for note in notes:
            w(f"| {_md(note.get('ride'))} | {_md(note.get('date'))} | {_md(note.get('approximate_time'))} | "
              f"{_md(note.get('description'))} | {_md(note.get('recorded_utc'))} |")
    w("")

    w("## 8. Researcher's interpretation")
    w("")
    w("_Not generated. Written by a person after reviewing sections 2–7._")
    w("")

    w("## 9. Limitations")
    w("")
    w("- FIMS publishes a scheduled plan. It has no live ride status, downtime, delay or reason field.")
    w("- A blank schedule means no hours were listed; it does not prove the ride was closed.")
    w("- Changes are only detected at sample times; anything published and reverted between two "
      "observations is invisible, and the change time is only known to lie between them.")
    w("- A schedule change alone is not evidence of downtime. That needs independent operational evidence.")
    w("- Failed or incomplete responses are excluded from comparison and listed above.")
    if unreadable:
        w("- Unreadable observation files were ignored: " + "; ".join(_md(u) for u in unreadable))
    w("")
    return "\n".join(out)


def load_notes(output_dir: Path, operating_date: dt.date) -> list[dict]:
    path = fc.study_dir(output_dir, operating_date) / "user-observations.json"
    if not path.is_file():
        return []
    data = json.loads(path.read_text(encoding="utf-8"))
    return list(data.get("observations", [])) if data.get("schema") == fc.NOTES_SCHEMA else []


def build_report(output_dir: Path, operating_date: dt.date, now=fc.utc_now) -> tuple[str, Path]:
    loaded = fc.load_observations(output_dir, operating_date)
    analysis = analyze(loaded.observations, operating_date)
    text = render_report(operating_date, analysis, loaded.unreadable, load_notes(output_dir, operating_date), now())
    path = fc.study_dir(output_dir, operating_date) / f"report-{operating_date.isoformat()}.md"
    fc.write_text_atomic(path, text)
    return text, path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--date", required=True, help="operating date, YYYY-MM-DD")
    parser.add_argument("--output-dir", type=Path, default=fc.DEFAULT_OUTPUT_DIR)
    args = parser.parse_args(argv)
    try:
        operating_date = fc.parse_operating_date(args.date)
    except ValueError as exc:
        parser.error(str(exc))
    if not fc.study_dir(args.output_dir, operating_date).is_dir():
        print(f"No observations folder for {operating_date} under {args.output_dir}", file=sys.stderr)
        return 1
    _, path = build_report(args.output_dir, operating_date)
    print(f"Report written: {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
