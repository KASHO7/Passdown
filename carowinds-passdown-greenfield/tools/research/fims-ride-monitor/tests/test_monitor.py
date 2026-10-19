import contextlib
import datetime as dt
import io
import unittest

import monitor
from tests.helpers import DATE, UTC, TempDirTest, at, fc, ok


class FakeClock:
    def __init__(self, start):
        self.now_value = start
        self.sleeps = []

    def now(self):
        return self.now_value

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now_value += dt.timedelta(seconds=seconds)


class FakeSource:
    def __init__(self, clock, responses, request_seconds=2):
        self.clock, self.responses, self.calls, self.request_seconds = clock, list(responses), [], request_seconds

    def __call__(self, operating_date):
        self.calls.append((operating_date, self.clock.now()))
        self.clock.now_value += dt.timedelta(seconds=self.request_seconds)
        return self.responses.pop(0) if len(self.responses) > 1 else self.responses[0]


class WatchTests(TempDirTest):
    def run_watch(self, responses, hours=4.0, interval=60):
        clock = FakeClock(at(9, 0))
        source = FakeSource(clock, responses)
        end = clock.now() + dt.timedelta(hours=hours)
        summary = monitor.watch(DATE, self.out, interval, end, fetch=source, now=clock.now,
                                sleep=clock.sleep, log=lambda _: None)
        return summary, source, clock

    def test_samples_on_interval_and_stops_at_window_end(self):
        summary, source, _ = self.run_watch([ok()], hours=4.0)
        self.assertEqual(summary["stopped_because"], "end of monitoring window")
        self.assertEqual(len(source.calls), 4)  # 9:00, 10:00, 11:00, 12:00; 13:00 is the end
        self.assertTrue(all(d == DATE for d, _ in source.calls))
        gaps = [(b - a).total_seconds() for (_, a), (_, b) in zip(source.calls, source.calls[1:])]
        self.assertEqual(gaps, [3600.0] * 3)
        self.assertEqual(len(fc.load_observations(self.out, DATE).observations), 4)

    def test_stops_after_three_consecutive_failures(self):
        failure = fc.FetchResult(url="u", status=None, error="timeout")
        summary, source, _ = self.run_watch([failure], hours=10)
        self.assertEqual(len(source.calls), 3)
        self.assertIn("consecutive failed", summary["stopped_because"])

    def test_isolated_failures_do_not_stop(self):
        failure = fc.FetchResult(url="u", status=503, body=b"")
        summary, source, _ = self.run_watch([ok(), failure, ok(), failure, ok()], hours=5)
        self.assertEqual((summary["successful"], summary["failed"]), (3, 2))
        self.assertEqual(summary["stopped_because"], "end of monitoring window")

    def test_honours_retry_after_longer_than_interval(self):
        limited = ok(b"", status=429, retry_after="7200")
        summary, source, _ = self.run_watch([limited, ok()], hours=6)
        gap = (source.calls[1][1] - source.calls[0][1]).total_seconds()
        self.assertGreaterEqual(gap, 7200)

    def test_retry_after_past_window_stops(self):
        limited = ok(b"", status=429, retry_after="99999")
        summary, source, _ = self.run_watch([limited], hours=6)
        self.assertEqual(len(source.calls), 1)
        self.assertIn("Retry-After", summary["stopped_because"])

    def test_no_catch_up_burst_after_long_pause(self):
        clock = FakeClock(at(9, 0))
        calls = []

        def slow_fetch(d):
            calls.append(clock.now())
            if len(calls) == 1:
                clock.now_value += dt.timedelta(hours=3)  # e.g. laptop asleep during the request
            return ok()

        monitor.watch(DATE, self.out, 60, at(15, 0), fetch=slow_fetch, now=clock.now, sleep=clock.sleep,
                      log=lambda _: None)
        gaps = [(b - a).total_seconds() for a, b in zip(calls, calls[1:])]
        self.assertTrue(all(g >= 3600 for g in gaps[1:]), gaps)

    def test_keyboard_interrupt_propagates_and_keeps_saved_observations(self):
        clock = FakeClock(at(9, 0))

        def interrupting_sleep(seconds):
            raise KeyboardInterrupt

        with self.assertRaises(KeyboardInterrupt):
            monitor.watch(DATE, self.out, 60, at(17, 0), fetch=FakeSource(clock, [ok()]), now=clock.now,
                          sleep=interrupting_sleep, log=lambda _: None)
        self.assertEqual(len(fc.load_observations(self.out, DATE).observations), 1)


class LimitsTests(unittest.TestCase):
    def setUp(self):
        quiet = contextlib.redirect_stderr(io.StringIO())
        quiet.__enter__()
        self.addCleanup(quiet.__exit__, None, None, None)

    def test_interval_bounds(self):
        parser = monitor.build_parser()
        self.assertEqual(parser.parse_args(["watch", "--date", "2026-10-24"]).interval_minutes, 60)
        for ok_value in ("30", "120"):
            parser.parse_args(["watch", "--date", "2026-10-24", "--interval-minutes", ok_value])
        for bad in ("29", "5", "121"):
            with self.assertRaises(SystemExit):
                parser.parse_args(["watch", "--date", "2026-10-24", "--interval-minutes", bad])

    def test_duration_bounds_and_default(self):
        parser = monitor.build_parser()
        self.assertEqual(parser.parse_args(["watch", "--date", "2026-10-24"]).duration_hours, 16.0)
        with self.assertRaises(SystemExit):
            parser.parse_args(["watch", "--date", "2026-10-24", "--duration-hours", "16.5"])

    def test_bad_date_rejected(self):
        with self.assertRaises(SystemExit):
            monitor.build_parser().parse_args(["snapshot", "--date", "10/24/2026"])

    def test_resolve_end(self):
        start = at(9, 0)
        self.assertEqual(monitor.resolve_end(start, DATE, None, 16), start + dt.timedelta(hours=16))
        self.assertEqual(monitor.resolve_end(start, DATE, "23:30", 16), at(23, 30))
        self.assertEqual(monitor.resolve_end(at(17, 0), DATE, "00:30", 16), at(0, 30, DATE + dt.timedelta(days=1)))
        self.assertEqual(monitor.resolve_end(start, DATE, "23:30", 4), start + dt.timedelta(hours=4))
        with self.assertRaises(ValueError):
            monitor.resolve_end(start, DATE, "11pm", 16)


class SnapshotTests(TempDirTest):
    def test_snapshot_saves_one_observation_with_requested_date_only(self):
        requested = []
        obs, path, _ = monitor.observe(DATE, self.out, fetch=lambda d: requested.append(d) or ok(),
                                       now=lambda: at(10, 0))
        self.assertEqual(requested, [DATE])
        self.assertTrue(path.exists())
        self.assertIn("3 rides listed, 2 with scheduled hours, 1 blank", monitor.describe(obs))

    def test_failed_snapshot_saved_as_not_comparable(self):
        obs, path, _ = monitor.observe(DATE, self.out, fetch=lambda d: fc.FetchResult(url="u", status=None, error="timeout"),
                                       now=lambda: at(10, 0))
        self.assertFalse(obs["comparable"])
        self.assertIn("FAILED (network_error)", monitor.describe(obs))

    def test_url_and_user_agent(self):
        self.assertEqual(fc.source_url(DATE), "https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20261024")
        self.assertIn("research", fc.USER_AGENT.lower())


if __name__ == "__main__":
    unittest.main()
