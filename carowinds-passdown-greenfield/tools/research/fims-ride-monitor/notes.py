#!/usr/bin/env python3
"""Record independent, user-reported operational observations for a study date.

  python notes.py add --date 2026-10-24 --ride "Copperhead Strike" --time 15:10 \
      --description "Ride observed not loading guests for about 20 minutes"
  python notes.py list --date 2026-10-24

Stored separately from official FIMS observations (output/<date>/user-observations.json) and
never used to classify FIMS data. Do not enter guest identities, complaint contents, employee
names or private operational documents.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import fims_common as fc

SOURCE_LABEL = "user-reported operational observation"
MAX_DESCRIPTION = 300


def notes_path(output_dir: Path, operating_date) -> Path:
    return fc.study_dir(output_dir, operating_date) / "user-observations.json"


def read_notes(output_dir: Path, operating_date) -> dict:
    path = notes_path(output_dir, operating_date)
    if path.is_file():
        data = json.loads(path.read_text(encoding="utf-8"))
        if data.get("schema") != fc.NOTES_SCHEMA:
            raise ValueError(f"{path} is not a user-observations file")
        return data
    return {"schema": fc.NOTES_SCHEMA, "operating_date": operating_date.isoformat(),
            "note": "User-reported operational observations. Not official FIMS data.", "observations": []}


def add_note(output_dir: Path, operating_date, ride: str, time_hhmm: str, description: str,
             now=fc.utc_now) -> dict:
    if not ride.strip():
        raise ValueError("ride is required")
    if not fc.is_hhmm(time_hhmm):
        raise ValueError("time must be HH:MM, 24-hour Eastern")
    description = " ".join(description.split())
    if not description or len(description) > MAX_DESCRIPTION:
        raise ValueError(f"description is required and at most {MAX_DESCRIPTION} characters")
    data = read_notes(output_dir, operating_date)
    entry = {"ride": ride.strip(), "date": operating_date.isoformat(),
             "approximate_time": f"{fc.format_clock(time_hhmm)} ET (approx.)",
             "description": description, "source": SOURCE_LABEL, "recorded_utc": fc.iso_utc(now())}
    data["observations"].append(entry)
    fc.write_text_atomic(notes_path(output_dir, operating_date),
                         json.dumps(data, indent=1, ensure_ascii=False) + "\n")
    return entry


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output-dir", type=Path, default=fc.DEFAULT_OUTPUT_DIR)
    sub = parser.add_subparsers(dest="command", required=True)
    add = sub.add_parser("add")
    add.add_argument("--date", required=True)
    add.add_argument("--ride", required=True)
    add.add_argument("--time", required=True, help="approximate Eastern time, HH:MM 24-hour")
    add.add_argument("--description", required=True)
    lst = sub.add_parser("list")
    lst.add_argument("--date", required=True)
    args = parser.parse_args(argv)
    try:
        operating_date = fc.parse_operating_date(args.date)
        if args.command == "add":
            entry = add_note(args.output_dir, operating_date, args.ride, args.time, args.description)
            print(f"Recorded ({SOURCE_LABEL}): {entry['ride']} {entry['approximate_time']} — {entry['description']}")
        else:
            for entry in read_notes(args.output_dir, operating_date)["observations"]:
                print(f"{entry['ride']} | {entry['approximate_time']} | {entry['description']}")
    except ValueError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
