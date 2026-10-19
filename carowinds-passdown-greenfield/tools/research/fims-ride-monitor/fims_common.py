"""Shared helpers for the Phase 22G.2 FIMS intraday ride-schedule research tool.

Standalone research code: standard library only, no dependency on the Carowinds Passdown
application, its database or its profile. Everything here is read-only with respect to the
official source and writes only under this tool's own output folder.

What the feed is: the public JSON file the official park-hours page loads
(docs/retrieval/park-hours-source.md). Ride rows are scheduled hours from the published plan.
Nothing in the feed reports live status or downtime, so nothing here ever classifies a ride as
"closed" or "down" -- changes are only ever "published schedule changes".
"""

from __future__ import annotations

import datetime as dt
import http.client
import json
import os
import re
import socket
import tempfile
from dataclasses import dataclass, field
from email.utils import parsedate_to_datetime
from pathlib import Path

# --- Source constants ----------------------------------------------------------------------------

PARK_ID = 30  # Carowinds; every response echoes "parkId":30.
SOURCE_HOST = "d3ln8b3jj5b2pq.cloudfront.net"
SOURCE_PATH = "/operating-hours/park/{park_id}?date={yyyymmdd}"
RIDES_VENUE_ID = 1
MAIN_GATE_TYPE_ID = 24
WATERPARK_TYPE_ID = 70
GATE_NAMES = {MAIN_GATE_TYPE_ID: "Park", WATERPARK_TYPE_ID: "Waterpark"}

USER_AGENT = "CarowindsPassdown-FIMSRideScheduleResearch/1.0 (read-only research script; hourly sampling)"
CONNECT_TIMEOUT_SECONDS = 10
READ_TIMEOUT_SECONDS = 30
MAX_RESPONSE_BYTES = 2 * 1024 * 1024  # a daily file is ~30 KB

SCHEMA = "fims-ride-observation/1"
NOTES_SCHEMA = "fims-user-observations/1"

TOOL_DIR = Path(__file__).resolve().parent
DEFAULT_OUTPUT_DIR = TOOL_DIR / "output"

_HHMM = re.compile(r"^([01]\d|2[0-3]):[0-5]\d$")


# --- Eastern time ----------------------------------------------------------------------------------


class _UsEasternFallback(dt.tzinfo):
    """America/New_York for 2007-onward US DST rules.

    Used only when the zoneinfo database is unavailable (stock Windows Python has no tz database
    unless the third-party `tzdata` package is installed). DST runs from 2:00 AM local on the
    second Sunday of March to 2:00 AM local on the first Sunday of November.
    """

    _STD = dt.timedelta(hours=-5)
    _DST = dt.timedelta(hours=-4)

    @staticmethod
    def _nth_sunday(year: int, month: int, n: int) -> dt.date:
        first = dt.date(year, month, 1)
        offset = (6 - first.weekday()) % 7
        return first + dt.timedelta(days=offset + 7 * (n - 1))

    def _dst_bounds_utc(self, year: int) -> tuple[dt.datetime, dt.datetime]:
        start = dt.datetime.combine(self._nth_sunday(year, 3, 2), dt.time(7, 0))  # 2 AM EST
        end = dt.datetime.combine(self._nth_sunday(year, 11, 1), dt.time(6, 0))  # 2 AM EDT
        return start, end

    def _is_dst_utc(self, naive_utc: dt.datetime) -> bool:
        start, end = self._dst_bounds_utc(naive_utc.year)
        return start <= naive_utc < end

    def utcoffset(self, d: dt.datetime | None) -> dt.timedelta:
        if d is None:
            return self._STD
        # Local wall time -> pick the offset; ambiguous fall-back hour resolves by `fold`.
        naive = d.replace(tzinfo=None)
        for offset in ((self._STD, self._DST) if d.fold else (self._DST, self._STD)):
            if self._is_dst_utc(naive - offset) == (offset == self._DST):
                return offset
        return self._STD  # nonexistent spring-forward hour

    def dst(self, d: dt.datetime | None) -> dt.timedelta:
        return self.utcoffset(d) - self._STD

    def tzname(self, d: dt.datetime | None) -> str:
        return "EDT" if self.dst(d) else "EST"

    def fromutc(self, d: dt.datetime) -> dt.datetime:
        naive = d.replace(tzinfo=None)
        dst = self._is_dst_utc(naive)
        local = naive + (self._DST if dst else self._STD)
        # Second occurrence of the repeated hour after fall-back gets fold=1.
        fold = 0
        if not dst:
            _, end = self._dst_bounds_utc(naive.year)
            if end <= naive < end + dt.timedelta(hours=1):
                fold = 1
        return local.replace(tzinfo=self, fold=fold)


