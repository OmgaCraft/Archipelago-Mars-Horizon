from .. import ITEM_NAME_BY_KEY
from .bases import MarsHorizonTestBase


class TestLaunchpadLogic(MarsHorizonTestBase):
    options = {"starting_launchpad": False}
    run_default_tests = False

    def test_first_launch_needs_small_pad(self) -> None:
        location = self.multiworld.get_location("Milestone: Test Launch", self.player)
        self.assertFalse(location.can_reach(self.state_with([])))
        self.assertTrue(location.can_reach(self.state_with(["Small Launchpad"])))

    def test_medium_pad_needs_small_pad(self) -> None:
        location = self.multiworld.get_location("Build: Medium Launchpad", self.player)
        self.assertFalse(location.can_reach(self.state_with(["Medium Launchpad"])))
        self.assertFalse(location.can_reach(self.state_with(["Small Launchpad"])))
        self.assertTrue(location.can_reach(self.state_with(["Medium Launchpad", "Small Launchpad"])))

    def test_research_needs_no_items(self) -> None:
        state = self.state_with([])
        research = [l for l in self.multiworld.get_locations() if l.name.startswith("Research: ")]
        self.assertGreater(len(research), 100)
        self.assertTrue(all(l.can_reach(state) for l in research))


class TestMissionLogic(MarsHorizonTestBase):
    run_default_tests = False

    def test_crewed_mission_needs_astronaut_training(self) -> None:
        location = self.multiworld.get_location("Milestone: Human In Space", self.player)
        self.assertTrue(location.can_reach(self.state_all_but("Nothing")))
        self.assertFalse(location.can_reach(self.state_all_but(ITEM_NAME_BY_KEY["Building_AstronautTraining"])))

    def test_goal_needs_mars_requirements_and_crew(self) -> None:
        goal = self.multiworld.get_location("Crewed Mars Landing (Goal)", self.player)
        self.assertTrue(goal.can_reach(self.state_all_but("Nothing")))
        for key in ("milestone_mars_mission_engine", "milestone_mars_mission_lander", "Building_AstronautTraining",
                    "milestone_mars_final_mission", "Payload_Orion"):
            self.assertFalse(goal.can_reach(self.state_all_but(ITEM_NAME_BY_KEY[key])),
                             f"le but ne devrait pas être atteignable sans {key}")

    def test_heavy_payload_needs_large_pad(self) -> None:
        location = self.multiworld.get_location("Milestone: Space Station", self.player)
        self.assertTrue(location.can_reach(self.state_all_but("Nothing")))
        self.assertFalse(location.can_reach(self.state_all_but(ITEM_NAME_BY_KEY["Building_LaunchPad_Large"])))

    def test_research_items_are_unique(self) -> None:
        names = [i.name for i in self.multiworld.itempool if i.name not in ("Funding Grant", "Research Data", "Public Support")]
        self.assertEqual(len(names), len(set(names)))
