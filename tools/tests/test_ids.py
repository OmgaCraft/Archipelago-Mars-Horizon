"""Invariants de la table d'IDs (python -m unittest discover tools/tests)."""
import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import build_data as bd  # noqa: E402

IDS = bd.DATA_DIR / "ids.json"


class AssignIdsTest(unittest.TestCase):
    def test_existing_ids_never_change_and_retired_are_never_reused(self):
        table = bd.empty_table()
        bd.assign_ids(table, "items", "research", ["a", "b", "c"], [])
        first = dict(table["items"]["research"])

        bd.assign_ids(table, "items", "research", ["a", "c", "d"], [])  # b retiré, d ajouté
        self.assertEqual(table["items"]["research"]["a"], first["a"])
        self.assertEqual(table["items"]["research"]["c"], first["c"])
        self.assertEqual(table["retired"]["items"]["research"], {"b": first["b"]})
        self.assertGreater(table["items"]["research"]["d"], max(first.values()))

        bd.assign_ids(table, "items", "research", ["a", "b", "c", "d"], [])  # b revient avec son ID
        self.assertEqual(table["items"]["research"]["b"], first["b"])

    def test_reordering_keys_does_not_change_ids(self):
        table = bd.empty_table()
        bd.assign_ids(table, "locations", "milestone", ["x", "y", "z"], [])
        before = copy.deepcopy(table)
        bd.assign_ids(table, "locations", "milestone", ["z", "x", "y"], [])
        self.assertEqual(before, table)

    def test_ids_start_inside_their_range(self):
        table = bd.empty_table()
        bd.assign_ids(table, "locations", "building", ["k"], [])
        lo, _ = bd.RANGES[("locations", "building")]
        self.assertEqual(table["locations"]["building"]["k"], bd.BASE_ID + lo)


@unittest.skipUnless(IDS.exists(), "ids.json pas encore généré")
class CommittedTableTest(unittest.TestCase):
    def setUp(self):
        self.table = json.loads(IDS.read_text(encoding="utf-8"))

    def test_base_id(self):
        self.assertEqual(self.table["base_id"], bd.BASE_ID)

    def test_ids_unique_and_in_range(self):
        for kind in ("items", "locations"):
            seen = set()
            for category in set(self.table[kind]) | set(self.table["retired"][kind]):
                lo, hi = bd.RANGES[(kind, category)]
                values = list(self.table[kind].get(category, {}).values()) + list(self.table["retired"][kind].get(category, {}).values())
                for value in values:
                    self.assertTrue(bd.BASE_ID + lo <= value <= bd.BASE_ID + hi, f"{kind}.{category} {value}")
                    self.assertNotIn(value, seen, f"ID dupliqué {value}")
                    seen.add(value)

    def test_lists_match_table(self):
        items = json.loads((bd.DATA_DIR / "items.json").read_text(encoding="utf-8"))
        locations = json.loads((bd.DATA_DIR / "locations.json").read_text(encoding="utf-8"))
        table_items = {v for c in self.table["items"].values() for v in c.values()}
        table_locations = {v for c in self.table["locations"].values() for v in c.values()}
        self.assertEqual({i["id"] for i in items}, table_items)
        self.assertEqual({l["id"] for l in locations}, table_locations)
        self.assertEqual(len({i["name"] for i in items}), len(items), "noms d'items en double")
        self.assertEqual(len({l["name"] for l in locations}), len(locations), "noms de locations en double")

    def test_ranges_do_not_overlap(self):
        spans = sorted(bd.RANGES.values())
        for (_, hi), (lo, _) in zip(spans, spans[1:]):
            self.assertLess(hi, lo)


if __name__ == "__main__":
    unittest.main()
