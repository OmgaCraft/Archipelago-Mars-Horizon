from .bases import MarsHorizonTestBase


class TestDefault(MarsHorizonTestBase):
    """Options par défaut (USA, but : atterrissage habité)."""


class TestNoStartingPad(MarsHorizonTestBase):
    options = {"starting_launchpad": False}


class TestMilestoneGoal(MarsHorizonTestBase):
    options = {"goal": "milestones", "milestone_goal_count": 12}


class TestBuildingsUnshuffled(MarsHorizonTestBase):
    options = {"shuffle_buildings": False}


class TestRussia(MarsHorizonTestBase):
    options = {"agency": "russia"}


class TestEurope(MarsHorizonTestBase):
    options = {"agency": "europe"}


class TestChina(MarsHorizonTestBase):
    options = {"agency": "china"}


class TestJapan(MarsHorizonTestBase):
    options = {"agency": "japan"}
