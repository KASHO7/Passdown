"""Synthetic FIMS fixtures and test plumbing. All data here is illustrative, not observed."""

import datetime as dt
import http.client
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import fims_common as fc  # noqa: E402


class _NoNetwork:
    def __init__(self, *args, **kwargs):
        raise AssertionError("tests must not open network connections")


http.client.HTTPSConnection = _NoNetwork  # every HTTP response in tests is mocked

DATE = dt.date(2026, 10, 24)
UTC = dt.timezone.utc


def ride(ride_id, name, time_from="11:00", time_to="22:00"):
    return {"itemID": -1, "itemName": name, "extLocationID": ride_id,
            "operatingTimeFrom": time_from, "operatingTimeTo": time_to}


def default_rides():
    return [
        ride("RIDE-030-00098", "Copperhead Strike"),
        ride("RIDE-030-00072", "Fury 325", "", ""),
        ride("RIDE-030-00055", "Woodstock Express", "11:00", "18:00"),
    ]


def day(rides=None, closed=False, gate=("11:00", "22:00"), date=DATE, include_rides_venue=True, extra_venues=True):
    venues = []
    if not closed:
        if include_rides_venue:
            venues.append({"venueId": 1, "venueName": "Rides", "assignmentHours": "",
                           "detailHours": default_rides() if rides is None else rides})
        if extra_venues:
            venues.append({"venueId": 4, "venueName": "Dining", "assignmentHours": "",
                           "detailHours": [{"itemID": -1, "itemName": "Example Grill",
                                            "extLocationID": "DINE-030-00001",
                                            "operatingTimeFrom": "11:00", "operatingTimeTo": "20:00"}]})
    operatings = [] if closed or gate is None else [
        {"operatingTypeId": 24, "operatingTypeName": "Park",
         "items": [{"timeFrom": gate[0], "timeTo": gate[1]}]}]
    return {"date": f"{date:%m/%d/%Y}", "isParkClosed": closed, "venues": venues, "shows": [],
            "events": [], "operatings": operatings}


def body(*days, park_id=30):
    return json.dumps({"parkId": park_id, "parkAbbreviation": "CA", "parkName": "Carowinds",
                       "dates": list(days)}).encode()


def ok(payload=None, etag='"e1"', last_modified="Sat, 24 Oct 2026 15:00:00 GMT", status=200, retry_after=None):
    return fc.FetchResult(url=fc.source_url(DATE), status=status,
                          body=body(day()) if payload is None else payload,
                          etag=etag, last_modified=last_modified, retry_after=retry_after)


def at(hour, minute=0, date=DATE):
    """Aware UTC datetime for an Eastern wall time on `date`."""
    return dt.datetime.combine(date, dt.time(hour, minute), tzinfo=fc.EASTERN).astimezone(UTC)


def obs_at(when, fetch=None):
    return fc.normalize(fetch or ok(), DATE, when)


class TempDirTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.out = self._tmp.name

    def tearDown(self):
        self._tmp.cleanup()
