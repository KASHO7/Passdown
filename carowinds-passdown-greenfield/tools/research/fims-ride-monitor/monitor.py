#!/usr/bin/env python3
"""Read-only sampler for the official Carowinds FIMS ride schedule (Phase 22G.2 research).

  python monitor.py snapshot --date 2026-10-24
  python monitor.py watch --date 2026-10-24 --interval-minutes 60 --end-at 23:30

One plain GET of one operating date per observation. Interval 30-120 minutes (default 60). A watch
stops on its own at --end-at / --duration-hours (max 16 h), after 3 consecutive failed
observations, or on Ctrl+C. No background services, no parallel requests, no immediate retries:
the next scheduled sample is the only retry.
"""

from __future__ import annotations

import argparse
import datetime as dt
import sys
import time
from pathlib import Path
from typing import Callable

import fims_common as fc

MIN_INTERVAL_MINUTES = 30
MAX_INTERVAL_MINUTES = 120
DEFAULT_INTERVAL_MINUTES = 60
MAX_DURATION_HOURS = 16.0
MAX_CONSECUTIVE_FAILURES = 3


def observe(operating_date: dt.date, output_dir: Path,
            fetch: Callable[[dt.date], fc.FetchResult] = fc.fetch_once,
            now: Callable[[], dt.datetime] = fc.utc_now) -> tuple[dict, Path, fc.FetchResult]:
    """One request -> one saved observation (failures are saved too, marked not comparable)."""
    observed = now()
    result = fetch(operating_date)
    obs = fc.normalize(result, operating_date, observed)
    path = fc.save_observation(output_dir, obs)
    return obs, path, result


def describe(obs: dict) -> str:
    when = fc.format_eastern(fc.parse_iso(obs["observed_utc"]))
    if obs["outcome"] == "ok":
        gate = next((g for g in obs["gate"] if g["type_id"] == fc.MAIN_GATE_TYPE_ID), None)
        gate_text = f"{gate['from']}-{gate['to']}" if gate and gate["from"] else "not listed"
        return (f"{when}: OK - gate {gate_text}; {obs['ride_count']} rides listed, "
                f"{obs['rides_with_hours']} with scheduled hours, {obs['rides_without_hours']} blank"
                + (f", {obs['rides_unreadable']} unreadable" if obs["rides_unreadable"] else ""))
    if obs["outcome"] == "park_closed":
        return f"{when}: OK - park listed as closed for this date"
    return f"{when}: FAILED ({obs['outcome']}) - {obs['detail']}"


def resolve_end(start_utc: dt.datetime, operating_date: dt.date, end_at: str | None,
                duration_hours: float) -> dt.datetime:
    """End of the watch window: the earlier of start+duration and --end-at (Eastern)."""
    end = start_utc + dt.timedelta(hours=duration_hours)
    if end_at:
        if not fc.is_hhmm(end_at):
            raise ValueError("--end-at must be HH:MM (24-hour, Eastern)")
        hour, minute = int(end_at[:2]), int(end_at[3:])
        local = dt.datetime.combine(operating_date, dt.time(hour, minute), tzinfo=fc.EASTERN)
        candidate = local.astimezone(dt.timezone.utc)
        if candidate <= start_utc:  # e.g. --end-at 00:30 means just after midnight
            local = dt.datetime.combine(operating_date + dt.timedelta(days=1), dt.time(hour, minute),
                                        tzinfo=fc.EASTERN)
            candidate = local.astimezone(dt.timezone.utc)
        end = min(end, candidate)
    if end <= start_utc:
        raise ValueError("monitoring window has already ended")
    return end


