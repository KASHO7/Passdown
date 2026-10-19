import datetime as dt
import json
import re
import unittest

import compare
import notes
from tests.helpers import DATE, UTC, TempDirTest, at, body, day, fc, ok, ride


def rides_with(**overrides):
    """Default three rides with per-ID (from, to) overrides; None removes the ride."""
    base = {"RIDE-030-00098": ("Copperhead Strike", "11:00", "22:00"),
            "RIDE-030-00072": ("Fury 325", "", ""),
            "RIDE-030-00055": ("Woodstock Express", "11:00", "18:00")}
    rows = []
    for ride_id, (name, f, t) in base.items():
        key = ride_id.replace("-", "_")
        if key in overrides:
            if overrides[key] is None:
                continue
            value = overrides[key]
            if len(value) == 3:
                name, f, t = value
            else:
                f, t = value
        rows.append(ride(ride_id, name, f, t))
    for key, value in overrides.items():
        ride_id = key.replace("_", "-")
        if ride_id not in base and value is not None:
            rows.append(ride(ride_id, *value))
    return rows


def obs(hour, rows=None, etag='"e1"', last_modified=None, gate=("11:00", "22:00"), closed=False, minute=0, fetch=None):
    fetch = fetch or ok(body(day(rows, gate=gate, closed=closed)), etag=etag,
                        last_modified=last_modified or f"Sat, 24 Oct 2026 {hour:02d}:00:00 GMT")
    o = fc.normalize(fetch, DATE, at(hour, minute) if hour < 24 else at(hour - 24, minute, DATE + dt.timedelta(days=1)))
    o["_observed"] = fc.parse_iso(o["observed_utc"])
    o["_file"] = f"{hour}"
    return o


def kinds(prev, cur):
    return sorted((c["kind"], c["id"]) for c in compare.compare_pair(prev, cur)["ride_changes"])


class ChangeDetectionTests(unittest.TestCase):
    def test_populated_to_blank(self):
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00098=("", "")))),
                         [("populated_to_blank", "RIDE-030-00098")])

    def test_blank_to_populated(self):
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00072=("12:00", "22:00")))),
                         [("blank_to_populated", "RIDE-030-00072")])

    def test_opening_and_closing_changes(self):
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00098=("12:00", "22:00")))),
                         [("opening_changed", "RIDE-030-00098")])
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00098=("11:00", "20:00")))),
                         [("closing_changed", "RIDE-030-00098")])
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00098=("12:00", "20:00")))),
                         [("closing_changed", "RIDE-030-00098"), ("opening_changed", "RIDE-030-00098")])

    def test_ride_added_removed_renamed(self):
        added = obs(15, rides_with(RIDE_030_00200=("New Ride", "11:00", "22:00")))
        self.assertEqual(kinds(obs(11), added), [("ride_added", "RIDE-030-00200")])
        self.assertEqual(kinds(obs(11), obs(15, rides_with(RIDE_030_00055=None))),
                         [("ride_removed", "RIDE-030-00055")])
        renamed = obs(15, rides_with(RIDE_030_00055=("Woodstock Express Coaster", "11:00", "18:00")))
        self.assertEqual(kinds(obs(11), renamed), [("ride_renamed", "RIDE-030-00055")])

    def test_reordered_rows_are_not_changes(self):
        self.assertEqual(kinds(obs(11), obs(15, list(reversed(rides_with())))), [])

    def test_no_change_and_etag_only_change(self):
        result = compare.compare_pair(obs(11), obs(15, etag='"e2"'))
        self.assertEqual(result["ride_changes"], [])
        etag = next(m for m in result["metadata"] if m["header"] == "etag")
        self.assertTrue(etag["schedule_identical"])
        self.assertEqual(compare.compare_pair(obs(11), obs(11, last_modified="Sat, 24 Oct 2026 11:00:00 GMT"))["metadata"], [])

    def test_gate_change_detected(self):
        result = compare.compare_pair(obs(11), obs(15, gate=("11:00", "20:00")))
        self.assertEqual(len(result["gate_changes"]), 1)
        self.assertEqual(result["ride_changes"], [])

    def test_park_closed_transition_does_not_remove_every_ride(self):
        result = compare.compare_pair(obs(11), obs(15, closed=True))
        self.assertEqual(result["park_changes"], [{"old": False, "new": True}])
        self.assertEqual(result["ride_changes"], [])

    def test_data_quality_difference_is_not_schedule_change(self):
        broken = obs(15, rides_with(RIDE_030_00098=("11:00", "")))
        result = compare.compare_pair(obs(11), broken)
        self.assertEqual(result["ride_changes"], [])
        self.assertEqual([q["id"] for q in result["data_quality"]], ["RIDE-030-00098"])


