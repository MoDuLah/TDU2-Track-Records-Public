import unittest

from objective_progress import build_objective_progress


class ObjectiveProgressTests(unittest.TestCase):
    def test_empty_save_still_returns_the_complete_catalogue(self):
        progress = build_objective_progress({}, [])

        self.assertEqual(286, len(progress))
        self.assertEqual(list(range(1, 287)), [row["objective_id"] for row in progress])
        self.assertFalse(any(row["earned"] for row in progress))

    def test_starter_house_and_vehicle_are_the_only_earned_objectives(self):
        data = {
            "Garage": [
                {"Car": {"Archetype": 101}},
                {"Car": {"Archetype": 102}},
            ],
            "MyHouse": {"House": [{}]},
        }

        progress = build_objective_progress(data, [])
        earned_ids = [row["objective_id"] for row in progress if row["earned"]]

        self.assertEqual([41, 54], earned_ids)

    def test_crc_uploads_and_exclusive_furniture_use_persistent_save_evidence(self):
        event_xps = [0] * 178
        event_xps[177] = 15
        data = {
            "Rewards": {"EventXPs": event_xps},
            "Casino": {
                "unlockables0": 1 << 54,
                "CasinoProgressionSave": {"_furniture_chic_set": False},
            },
        }

        progress = {row["objective_id"]: row for row in build_objective_progress(data, [])}

        self.assertTrue(progress[141]["earned"])
        self.assertEqual(15, progress[141]["current"])
        self.assertTrue(progress[223]["earned"])
        self.assertIn("unlocked", progress[223]["detail"].lower())

    def test_road_awards_follow_catalogue_order_across_both_islands(self):
        for objective_ids, reward_slots in (
            ((71, 73, 75, 77, 79), range(124, 129)),
            ((72, 74, 76, 78, 80), range(156, 161)),
            ((81, 85, 89, 93, 97), (4, 5, 121, 122, 123)),
            ((82, 86, 90, 94, 98), range(161, 166)),
            ((83, 87, 91, 95, 99), range(166, 171)),
            ((84, 88, 92, 96, 100), range(171, 176)),
        ):
            for objective_id, slot in zip(objective_ids, reward_slots):
                with self.subTest(objective_id=objective_id, reward_slot=slot):
                    awards = [0] * 300
                    awards[slot] = 1
                    progress = build_objective_progress({"Rewards": {"EventXPs": awards}}, [])
                    self.assertEqual([objective_id], [row["objective_id"] for row in progress if row["earned"]])

    def test_ibiza_road_checkmarks_use_awards_and_detail_uses_map_count(self):
        awards = [0] * 300
        awards[124:126] = [1, 1]
        data = {
            "Rewards": {"EventXPs": awards},
            "PlayerLevels": {
                "AreaRoadsUnlocked": [47, 93, 0, 0, 0, 0],
                "AreaTotalRoads": [467, 924, 309, 1144, 426, 998],
            },
        }

        progress = build_objective_progress(data, [])
        self.assertEqual([71, 73], [row["objective_id"] for row in progress if row["earned"]])
        roads = {row["objective_id"]: row for row in progress if 71 <= row["objective_id"] <= 100}
        self.assertEqual("Roads discovered: 47/467 (10%)", roads[73]["detail"])
        self.assertEqual("Roads discovered: 93/924 (10%)", roads[72]["detail"])

    def test_duel_and_social_rewards_use_ledger_slots(self):
        awards = [0] * 300
        for slot in (97, 139, 141, 140, 142, 10, 137, 136):
            awards[slot] = 1
        awards[114] = 4
        awards[135] = 20

        progress = build_objective_progress({"Rewards": {"EventXPs": awards}}, [])
        self.assertEqual([9, 10, 11, 12, 13, 63, 132, 135, 136, 139],
                         [row["objective_id"] for row in progress if row["earned"]])


if __name__ == "__main__":
    unittest.main()