def _load_eastern() -> dt.tzinfo:
    try:
        from zoneinfo import ZoneInfo

        return ZoneInfo("America/New_York")
    except Exception:  # ZoneInfoNotFoundError on Windows without tzdata, or no zoneinfo module
        return _UsEasternFallback()


EASTERN = _load_eastern()
EASTERN_FALLBACK = _UsEasternFallback()


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def to_eastern(moment: dt.datetime) -> dt.datetime:
    if moment.tzinfo is None:
        raise ValueError("timestamp must be timezone-aware")
    return moment.astimezone(EASTERN)


def iso_utc(moment: dt.datetime) -> str:
    return moment.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_iso(text: str) -> dt.datetime:
    value = dt.datetime.fromisoformat(text.replace("Z", "+00:00"))
    if value.tzinfo is None:
        raise ValueError(f"timestamp without offset: {text}")
    return value


def format_eastern(moment: dt.datetime) -> str:
    """e.g. '2026-10-24 3:05 PM EDT'."""
    local = to_eastern(moment)
    hour = local.hour % 12 or 12
    return f"{local:%Y-%m-%d} {hour}:{local:%M} {'AM' if local.hour < 12 else 'PM'} {local.tzname()}"


def parse_operating_date(text: str) -> dt.date:
    try:
        return dt.date.fromisoformat(text)
    except ValueError as exc:
        raise ValueError(f"operating date must be YYYY-MM-DD, got {text!r}") from exc


# --- Schedule values ---------------------------------------------------------------------------------


def is_hhmm(value: object) -> bool:
    return isinstance(value, str) and bool(_HHMM.match(value))


def format_clock(hhmm: str) -> str:
    hour, minute = int(hhmm[:2]), hhmm[3:]
    return f"{hour % 12 or 12}:{minute} {'AM' if hour < 12 else 'PM'}"


def crosses_midnight(time_from: str, time_to: str) -> bool:
    """FIMS writes closings after midnight as small hours ('00:00', '01:00')."""
    return time_to <= time_from


def format_schedule(ride: dict) -> str:
    state = ride.get("state")
    if state == "populated":
        text = f"{format_clock(ride['from'])} – {format_clock(ride['to'])}"
        if crosses_midnight(ride["from"], ride["to"]):
            text += " (next day)"
        return text
    if state == "blank":
        return "No scheduled hours listed"
    if state == "missing_fields":
        return "Time fields absent from response"
    return f"Unreadable hours ({ride.get('from')!r} – {ride.get('to')!r})"


def classify_hours(row: dict) -> tuple[str, str | None, str | None]:
    """Return (state, from, to) for one detailHours row.

    populated: both times are valid HH:mm. blank: both present and empty.
    missing_fields: either key absent/null. invalid: anything else (one blank, bad format).
    Only populated/blank are schedule facts; the other two are data-quality states.
    """
    has_from, has_to = "operatingTimeFrom" in row, "operatingTimeTo" in row
    time_from, time_to = row.get("operatingTimeFrom"), row.get("operatingTimeTo")
    if not has_from or not has_to or time_from is None or time_to is None:
        return "missing_fields", time_from, time_to
    if time_from == "" and time_to == "":
        return "blank", "", ""
    if is_hhmm(time_from) and is_hhmm(time_to):
        return "populated", time_from, time_to
    return "invalid", time_from, time_to


