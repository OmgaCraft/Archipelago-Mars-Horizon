import json

from .bases import MarsHorizonTestBase


class TestSlotData(MarsHorizonTestBase):
    run_default_tests = False

    def test_slot_data_is_json_and_complete(self) -> None:
        data = self.world.fill_slot_data()
        json.dumps(data)
        for key in ("agency_name", "goal", "milestone_goal_count", "filler_strength", "locations", "items", "filler"):
            self.assertIn(key, data)
        self.assertEqual(data["agency_name"], "USA")
        self.assertTrue(all(":" in v for v in data["locations"].values()))
