import json
import unittest

from tests.helpers import DATE, at, body, day, fc, ok, ride


class NormalizeValidResponseTests(unittest.TestCase):
    def test_valid_response_records_rides_gate_and_metadata(self):
        obs = fc.normalize(ok(), DATE, at(10, 0))
        self.assertEqual(obs["outcome"], "ok")
        self.assertTrue(obs["comparable"])
        self.assertEqual(obs["operating_date"], "2026-10-24")
        self.assertEqual(obs["observed_utc"], "2026-10-24T14:00:00Z")
        self.assertEqual(obs["observed_eastern"], "2026-10-24T10:00:00-04:00")
        self.assertEqual(obs["http_status"], 200)
        self.assertEqual(obs["etag"], '"e1"')
        self.assertEqual(obs["source_url"], "https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20261024")
        self.assertEqual(obs["gate"], [{"type_id": 24, "label": "Park", "from": "11:00", "to": "22:00"}])
        self.assertEqual((obs["ride_count"], obs["rides_with_hours"], obs["rides_without_hours"]), (3, 2, 1))

    def test_only_rides_venue_is_used_not_whole_catalog(self):
        obs = fc.normalize(ok(), DATE, at(10))
        self.assertNotIn("DINE-030-00001", [r["id"] for r in obs["rides"]])

    def test_ride_identity_is_official_id_not_list_position(self):
        rides = [ride("RIDE-030-00002", "B"), ride("RIDE-030-00001", "A", "", "")]
        forward = fc.normalize(ok(body(day(rides))), DATE, at(10))["rides"]
        backward = fc.normalize(ok(body(day(list(reversed(rides))))), DATE, at(10))["rides"]
        self.assertEqual(forward, backward)
        by_id = {r["id"]: r for r in forward}
        self.assertEqual(by_id["RIDE-030-00001"]["state"], "blank")
        self.assertEqual(by_id["RIDE-030-00002"]["from"], "11:00")

    def test_blank_schedule_is_blank_not_closed(self):
        fury = next(r for r in fc.normalize(ok(), DATE, at(10))["rides"] if r["name"] == "Fury 325")
        self.assertEqual((fury["state"], fury["populated"], fury["from"], fury["to"]), ("blank", False, "", ""))
        self.assertEqual(fc.format_schedule(fury), "No scheduled hours listed")

    def test_overnight_closing_time(self):
        rows = [ride("RIDE-030-00098", "Copperhead Strike", "18:00", "00:00")]
        r = fc.normalize(ok(body(day(rows, gate=("18:00", "00:00")))), DATE, at(17))["rides"][0]
        self.assertEqual(r["state"], "populated")
        self.assertTrue(fc.crosses_midnight("18:00", "00:00"))
        self.assertFalse(fc.crosses_midnight("11:00", "23:59"))
        self.assertEqual(fc.format_schedule(r), "6:00 PM – 12:00 AM (next day)")

    def test_missing_and_invalid_time_fields_are_data_quality_states(self):
        no_to = {"itemID": -1, "itemName": "X", "extLocationID": "RIDE-030-00010", "operatingTimeFrom": "11:00"}
        half = ride("RIDE-030-00011", "Y", "11:00", "")
        bad = ride("RIDE-030-00012", "Z", "11am", "10pm")
        obs = fc.normalize(ok(body(day([no_to, half, bad]))), DATE, at(10))
        states = {r["id"]: r["state"] for r in obs["rides"]}
        self.assertEqual(states, {"RIDE-030-00010": "missing_fields", "RIDE-030-00011": "invalid",
                                  "RIDE-030-00012": "invalid"})
        self.assertEqual((obs["rides_with_hours"], obs["rides_without_hours"], obs["rides_unreadable"]), (0, 0, 3))
        self.assertTrue(all(r["populated"] is None for r in obs["rides"]))

    def test_identical_duplicate_collapsed_conflicting_duplicate_flagged(self):
        rows = [ride("RIDE-030-00001", "A"), ride("RIDE-030-00001", "A"),
                ride("RIDE-030-00002", "B", "11:00", "18:00"), ride("RIDE-030-00002", "B", "", "")]
        obs = fc.normalize(ok(body(day(rows))), DATE, at(10))
        self.assertEqual(obs["ride_count"], 2)
        self.assertEqual(obs["duplicate_ids"], ["RIDE-030-00002"])
        b = next(r for r in obs["rides"] if r["id"] == "RIDE-030-00002")
        self.assertEqual(b["state"], "conflicting_duplicate")
        self.assertTrue(any("identical duplicate" in w for w in obs["warnings"]))

    def test_park_closed_is_comparable_with_no_rides(self):
        obs = fc.normalize(ok(body(day(closed=True))), DATE, at(10))
        self.assertEqual(obs["outcome"], "park_closed")
        self.assertTrue(obs["comparable"])
        self.assertEqual(obs["rides"], [])


class NormalizeFailureTests(unittest.TestCase):
    def assertFailure(self, fetch, outcome):
        obs = fc.normalize(fetch, DATE, at(10))
        self.assertEqual(obs["outcome"], outcome)
        self.assertFalse(obs["comparable"])
        self.assertEqual(obs["rides"], [])
        return obs

    def test_timeout(self):
        self.assertFailure(fc.FetchResult(url="u", status=None, error="timeout"), "network_error")

    def test_404_not_found(self):
        payload = json.dumps({"error": "Not Found", "message": "The requested resource could not be found."}).encode()
        self.assertFailure(ok(payload, status=404), "not_found")

    def test_429_rate_limited(self):
        obs = self.assertFailure(ok(b"", status=429, retry_after="120"), "rate_limited")
        self.assertIn("120", obs["detail"])

    def test_5xx(self):
        for status in (500, 502, 503):
            self.assertFailure(ok(b"<html>error</html>", status=status), "http_error")

    def test_malformed_json(self):
        self.assertFailure(ok(b'{"parkId":30,"dates":[{'), "invalid_response")

    def test_wrong_park(self):
        self.assertFailure(ok(body(day(), park_id=31)), "invalid_response")

    def test_requested_date_missing(self):
        import datetime as dt
        self.assertFailure(ok(body(day(date=dt.date(2026, 10, 25)))), "incomplete")

    def test_missing_rides_venue(self):
        self.assertFailure(ok(body(day(include_rides_venue=False))), "incomplete")

    def test_empty_ride_catalog(self):
        self.assertFailure(ok(body(day(rides=[]))), "incomplete")

    def test_partial_day_without_venues(self):
        partial = day()
        del partial["venues"]
        self.assertFailure(ok(body(partial)), "incomplete")

    def test_oversized_or_truncated_body(self):
        self.assertFailure(fc.FetchResult(url="u", status=200, body=b"", error="response larger than"), "invalid_response")


if __name__ == "__main__":
    unittest.main()
