"""Génération massive de seeds en solo (critère de la phase 2). MH_SEEDS=100 pour la série complète."""
import os
import random

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from worlds.AutoWorld import call_all

from .bases import MarsHorizonTestBase

SEEDS = int(os.environ.get("MH_SEEDS", "10"))
AGENCIES = ["usa", "russia", "europe", "china", "japan"]


class TestManySeeds(MarsHorizonTestBase):
    run_default_tests = False

    def test_seeds(self) -> None:
        rng = random.Random(1234)
        for n in range(SEEDS):
            self.options = {
                "agency": AGENCIES[n % len(AGENCIES)],
                "goal": rng.choice(["crewed_mars_landing", "milestones"]),
                "milestone_goal_count": rng.randint(5, 30),
                "starting_launchpad": rng.random() < 0.8,
                "shuffle_buildings": rng.random() < 0.7,
                "filler_funds_weight": rng.randint(0, 100),
                "filler_science_weight": rng.randint(0, 100),
                "filler_support_weight": rng.randint(0, 100),
            }
            with self.subTest(n=n, options=self.options):
                self.world_setup(seed=1000 + n)
                distribute_items_restrictive(self.multiworld)
                call_all(self.multiworld, "post_fill")
                call_all(self.multiworld, "finalize_multiworld")
                self.assertTrue(self.multiworld.can_beat_game(CollectionState(self.multiworld)), "seed non finissable")
                self.assertEqual(0, len([l for l in self.multiworld.get_locations() if l.item is None]))
