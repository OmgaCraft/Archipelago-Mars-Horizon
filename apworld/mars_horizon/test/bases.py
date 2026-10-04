from typing import Iterable

from BaseClasses import CollectionState
from test.bases import WorldTestBase


class MarsHorizonTestBase(WorldTestBase):
    game = "Mars Horizon"

    def all_items(self):
        return [i for i in self.multiworld.itempool if i.player == self.player] + \
            list(self.multiworld.precollected_items[self.player])

    def state_with(self, names: Iterable[str]) -> CollectionState:
        """État où seuls ces items (et les précollectés) sont collectés, événements balayés."""
        names = set(names)
        state = CollectionState(self.multiworld)
        for item in self.all_items():
            if item.name in names or item in self.multiworld.precollected_items[self.player]:
                state.collect(item, True)
        state.sweep_for_advancements()
        return state

    def state_all_but(self, name: str) -> CollectionState:
        """État avec tous les items sauf `name`, événements balayés (contrairement à get_all_state)."""
        state = CollectionState(self.multiworld)
        for item in self.all_items():
            if item.name != name:
                state.collect(item, True)
        state.sweep_for_advancements()
        return state