class AnalysisTests(unittest.TestCase):
    def test_failed_observation_is_skipped_and_previous_good_kept(self):
        failed = obs(13, fetch=fc.FetchResult(url="u", status=503, body=b"<html>"))
        incomplete = obs(14, fetch=ok(body(day(include_rides_venue=False))))
        observations = [obs(11), failed, incomplete, obs(15)]
        analysis = compare.analyze(observations, DATE)
        self.assertEqual((len(analysis["good"]), len(analysis["failed"])), (2, 2))
        self.assertEqual(len(analysis["pairs"]), 1)
        self.assertEqual(analysis["pairs"][0][2]["ride_changes"], [])

    def test_historical_lookup_separated_and_matched(self):
        final = obs(23)
        lookup = obs(24 + 9)  # 9 AM the next day
        early_next_day = obs(24 + 1)  # 1 AM next day still belongs to the study
        analysis = compare.analyze([obs(11), final, early_next_day, lookup], DATE)
        self.assertEqual(len(analysis["study"]), 3)
        self.assertEqual([m for _, m in analysis["lookups"]], [True])


class ReportTests(TempDirTest):
    def save(self, o):
        o = {k: v for k, v in o.items() if not k.startswith("_")}
        fc.save_observation(self.out, o)

    def report(self):
        now = lambda: dt.datetime(2026, 10, 25, 14, 0, tzinfo=UTC)
        text, path = compare.build_report(self.out, DATE, now=now)
        self.assertTrue(path.exists())
        return text

    def test_no_change_report_states_required_sentence(self):
        self.save(obs(11))
        self.save(obs(12, etag='"e2"'))
        text = self.report()
        self.assertIn(compare.NO_CHANGE_SENTENCE, text)
        self.assertIn("Successful observations | 2", text)
        self.assertIn("1 with ride, gate and park data identical", text)
        self.assertIn("Last-Modified changes with the ETag unchanged: 0", text)
        self.assertIn("Later historical lookup: none recorded", text)

    def test_last_modified_only_rewrite_counted(self):
        self.save(obs(11))
        self.save(obs(12))  # same ETag, new Last-Modified
        self.assertIn("Last-Modified changes with the ETag unchanged: 1", self.report())

    def test_change_report_table_and_grouping(self):
        self.save(obs(11))
        self.save(obs(13, fetch=fc.FetchResult(url="u", status=None, error="timeout")))
        self.save(obs(15, rides_with(RIDE_030_00098=("", ""))))
        text = self.report()
        self.assertIn("| 2026-10-24 11:00 AM EDT | 2026-10-24 3:00 PM EDT | Copperhead Strike (`RIDE-030-00098`) | "
                      "11:00 AM – 10:00 PM | No scheduled hours listed | Populated → blank |", text)
        self.assertIn("#### Copperhead Strike (`RIDE-030-00098`)", text)
        self.assertIn("Actual downtime or closure is not established.", text)
        self.assertIn("Failed observations | 1", text)
        self.assertIn("Rides whose published schedule changed | 1", text)
        self.assertIn("**different** published schedule", text)
        self.assertNotIn(compare.NO_CHANGE_SENTENCE, text)

    def test_report_never_asserts_downtime_or_closure(self):
        self.save(obs(11))
        self.save(obs(15, rides_with(RIDE_030_00098=("", ""), RIDE_030_00072=("12:00", "22:00"))))
        text = self.report().lower()
        for phrase in ("was down", "went down", "ride closed", "is closed", "downtime confirmed",
                       "confirmed closure", "breakdown"):
            self.assertNotIn(phrase, text)
        # Every mention of downtime is a disclaimer.
        for line in [l for l in text.splitlines() if "downtime" in l]:
            self.assertTrue(re.search(r"not establish|no live|not evidence|needs independent|does not", line), line)

    def test_user_notes_shown_separately_and_not_linked(self):
        self.save(obs(11))
        notes.add_note(self.out, DATE, "Copperhead Strike", "15:10", "Illustrative test note",
                       now=lambda: dt.datetime(2026, 10, 24, 19, 30, tzinfo=UTC))
        text = self.report()
        section = text.split("## 7.")[1].split("## 8.")[0]
        self.assertIn("user-reported operational observation", section)
        self.assertIn("3:10 PM ET (approx.)", section)
        self.assertIn(compare.NO_CHANGE_SENTENCE, text)  # a note never creates a change
        saved = json.loads((self.report_dir() / "user-observations.json").read_text())
        self.assertEqual(saved["observations"][0]["source"], "user-reported operational observation")

    def test_historical_lookup_mismatch_reported(self):
        self.save(obs(11))
        self.save(obs(24 + 10, rides_with(RIDE_030_00055=("11:00", "17:00"))))
        self.assertIn("**differs** from the final study observation", self.report())

    def test_after_closing_section(self):
        self.save(obs(11))
        self.save(obs(23))
        self.assertIn("1 observation(s); 0 ride schedule change(s) first seen after closing", self.report())

    def report_dir(self):
        return fc.study_dir(self.out, DATE)


class NotesValidationTests(TempDirTest):
    def test_rejects_bad_time_and_long_description(self):
        with self.assertRaises(ValueError):
            notes.add_note(self.out, DATE, "Fury 325", "3pm", "x")
        with self.assertRaises(ValueError):
            notes.add_note(self.out, DATE, "Fury 325", "15:00", "x" * 301)


if __name__ == "__main__":
    unittest.main()