# --- Fetching -------------------------------------------------------------------------------------


def source_url(operating_date: dt.date) -> str:
    return "https://" + SOURCE_HOST + SOURCE_PATH.format(park_id=PARK_ID, yyyymmdd=f"{operating_date:%Y%m%d}")


@dataclass
class FetchResult:
    url: str
    status: int | None  # None when no HTTP response was received
    body: bytes = b""
    etag: str | None = None
    last_modified: str | None = None
    retry_after: str | None = None
    error: str | None = None  # network-level failure description


def fetch_once(operating_date: dt.date) -> FetchResult:
    """One plain GET. No cookies, no auth, no redirects followed, no retries."""
    url = source_url(operating_date)
    path = SOURCE_PATH.format(park_id=PARK_ID, yyyymmdd=f"{operating_date:%Y%m%d}")
    conn = http.client.HTTPSConnection(SOURCE_HOST, timeout=CONNECT_TIMEOUT_SECONDS)
    try:
        conn.connect()
        conn.sock.settimeout(READ_TIMEOUT_SECONDS)
        conn.request("GET", path, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
        response = conn.getresponse()
        body = response.read(MAX_RESPONSE_BYTES + 1)
        result = FetchResult(
            url=url,
            status=response.status,
            body=body,
            etag=response.getheader("ETag"),
            last_modified=response.getheader("Last-Modified"),
            retry_after=response.getheader("Retry-After"),
        )
        if len(body) > MAX_RESPONSE_BYTES:
            result.body = b""
            result.error = f"response larger than {MAX_RESPONSE_BYTES} bytes; discarded"
        return result
    except (socket.timeout, TimeoutError):
        return FetchResult(url=url, status=None, error="timeout")
    except (OSError, http.client.HTTPException) as exc:
        return FetchResult(url=url, status=None, error=f"{type(exc).__name__}: {exc}")
    finally:
        conn.close()


def retry_after_seconds(value: str | None, now: dt.datetime) -> float | None:
    """Retry-After as seconds (delta-seconds or HTTP-date); None if absent/unparseable."""
    if not value:
        return None
    value = value.strip()
    if value.isdigit():
        return float(value)
    try:
        when = parsedate_to_datetime(value)
    except (TypeError, ValueError):
        return None
    if when.tzinfo is None:
        when = when.replace(tzinfo=dt.timezone.utc)
    return max(0.0, (when - now).total_seconds())


# --- Normalizing ----------------------------------------------------------------------------------

# Outcomes. Only "ok" and "park_closed" are comparable schedule observations.
COMPARABLE_OUTCOMES = {"ok", "park_closed"}


def _gate_entries(day: dict) -> list[dict]:
    gates = []
    operatings = day.get("operatings")
    if not isinstance(operatings, list):
        return gates
    for type_id, name in GATE_NAMES.items():
        match = next((o for o in operatings if isinstance(o, dict) and o.get("operatingTypeId") == type_id), None)
        if match is None:  # label fallback for an entry without an id (Phase 22E.1 rule)
            match = next(
                (o for o in operatings if isinstance(o, dict) and o.get("operatingTypeId") is None
                 and o.get("operatingTypeName") == name),
                None,
            )
        if match is None:
            continue
        items = match.get("items") if isinstance(match.get("items"), list) else []
        item = items[0] if items and isinstance(items[0], dict) else {}
        gates.append({
            "type_id": type_id,
            "label": match.get("operatingTypeName"),
            "from": item.get("timeFrom") or None,
            "to": item.get("timeTo") or None,
        })
    return gates


def normalize(fetch: FetchResult, operating_date: dt.date, observed_utc: dt.datetime) -> dict:
    """Turn one fetch into a compact observation. Never raises on bad source data."""
    obs = {
        "schema": SCHEMA,
        "operating_date": operating_date.isoformat(),
        "observed_utc": iso_utc(observed_utc),
        "observed_eastern": to_eastern(observed_utc).isoformat(timespec="seconds"),
        "source_url": fetch.url,
        "http_status": fetch.status,
        "etag": fetch.etag,
        "last_modified": fetch.last_modified,
        "outcome": None,
        "comparable": False,
        "detail": None,
        "park_closed": None,
        "gate": [],
        "ride_venue_present": None,
        "ride_count": 0,
        "rides_with_hours": 0,
        "rides_without_hours": 0,
        "rides_unreadable": 0,
        "duplicate_ids": [],
        "warnings": [],
        "rides": [],
    }

    def fail(outcome: str, detail: str) -> dict:
        obs["outcome"], obs["detail"] = outcome, detail
        return obs

    if fetch.status is None:
        return fail("network_error", fetch.error or "no response")
    if fetch.error:
        return fail("invalid_response", fetch.error)
    if fetch.status == 429:
        return fail("rate_limited", f"HTTP 429; Retry-After={fetch.retry_after!r}")
    if fetch.status >= 500:
        return fail("http_error", f"HTTP {fetch.status}")
    if fetch.status == 404:
        return fail("not_found", "HTTP 404: the source has no file for this date")
    if fetch.status != 200:
        return fail("http_error", f"HTTP {fetch.status}")

    try:
        root = json.loads(fetch.body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        return fail("invalid_response", f"malformed JSON: {exc}")
    if not isinstance(root, dict):
        return fail("invalid_response", "JSON root is not an object")
    if root.get("error"):
        return fail("not_found" if root.get("error") == "Not Found" else "invalid_response",
                    f"error body: {root.get('error')!r}")
    if root.get("parkId") != PARK_ID:
        return fail("invalid_response", f"parkId {root.get('parkId')!r} is not {PARK_ID}")
    dates = root.get("dates")
    if not isinstance(dates, list):
        return fail("invalid_response", "no dates array")
    wanted = f"{operating_date:%m/%d/%Y}"
    days = [d for d in dates if isinstance(d, dict) and d.get("date") == wanted]
    if not days:
        return fail("incomplete", f"requested date {wanted} not in response (got {[d.get('date') for d in dates if isinstance(d, dict)]})")
    if len(days) > 1:
        return fail("invalid_response", f"requested date {wanted} appears {len(days)} times")
    day = days[0]

    closed = day.get("isParkClosed")
    if not isinstance(closed, bool):
        return fail("invalid_response", "isParkClosed missing or not boolean")
    obs["park_closed"] = closed
    obs["gate"] = _gate_entries(day)

    venues = day.get("venues")
    if not isinstance(venues, list):
        return fail("incomplete", "venues missing or not a list")
    ride_venues = [v for v in venues if isinstance(v, dict) and v.get("venueId") == RIDES_VENUE_ID]
    obs["ride_venue_present"] = bool(ride_venues)

    if closed:
        if venues:
            obs["warnings"].append("park closed but venues listed; ride rows ignored")
        obs["outcome"], obs["comparable"] = "park_closed", True
        return obs
    if not ride_venues:
        return fail("incomplete", "open day without the Rides venue (venueId 1)")
    if len(ride_venues) > 1:
        return fail("invalid_response", "Rides venue (venueId 1) appears more than once")
    rows = ride_venues[0].get("detailHours")
    if not isinstance(rows, list) or not rows:
        return fail("incomplete", "Rides venue has an empty or missing detailHours list")

    by_id: dict[str, dict] = {}
    conflicting: set[str] = set()
    for row in rows:
        if not isinstance(row, dict):
            obs["warnings"].append("non-object ride row skipped")
            continue
        ride_id = row.get("extLocationID")
        if not isinstance(ride_id, str) or not ride_id.strip():
            obs["warnings"].append(f"ride row without extLocationID skipped ({row.get('itemName')!r})")
            continue
        state, time_from, time_to = classify_hours(row)
        ride = {"id": ride_id, "name": row.get("itemName"), "from": time_from, "to": time_to,
                "populated": state == "populated" if state in ("populated", "blank") else None,
                "state": state}
        if ride_id in by_id:
            if by_id[ride_id] != ride:
                conflicting.add(ride_id)
            else:
                obs["warnings"].append(f"identical duplicate row for {ride_id} collapsed")
            continue
        by_id[ride_id] = ride

    for ride_id in sorted(conflicting):
        by_id[ride_id] = {"id": ride_id, "name": by_id[ride_id]["name"], "from": None, "to": None,
                          "populated": None, "state": "conflicting_duplicate"}
    if not by_id:
        return fail("incomplete", "no usable ride rows")

    rides = [by_id[k] for k in sorted(by_id)]
    obs["rides"] = rides
    obs["duplicate_ids"] = sorted(conflicting)
    obs["ride_count"] = len(rides)
    obs["rides_with_hours"] = sum(1 for r in rides if r["state"] == "populated")
    obs["rides_without_hours"] = sum(1 for r in rides if r["state"] == "blank")
    obs["rides_unreadable"] = len(rides) - obs["rides_with_hours"] - obs["rides_without_hours"]
    obs["outcome"], obs["comparable"] = "ok", True
    return obs


# --- Storage ----------------------------------------------------------------------------------------


def study_dir(output_dir: Path, operating_date: dt.date) -> Path:
    return Path(output_dir) / operating_date.isoformat()


def _atomic_create(path: Path, text: str) -> Path:
    """Write text to a new file at `path` (or a suffixed sibling); never overwrites."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".tmp-", suffix=".part", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        candidate, n = path, 1
        while True:
            try:
                os.link(tmp, candidate)  # atomic, fails if candidate exists
                return candidate
            except FileExistsError:
                candidate = path.with_name(f"{path.stem}-{n}{path.suffix}")
                n += 1
            except OSError:
                # Filesystem without hard links: fall back to exists-check + atomic rename.
                while candidate.exists():
                    candidate = path.with_name(f"{path.stem}-{n}{path.suffix}")
                    n += 1
                os.replace(tmp, candidate)
                return candidate
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def save_observation(output_dir: Path, obs: dict) -> Path:
    operating_date = parse_operating_date(obs["operating_date"])
    stamp = parse_iso(obs["observed_utc"]).strftime("%Y%m%dT%H%M%SZ")
    target = study_dir(output_dir, operating_date) / f"obs-{stamp}.json"
    return _atomic_create(target, json.dumps(obs, indent=1, ensure_ascii=False) + "\n")


def write_text_atomic(path: Path, text: str) -> None:
    """Atomic replace for derived files (reports) that may be regenerated."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".tmp-", suffix=".part", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


@dataclass
class LoadedStudy:
    observations: list[dict] = field(default_factory=list)  # sorted by observed_utc
    unreadable: list[str] = field(default_factory=list)


def load_observations(output_dir: Path, operating_date: dt.date) -> LoadedStudy:
    folder = study_dir(output_dir, operating_date)
    loaded = LoadedStudy()
    if not folder.is_dir():
        return loaded
    for path in folder.glob("obs-*.json"):
        try:
            obs = json.loads(path.read_text(encoding="utf-8"))
            if obs.get("schema") != SCHEMA or obs.get("operating_date") != operating_date.isoformat():
                raise ValueError("schema or date mismatch")
            obs["_observed"] = parse_iso(obs["observed_utc"])
            obs["_file"] = path.name
            loaded.observations.append(obs)
        except (OSError, ValueError, KeyError, TypeError) as exc:
            loaded.unreadable.append(f"{path.name}: {exc}")
    # Order by the recorded UTC timestamp, never by filename or Eastern wall time.
    loaded.observations.sort(key=lambda o: (o["_observed"], o["_file"]))
    return loaded
