import datetime as dt
import json
import os
import unittest
from pathlib import Path

from tests.helpers import DATE, UTC, TempDirTest, at, fc, obs_at


class EasternTimeTests(unittest.TestCase):
    def test_utc_to_eastern_summer_and_winter(self):
        self.assertEqual(fc.to_eastern(dt.datetime(2026, 7, 4, 16, 0, tzinfo=UTC)).isoformat(),
                         "2026-07-04T12:00:00-04:00")
        self.assertEqual(fc.to_eastern(dt.datetime(2026, 12, 5, 17, 0, tzinfo=UTC)).isoformat(),
                         "2026-12-05T12:00:00-05:00")
        self.assertEqual(fc.format_eastern(dt.datetime(2026, 10, 24, 19, 5, tzinfo=UTC)), "2026-10-24 3:05 PM EDT")

    def test_fall_back_boundary(self):
        before = fc.to_eastern(dt.datetime(2026, 11, 1, 5, 30, tzinfo=UTC))
        after = fc.to_eastern(dt.datetime(2026, 11, 1, 6, 30, tzinfo=UTC))
        self.assertEqual((before.hour, before.utcoffset()), (1, dt.timedelta(hours=-4)))
        self.assertEqual((after.hour, after.utcoffset()), (1, dt.timedelta(hours=-5)))

    def test_spring_forward_boundary(self):
        self.assertEqual(fc.to_eastern(dt.datetime(2026, 3, 8, 6, 59, tzinfo=UTC)).strftime("%H:%M %z"), "01:59 -0500")
        self.assertEqual(fc.to_eastern(dt.datetime(2026, 3, 8, 7, 0, tzinfo=UTC)).strftime("%H:%M %z"), "03:00 -0400")

    def test_fallback_timezone_matches_zoneinfo_hourly_through_dst_transitions(self):
        start = dt.datetime(2026, 3, 7, 0, 0, tzinfo=UTC)
        for hours in list(range(0, 72)) + list(range(5712, 5784)):  # around Mar 8 and Nov 1, 2026
            moment = start + dt.timedelta(hours=hours)
            a = moment.astimezone(fc.EASTERN)
            b = moment.astimezone(fc.EASTERN_FALLBACK)
            self.assertEqual((a.replace(tzinfo=None), a.utcoffset()), (b.replace(tzinfo=None), b.utcoffset()), moment)
            self.assertEqual(b.astimezone(UTC), moment)

    def test_naive_timestamp_rejected(self):
        with self.assertRaises(ValueError):
            fc.to_eastern(dt.datetime(2026, 10, 24, 12, 0))

    def test_retry_after_parsing(self):
        now = dt.datetime(2026, 10, 24, 15, 0, tzinfo=UTC)
        self.assertEqual(fc.retry_after_seconds("120", now), 120.0)
        self.assertEqual(fc.retry_after_seconds("Sat, 24 Oct 2026 15:10:00 GMT", now), 600.0)
        self.assertIsNone(fc.retry_after_seconds("soon", now))
        self.assertIsNone(fc.retry_after_seconds(None, now))


class StorageTests(TempDirTest):
    def test_save_is_compact_and_in_date_folder(self):
        path = fc.save_observation(self.out, obs_at(at(10)))
        self.assertEqual(path.parent.name, "2026-10-24")
        self.assertEqual(path.name, "obs-20261024T140000Z.json")
        saved = json.loads(path.read_text())
        self.assertEqual(saved["schema"], fc.SCHEMA)
        self.assertNotIn("venues", saved)  # no raw catalog
        self.assertLess(path.stat().st_size, 5000)

    def test_never_overwrites_same_timestamp(self):
        first = fc.save_observation(self.out, obs_at(at(10)))
        second = fc.save_observation(self.out, obs_at(at(10)))
        self.assertNotEqual(first, second)
        self.assertTrue(first.exists() and second.exists())
        self.assertEqual(second.name, "obs-20261024T140000Z-1.json")

    def test_no_temp_files_left_and_temp_files_ignored(self):
        fc.save_observation(self.out, obs_at(at(10)))
        folder = Path(self.out) / "2026-10-24"
        self.assertEqual([p.name for p in folder.iterdir() if p.name.startswith(".tmp-")], [])
        (folder / ".tmp-interrupted.part").write_text("{partial")
        self.assertEqual(len(fc.load_observations(self.out, DATE).observations), 1)

    def test_out_of_order_files_sorted_by_recorded_utc(self):
        later = obs_at(at(14))
        earlier = obs_at(at(10))
        p_later = fc.save_observation(self.out, later)
        p_earlier = fc.save_observation(self.out, earlier)
        # Rename so filename order disagrees with timestamp order.
        os.rename(p_later, p_later.with_name("obs-00000000T000000Z.json"))
        os.rename(p_earlier, p_earlier.with_name("obs-99999999T999999Z.json"))
        loaded = fc.load_observations(self.out, DATE).observations
        self.assertEqual([o["observed_utc"] for o in loaded], ["2026-10-24T14:00:00Z", "2026-10-24T18:00:00Z"])

    def test_dst_ambiguous_eastern_hour_sorted_by_utc(self):
        nov1 = dt.date(2026, 11, 1)
        second_130 = fc.normalize(fc.FetchResult(url="u", status=None, error="timeout"), nov1,
                                  dt.datetime(2026, 11, 1, 6, 30, tzinfo=UTC))
        first_130 = fc.normalize(fc.FetchResult(url="u", status=None, error="timeout"), nov1,
                                 dt.datetime(2026, 11, 1, 5, 30, tzinfo=UTC))
        fc.save_observation(self.out, second_130)
        fc.save_observation(self.out, first_130)
        loaded = fc.load_observations(self.out, nov1).observations
        self.assertEqual([o["observed_eastern"] for o in loaded],
                         ["2026-11-01T01:30:00-04:00", "2026-11-01T01:30:00-05:00"])

    def test_corrupt_file_reported_not_loaded(self):
        fc.save_observation(self.out, obs_at(at(10)))
        (Path(self.out) / "2026-10-24" / "obs-20261024T150000Z.json").write_text("{not json")
        loaded = fc.load_observations(self.out, DATE)
        self.assertEqual(len(loaded.observations), 1)
        self.assertEqual(len(loaded.unreadable), 1)


if __name__ == "__main__":
    unittest.main()