def watch(operating_date: dt.date, output_dir: Path, interval_minutes: int, end_utc: dt.datetime,
          fetch: Callable[[dt.date], fc.FetchResult] = fc.fetch_once,
          now: Callable[[], dt.datetime] = fc.utc_now,
          sleep: Callable[[float], None] = time.sleep,
          log: Callable[[str], None] = print) -> dict:
    """Sample until end_utc. Returns a summary; KeyboardInterrupt propagates to the caller."""
    interval = dt.timedelta(minutes=interval_minutes)
    summary = {"successful": 0, "failed": 0, "stopped_because": None}
    failures_in_a_row = 0
    next_at = now()
    while True:
        current = now()
        if next_at >= end_utc or current >= end_utc:
            summary["stopped_because"] = "end of monitoring window"
            return summary
        if next_at > current:
            sleep((next_at - current).total_seconds())
            continue
        obs, path, result = observe(operating_date, output_dir, fetch, now)
        log(f"{describe(obs)}  [{path.name}]")
        scheduled = next_at + interval
        if obs["comparable"]:
            summary["successful"] += 1
            failures_in_a_row = 0
        else:
            summary["failed"] += 1
            failures_in_a_row += 1
            if failures_in_a_row >= MAX_CONSECUTIVE_FAILURES:
                summary["stopped_because"] = f"{MAX_CONSECUTIVE_FAILURES} consecutive failed observations"
                return summary
            if obs["outcome"] == "rate_limited":
                wait = fc.retry_after_seconds(result.retry_after, now())
                if wait is not None:
                    honoured = now() + dt.timedelta(seconds=wait)
                    if honoured >= end_utc:
                        summary["stopped_because"] = "Retry-After extends past the monitoring window"
                        return summary
                    scheduled = max(scheduled, honoured)
        # Never schedule a catch-up burst: the next sample is at least one interval away from now.
        current = now()
        next_at = scheduled if scheduled > current else current + interval


def _interval(value: str) -> int:
    minutes = int(value)
    if not MIN_INTERVAL_MINUTES <= minutes <= MAX_INTERVAL_MINUTES:
        raise argparse.ArgumentTypeError(
            f"interval must be between {MIN_INTERVAL_MINUTES} and {MAX_INTERVAL_MINUTES} minutes")
    return minutes


def _duration(value: str) -> float:
    hours = float(value)
    if not 0 < hours <= MAX_DURATION_HOURS:
        raise argparse.ArgumentTypeError(f"duration must be more than 0 and at most {MAX_DURATION_HOURS:g} hours")
    return hours


def _date(value: str) -> dt.date:
    try:
        return fc.parse_operating_date(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(str(exc)) from exc


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output-dir", type=Path, default=fc.DEFAULT_OUTPUT_DIR,
                        help="research output folder (default: ./output next to this script)")
    sub = parser.add_subparsers(dest="command", required=True)

    snap = sub.add_parser("snapshot", help="one request, one saved observation")
    snap.add_argument("--date", type=_date, required=True, help="operating date, YYYY-MM-DD")

    w = sub.add_parser("watch", help="repeated observations within a bounded window")
    w.add_argument("--date", type=_date, required=True, help="operating date, YYYY-MM-DD")
    w.add_argument("--interval-minutes", type=_interval, default=DEFAULT_INTERVAL_MINUTES,
                   help=f"{MIN_INTERVAL_MINUTES}-{MAX_INTERVAL_MINUTES}, default {DEFAULT_INTERVAL_MINUTES}")
    w.add_argument("--end-at", help="stop at this Eastern time, HH:MM 24-hour (an earlier time means after midnight)")
    w.add_argument("--duration-hours", type=_duration, default=MAX_DURATION_HOURS,
                   help=f"maximum run time, default and maximum {MAX_DURATION_HOURS:g}")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "snapshot":
        obs, path, _ = observe(args.date, args.output_dir)
        print(describe(obs))
        print(f"Saved {path}")
        return 0 if obs["comparable"] else 1

    today_eastern = fc.to_eastern(fc.utc_now()).date()
    if abs((args.date - today_eastern).days) > 1:
        print(f"Note: {args.date} is not today ({today_eastern}) in Eastern time; "
              "intraday changes are only meaningful on the operating day.")
    start = fc.utc_now()
    try:
        end = resolve_end(start, args.date, args.end_at, args.duration_hours)
    except ValueError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2
    print(f"Watching {args.date} every {args.interval_minutes} min until {fc.format_eastern(end)}. "
          "Press Ctrl+C to stop.")
    try:
        summary = watch(args.date, args.output_dir, args.interval_minutes, end)
    except KeyboardInterrupt:
        print("\nStopped by Ctrl+C. Saved observations are kept.")
        return 130
    print(f"Stopped: {summary['stopped_because']}. "
          f"{summary['successful']} successful, {summary['failed']} failed observation(s).")
    print(f"Next: python compare.py --date {args.date}")
    return 0 if summary["stopped_because"] == "end of monitoring window" else 1


if __name__ == "__main__":
    sys.exit(main())
