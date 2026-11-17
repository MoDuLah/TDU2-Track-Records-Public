"""Derive Track Records objective progress from read-only TDU2 save data.

The base-game objectives use the save's domain structures rather than the four
headline level totals.  Casino objectives are keyed to DBCasinoObjectiveDetail
hashes from the installed v34/TDU World game database and evaluated against the
matching CasinoProgressionSave counters.
"""

import math


ROAD_MILESTONES = (10, 25, 50, 75, 100)
# EventXPs indexes DBRewardXP by row position. The objective catalogue
# interleaves areas within each milestone, separately for each island.
ROAD_REWARD_SLOTS = (
    (124, 125, 126, 127, 128),  # Ibiza 1
    (156, 157, 158, 159, 160),  # Ibiza 2
    (4, 5, 121, 122, 123),      # Hawaii 1
    (161, 162, 163, 164, 165),  # Hawaii 2
    (166, 167, 168, 169, 170),  # Hawaii 3
    (171, 172, 173, 174, 175),  # Hawaii 4
)


# objective id, casino objective hash, category, filter A, filter B, filter C,
# objective value.  IDs are matched to the English objective catalogue by the
# authoritative localized DBCasinoObjectiveDetail text and point value.
CASINO_OBJECTIVES = (
    (142, 49, 5, 15, 1, 1, 13),
    (143, 47, 5, 11, 1, 1, 2),
    (144, 48, 5, 12, 1, 1, 4),
    (145, 50, 5, 13, 1, 1, 5),
    (146, 46, 5, 14, 1, 1, 6),
    (147, 45, 5, 10, 1, 1, 6),
    (148, 54, 13, 4, 1, 1, 1),
    (149, 53, 1, 4, 1, 1, 10000),
    (150, 52, 1, 4, 1, 1, 2500),
    (151, 55, 6, 2, 1, 1, 3),
    (152, 56, 6, 2, 1, 1, 5),
    (153, 65, 5, 2, 44, 1, 1),
    (154, 64, 5, 2, 43, 1, 1),
    (155, 66, 5, 2, 45, 1, 1),
    (156, 63, 5, 2, 42, 1, 1),
    (157, 69, 5, 2, 48, 1, 1),
    (158, 62, 5, 2, 41, 1, 1),
    (159, 68, 5, 2, 47, 1, 1),
    (160, 67, 5, 2, 46, 1, 1),
    (161, 58, 5, 2, 37, 1, 1),
    (162, 57, 5, 2, 36, 1, 1),
    (163, 61, 5, 2, 40, 1, 1),
    (164, 60, 5, 2, 39, 1, 1),
    (165, 59, 5, 2, 38, 1, 1),
    (166, 72, 1, 2, 1, 1, 10000),
    (167, 71, 1, 2, 1, 1, 2500),
    (168, 70, 1, 2, 1, 1, 500),
    (169, 76, 9, 17, 1, 1, 10),
    (170, 77, 9, 17, 1, 1, 25),
    (171, 79, 6, 17, 1, 1, 3),
    (172, 80, 6, 17, 1, 1, 5),
    (173, 78, 9, 17, 1, 1, 50),
    (174, 85, 5, 3, 64, 1, 1),
    (175, 86, 5, 3, 65, 1, 1),
    (176, 84, 5, 3, 63, 1, 1),
    (177, 83, 5, 3, 62, 1, 1),
    (178, 81, 5, 3, 60, 1, 1),
    (179, 82, 5, 3, 61, 1, 1),
    (180, 73, 1, 17, 1, 1, 1000),
    (181, 75, 1, 17, 1, 1, 10000),
    (182, 74, 1, 17, 1, 1, 2500),
    (183, 91, 7, 21, 49, 1, 1),
    (184, 92, 7, 18, 31, 49, 1),
    (185, 93, 7, 18, 31, 50, 1),
    (186, 94, 7, 18, 31, 51, 1),
    (187, 95, 7, 18, 31, 52, 1),
    (188, 96, 7, 18, 31, 53, 1),
    (189, 101, 40, 18, 1, 1, 10),
    (190, 104, 42, 18, 1, 1, 10),
    (191, 103, 41, 18, 1, 1, 10),
    (192, 102, 40, 18, 1, 1, 20),
    (193, 97, 9, 18, 1, 1, 25),
    (194, 99, 6, 18, 1, 1, 3),
    (195, 100, 6, 18, 1, 1, 5),
    (196, 98, 9, 18, 1, 1, 75),
    (197, 89, 1, 18, 1, 1, 20000),
    (198, 88, 1, 18, 1, 1, 5000),
    (199, 90, 1, 18, 1, 1, 50000),
    (200, 105, 7, 19, 23, 54, 1),
    (201, 106, 7, 19, 24, 54, 1),
    (202, 107, 7, 19, 25, 54, 1),
    (203, 108, 7, 19, 26, 54, 1),
    (204, 115, 7, 19, 1, 49, 2),
    (205, 109, 7, 19, 23, 49, 1),
    (206, 110, 7, 19, 24, 49, 1),
    (207, 111, 7, 19, 25, 49, 1),
    (208, 112, 7, 19, 26, 49, 1),
    (209, 114, 1, 19, 1, 1, 1),
    (210, 113, 1, 19, 1, 1, 1),
    (211, 117, 11, 55, 1, 1, 149),
    (212, 116, 11, 55, 1, 1, 164),
    (213, 194, 70, 55, 1, 1, 149),
    (214, 193, 70, 55, 1, 1, 164),
    (215, 170, 22, 1, 1, 1, 10),
    (216, 172, 24, 1, 1, 1, 15),
    (217, 173, 24, 1, 1, 1, 25),
    (218, 174, 24, 1, 1, 1, 32),
    (219, 171, 22, 1, 1, 1, 15),
    (220, 175, 31, 1, 1, 1, 1),
    (221, 176, 31, 1, 1, 1, 2),
    (222, 177, 49, 1, 1, 1, 1),
    (223, 179, 51, 1, 1, 1, 1),
    (224, 178, 50, 1, 1, 1, 1),
    (225, 182, 34, 1, 1, 1, 25),
    (226, 184, 53, 1, 1, 1, 4),
    (227, 180, 34, 1, 1, 1, 8),
    (228, 183, 52, 1, 1, 1, 2),
    (229, 181, 34, 1, 1, 1, 16),
    (230, 138, 12, 55, 1, 1, 1),
    (231, 188, 59, 1, 1, 1, 1),
    (232, 134, 43, 19, 23, 1, 1),
    (233, 135, 43, 19, 24, 1, 1),
    (234, 136, 43, 19, 25, 1, 1),
    (235, 137, 43, 19, 26, 1, 1),
    (236, 129, 4, 2, 1, 1, 16),
    (237, 122, 15, 4, 1, 1, 10),
    (238, 120, 14, 4, 1, 1, 10),
    (239, 123, 16, 4, 1, 1, 20),
    (240, 121, 14, 4, 1, 1, 25),
    (241, 118, 43, 4, 1, 1, 3),
    (242, 124, 17, 4, 1, 1, 30),
    (243, 125, 18, 4, 1, 1, 40),
    (244, 130, 43, 17, 1, 1, 12),
    (245, 131, 43, 20, 1, 1, 12),
    (246, 119, 43, 4, 1, 1, 6),
    (247, 132, 43, 21, 27, 1, 1),
    (248, 133, 43, 21, 67, 1, 1),
    (249, 126, 54, 2, 36, 1, 12),
    (250, 127, 54, 2, 36, 1, 24),
    (251, 128, 54, 2, 48, 1, 1),
    (252, 186, 57, 1, 1, 1, 1),
    (253, 142, 19, 59, 1, 1, 1),
    (254, 140, 19, 57, 1, 1, 1),
    (255, 139, 19, 56, 1, 1, 1),
    (256, 141, 19, 58, 1, 1, 1),
    (257, 143, 23, 1, 1, 1, 8),
    (258, 145, 23, 1, 1, 1, 32),
    (259, 144, 23, 1, 1, 1, 16),
    (260, 146, 25, 1, 1, 1, 1),
    (261, 151, 34, 1, 1, 1, 1),
    (262, 147, 27, 1, 1, 1, 1),
    (263, 148, 29, 1, 1, 1, 1),
    (264, 149, 32, 1, 1, 1, 1),
    (265, 187, 58, 1, 1, 1, 3),
    (266, 185, 56, 1, 1, 1, 1),
    (267, 190, 61, 1, 1, 1, 1),
    (268, 189, 60, 1, 1, 1, 1),
    (269, 155, 55, 1, 1, 1, 10),
    (270, 156, 55, 1, 1, 1, 25),
    (271, 157, 21, 1, 1, 1, 10),
    (272, 154, 45, 1, 1, 1, 15),
    (273, 153, 45, 1, 1, 1, 5),
    (274, 158, 36, 1, 1, 1, 3),
    (275, 159, 36, 1, 1, 1, 5),
    (276, 161, 26, 1, 1, 1, 1),
    (277, 160, 37, 1, 1, 1, 10),
    (278, 191, 62, 1, 1, 1, 1),
    (279, 164, 47, 21, 1, 1, 1),
    (280, 163, 46, 21, 1, 1, 1),
    (281, 162, 10, 19, 1, 1, 3),
    (282, 165, 48, 18, 1, 1, 5),
    (283, 166, 38, 17, 1, 1, 10),
    (284, 167, 38, 17, 1, 1, 25),
    (285, 169, 39, 17, 1, 1, 2500),
    (286, 168, 39, 17, 1, 1, 500),
)


EVENT_AREAS = {
    ("base", "Hawaii", 1): (3, 4, 5, 6, 18, 29, 30, 73, 74, 75, 76, 94, 95, 99, 102),
    ("base", "Hawaii", 2): (2, 7, 17, 31, 32, 33, 34, 77, 78, 79, 80, 98, 100, 103, 104),
    ("base", "Hawaii", 3): (9, 10, 14, 15, 16, 23, 81, 82, 83, 84, 85, 86, 96, 101, 105),
    ("base", "Hawaii", 4): (1, 8, 11, 24, 25, 26, 27, 87, 88, 89, 90, 91, 97, 106, 107),
    ("base", "Ibiza", 1): (35, 36, 37, 38, 39, 40, 41, 49, 53, 54, 55, 56, 58, 59, 61, 62, 65, 66, 67, 68, 92),
    ("base", "Ibiza", 2): (42, 43, 44, 45, 46, 47, 48, 50, 51, 52, 57, 60, 63, 69, 70, 71, 72, 93),
    ("dlc", "Hawaii", 1): (118, 124, 125),
    ("dlc", "Hawaii", 2): (116, 120, 123),
    ("dlc", "Hawaii", 3): (121, 122, 126),
    ("dlc", "Hawaii", 4): (117, 119, 127),
    ("dlc", "Ibiza", 1): (108, 110, 113, 115),
    ("dlc", "Ibiza", 2): (109, 111, 112, 114),
}


def _number(value, default=0):
    return value if type(value) in (int, float) and math.isfinite(value) else default


def _bit_count(value):
    return int(value).bit_count() if type(value) is int and value >= 0 else 0


def _entry(objective_id, current, target, label, earned=None):
    current = max(0, int(current))
    target = max(1, int(target))
    if earned is None:
        earned = current >= target
    return {
        "objective_id": objective_id,
        "earned": bool(earned),
        "current": min(current, target),
        "target": target,
        "detail": f"{label}: {min(current, target)}/{target}",
    }


def _bool_entry(objective_id, value, label):
    return _entry(objective_id, 1 if value else 0, 1, label)


def _event_xp(data, slot):
    values = data.get("Rewards", {}).get("EventXPs", [])
    return values[slot] if slot < len(values) and type(values[slot]) is int else 0


def _road_progress(data, levels):
    unlocked = levels.get("AreaRoadsUnlocked", [])
    totals = levels.get("AreaTotalRoads", [])
    for area, rewards in enumerate(ROAD_REWARD_SLOTS):
        first_id, area_count, island_area = (71, 2, area) if area < 2 else (81, 4, area - 2)
        current = unlocked[area] if area < len(unlocked) else 0
        total = totals[area] if area < len(totals) else 0
        has_map_count = total > 0 and 0 <= current <= total
        for tier, (milestone, slot) in enumerate(zip(ROAD_MILESTONES, rewards)):
            objective_id = first_id + tier * area_count + island_area
            required = (total * milestone + 99) // 100 if has_map_count else 1
            row = _entry(objective_id, current if has_map_count else 0, required, "Roads discovered",
                         earned=_event_xp(data, slot) > 0)
            # Awarded milestones persist independently of the map discoveries.
            row["detail"] = (f"Roads discovered: {current}/{total} ({current * 100 // total}%)"
                             if has_map_count else "Map discovery unavailable")
            yield row


def _count_taken(rows, start, count):
    return sum(bool(row.get("Taken")) for row in rows[start:start + count] if isinstance(row, dict))


def _count_rows(rows, key):
    return sum(max(0, int(_number(row.get(key)))) for row in rows if isinstance(row, dict))


def _max_rows(rows, key):
    return max((int(_number(row.get(key))) for row in rows if isinstance(row, dict)), default=0)


def _union_count(rows, key):
    arrays = [row.get(key, []) for row in rows if isinstance(row, dict)]
    width = max((len(values) for values in arrays), default=0)
    return sum(any(index < len(values) and _number(values[index]) > 0 for values in arrays)
               for index in range(width))


def _casino_rows(progression, filter_code):
    names = {
        2: "_array_save_roulettes",
        4: "_array_save_slotmachines",
        17: "_array_save_poker_cashgame",
        18: "_array_save_poker_sitandgo_public",
        19: "_array_save_poker_satellite",
        20: "_array_save_poker_sitandgo_public",
        21: "_array_save_poker_sitandgo_private",
    }
    return progression.get(names.get(filter_code, ""), [])


def _casino_unlockable(casino, unlockable_hash):
    """Return whether a DBCasinoUnlockable hash is set in the save bitfields."""
    if unlockable_hash < 0:
        return False
    word_name = "unlockables0" if unlockable_hash < 64 else "unlockables1"
    bit_index = unlockable_hash if unlockable_hash < 64 else unlockable_hash - 64
    return bool(int(_number(casino.get(word_name))) & (1 << bit_index))


def _casino_progress(data, row):
    objective_id, reward_hash, category, filter_a, filter_b, filter_c, target = row
    casino = data.get("Casino", {})
    progression = casino.get("CasinoProgressionSave", {})
    rows = _casino_rows(progression, filter_a)
    label = "Saved progress"

    if category == 1:
        if filter_a == 19 and reward_hash in (113, 114):
            target = 25000 if reward_hash == 113 else 100000
        current = _count_rows(rows, "_money_won")
        label = "Winnings"
    elif category == 4:
        current = _union_count(rows, "_p_played_combination_array")
        label = "Bet types placed"
    elif category == 5:
        if 10 <= filter_a <= 15:
            slot_rows = _casino_rows(progression, 4)
            selected = slot_rows[filter_a - 10] if filter_a - 10 < len(slot_rows) else {}
            current = sum(_number(value) > 0 for value in selected.get("_p_won_combination_array", []))
            label = "Winning combinations"
        elif filter_a == 2:
            index = filter_b - 36
            current = 1 if any(index < len(values) and _number(values[index]) > 0
                               for values in (item.get("_p_won_combination_array", []) for item in rows)) else 0
            label = "Roulette bet won"
        else:
            poker_rows = sum((_casino_rows(progression, code) for code in (17, 18, 21, 19)), [])
            index = filter_b - 60
            current = 1 if any(index < len(values) and _number(values[index]) > 0
                               for values in (item.get("_p_won_combination_array", []) for item in poker_rows)) else 0
            label = "Poker hand won"
    elif category == 6:
        current = _max_rows(rows, "_best_win_in_a_row")
        label = "Best winning streak"
    elif category == 7:
        if filter_a == 19:
            selected = rows[filter_b - 23] if 0 <= filter_b - 23 < len(rows) else None
            tournament_rows = [selected] if selected is not None else rows
            rank_count = 5 if filter_c == 54 else 1
            current = sum(sum(int(_number(value)) for value in item.get("_ranks_count", [])[:rank_count])
                          for item in tournament_rows)
        else:
            rank_index = 0 if filter_a == 21 else max(0, filter_c - 49)
            current = sum(int(_number(item.get("_ranks_count", [])[rank_index]))
                          for item in rows if rank_index < len(item.get("_ranks_count", [])))
        label = "Qualifying finishes"
    elif category == 9:
        current = _count_rows(rows, "_win_count")
        label = "Hands won"
    elif category == 10:
        current = _count_rows(rows, "_play_count")
        label = "Tournaments played"
    elif category in (11, 70):
        key = "_time_attack_vip_best_score" if category == 70 else "_time_attack_best_score"
        best = int(_number(progression.get(key), 2147483647))
        limit = target * 100
        return _bool_entry(objective_id, best < limit, f"Best time below {target + 1} seconds")
    elif category == 12:
        current = int(_number(progression.get("_time_attack_played_count")))
        label = "Island races"
    elif category == 13:
        current = sum(_bit_count(item.get("_jackpot_won_bitfield")) for item in rows)
        label = "Jackpots won"
    elif category == 14:
        current = _count_rows(rows, "_play_by_relaunch_count")
        label = "Respin plays"
    elif category in (15, 16, 17, 18):
        key = f"_played_with_{(2, 3, 5, 9)[category - 15]}_lines"
        current = _count_rows(rows, key)
        label = "Matching slot plays"
    elif category == 19:
        index = filter_a - 56
        values = progression.get("_cocktail_drink_array", [])
        current = int(_number(values[index])) if 0 <= index < len(values) else 0
        label = "Cocktails drunk"
    elif category == 21:
        current = sum(int(_number(value)) for value in progression.get("_cocktail_received_array", []))
        label = "Cocktails received"
    elif category == 22:
        current = _bit_count(progression.get("_cocktail_unlock_flag"))
        label = "Cocktails unlocked"
    elif category == 23:
        current = sum(_number(value) > 0 for value in progression.get("_emote_played_array", []))
        label = "Emotes played"
    elif category == 24:
        current = _bit_count(progression.get("_emote_unlock_flag"))
        label = "Emotes unlocked"
    elif category == 25:
        current = int(_number(progression.get("_vip_terrace_seen")))
        label = "VIP terrace visits"
    elif category == 26:
        return _bool_entry(objective_id, progression.get("_vip_status"), "VIP status")
    elif category == 27:
        return _bool_entry(objective_id, progression.get("_hall_car_seen"), "Hall car inspected")
    elif category == 29:
        return _bool_entry(objective_id, progression.get("_vip_car_seen"), "VIP car inspected")
    elif category == 31:
        current = sum(bool(progression.get(key)) for key in ("_hall_car_won", "_vip_car_won"))
        label = "Casino cars possessed"
    elif category == 32:
        current = int(_number(progression.get("_cashier_seen_with_success_count"))) + int(
            _number(progression.get("_cashier_seen_with_failure_count")))
        label = "Cashier visits"
    elif category == 34:
        current = _bit_count(progression.get("_cloth_bought"))
        label = "Shop clothes owned"
    elif category == 36:
        current = int(_number(progression.get("_friend_count")))
        label = "Casino friends"
    elif category == 37:
        current = int(_number(progression.get("_session_invite_friend_count")))
        label = "Accepted session invitations"
    elif category == 38:
        current = sum(type(value) is int and value >= 0
                      for value in progression.get("_cash_game_unique_opponents_array", []))
        label = "Unique cash-game opponents"
    elif category == 39:
        current = int(_number(progression.get("_cash_game_money_taken_from_opponent")))
        label = "Most money taken"
    elif category in (40, 41, 42):
        poker_rows = _casino_rows(progression, 18) + _casino_rows(progression, 21)
        key = {40: "_win_before_flop_count", 41: "_win_before_turn_count", 42: "_win_before_river_count"}[category]
        current = _count_rows(poker_rows, key)
        label = "Early hands won"
    elif category == 43:
        if filter_a == 4:
            current = sum(_number(item.get("_play_count")) > 0 for item in rows)
        elif filter_a in (17, 20):
            current = sum(_number(item.get("_play_count")) > 0 for item in rows)
        elif filter_a == 19:
            index = filter_b - 23
            current = 1 if 0 <= index < len(rows) and _number(rows[index].get("_play_count")) > 0 else 0
        else:
            # The save stores twelve free/private tables followed by three paying tables.
            selected = rows[:12] if filter_b == 27 else rows[12:]
            current = 1 if any(_number(item.get("_play_count")) > 0 for item in selected) else 0
        label = "Different games played"
    elif category == 45:
        current = sum(type(value) is int and value >= 0
                      for value in progression.get("_cocktail_different_players_offered_to", []))
        label = "Players offered cocktails"
    elif category == 46:
        current = int(_number(progression.get("_sitandgo_joined_private_count")))
        label = "Private games joined"
    elif category == 47:
        current = int(_number(progression.get("_sitandgo_created_private_count")))
        label = "Private games created"
    elif category == 48:
        current = int(_number(progression.get("_sitandgo_played_to_the_end")))
        label = "Sit & Go games finished"
    elif category in (49, 50, 51):
        key = {49: "_cloth_chic_set", 50: "_sticker_set", 51: "_furniture_chic_set"}[category]
        unlockable_hash = {49: 52, 50: 53, 51: 54}[category]
        # The dedicated furniture boolean can remain false after the reward has
        # been claimed.  DBCasinoUnlockable is the persistent game authority.
        unlocked = _casino_unlockable(casino, unlockable_hash) or bool(progression.get(key))
        return _bool_entry(objective_id, unlocked, "Exclusive set unlocked")
    elif category == 52:
        current = _bit_count(progression.get("_glasses_bought"))
        label = "Sunglasses owned"
    elif category == 53:
        current = _bit_count(progression.get("_shoes_bought"))
        label = "Shoes owned"
    elif category == 54:
        seen = progression.get("_roulette_result_seen_array", [])
        if filter_b == 48:
            current = 1 if (seen and (bool(seen[0]) or bool(seen[-1]))) else 0
            label = "Zero results seen"
        else:
            current = sum(bool(value) for value in seen)
            label = "Roulette numbers seen"
    elif category == 55:
        current = int(_number(progression.get("_cocktail_drink_count_on_poker")))
        label = "Poker-table cocktails"
    elif category == 56:
        return _bool_entry(objective_id, progression.get("_rankings_seen"), "Rankings viewed")
    elif category == 57:
        return _bool_entry(objective_id, progression.get("_roulette_used_rebet"), "Rebet used")
    elif category == 58:
        current = _bit_count(progression.get("_satellite_prize_inspected_flag"))
        label = "VIP rewards inspected"
    elif category == 59:
        return _bool_entry(objective_id, progression.get("_roulette_used_historic"), "Roulette history viewed")
    elif category == 60:
        return _bool_entry(objective_id, progression.get("_photo_mode_used"), "MyTDULife photo taken")
    elif category == 61:
        return _bool_entry(objective_id, progression.get("_look_at_mylife_done"), "Own MyTDULife viewed")
    elif category == 62:
        return _bool_entry(objective_id, progression.get("_look_at_mylife_of_other_done"), "Another MyTDULife viewed")
    else:
        raise ValueError(f"Unsupported Casino objective category {category} (hash {reward_hash}).")

    return _entry(objective_id, current, target, label)


def _contest_passed(contests, title):
    row = next((item for item in contests if item.get("name") == title and item.get("verified")), None)
    return bool(row and row.get("passed"))


def _base_progress(data, contests):
    progress = []
    stats = data.get("StatisticsData", {})
    levels = data.get("PlayerLevels", {})

    licence_ids = {
        "1:C4": 1, "1:B4": 2, "1:A7": 3, "1:C3": 4,
        "1:B3": 5, "1:A5": 6, "2:A3": 7, "3:A1": 8,
    }
    licences = {row.get("Name"): bool(row.get("Passed"))
                for row in data.get("PlayerLicenses", {}).get("Licenses", [])}
    for name, objective_id in licence_ids.items():
        progress.append(_bool_entry(objective_id, licences.get(name, False), "Licence passed"))

    for objective_id, reward_slot in zip(range(9, 14), (97, 139, 141, 140, 142)):
        progress.append(_bool_entry(objective_id, _event_xp(data, reward_slot) > 0, "Duel reward recorded"))

    classes = ("C4", "B4", "A7", "A6", "A5", "C3", "B3", "A4", "A3", "A2", "A1")
    objective_id = 14
    for vehicle_class in classes:
        for number in (1, 2):
            progress.append(_bool_entry(
                objective_id,
                _contest_passed(contests, f"{vehicle_class} Championship {number}"),
                "Verified championship result"))
            objective_id += 1

    progress.append(_entry(36, stats.get("MultiRankedWon", 0), 50, "Ranked wins"))
    for objective_id, title in ((37, "Ibiza Cup"), (38, "Hawaii Cup 1"), (39, "Hawaii Cup 2")):
        progress.append(_bool_entry(objective_id, _contest_passed(contests, title), "Verified cup result"))
    instant_wins = sum(int(_number(stats.get(key))) for key in ("ICEvsHumanWon", "ICEvsBotWon"))
    progress.append(_entry(40, instant_wins, 50, "Instant challenges won"))

    distinct_vehicles = len({entry.get("Car", {}).get("Archetype")
                             for entry in data.get("Garage", [])
                             if isinstance(entry, dict) and type(entry.get("Car", {}).get("Archetype")) is int})
    for objective_id, target in zip(range(41, 51), (1, 3, 6, 10, 15, 20, 25, 30, 40, 50)):
        progress.append(_entry(objective_id, distinct_vehicles, target, "Different vehicles owned"))

    haircuts = int(_number(stats.get("NbHaircutsBought")))
    for objective_id, target in zip(range(51, 54), (2, 5, 10)):
        progress.append(_entry(objective_id, haircuts, target, "Haircuts purchased"))

    houses = len(data.get("MyHouse", {}).get("House", []))
    for objective_id, target in zip(range(54, 59), (1, 3, 6, 10, 15)):
        progress.append(_entry(objective_id, houses, target, "Houses owned"))
    furniture = int(_number(data.get("MyHouse", {}).get("NbBoughtFurnitures")))
    for objective_id, target in zip(range(59, 63), (5, 10, 20, 35)):
        progress.append(_entry(objective_id, furniture, target, "Furniture purchased"))

    progress.append(_entry(63, _event_xp(data, 114), 4, "Cosmetic operations recorded"))
    clothes = int(_number(stats.get("NbClothesBought")))
    for objective_id, target in zip(range(64, 68), (5, 10, 20, 30)):
        progress.append(_entry(objective_id, clothes, target, "Clothes purchased"))
    sticker_boards = len(set(value for value in data.get("Stickers", {}).get("OwnedStickers", [])
                             if type(value) is int and value > 0))
    for objective_id, target in zip(range(68, 71), (2, 5, 10)):
        progress.append(_entry(objective_id, sticker_boards, target, "Owned sticker entries"))

    progress.extend(_road_progress(data, levels))

    wrecks = data.get("Wrecks", {})
    ibiza_wrecks = wrecks.get("Wrecks_IBIZA", [])
    hawaii_wrecks = wrecks.get("Wrecks_HAWAI", [])
    for objective_id, start, count in ((101, 0, 10), (102, 14, 10), (103, 10, 4), (104, 24, 4)):
        progress.append(_entry(objective_id, _count_taken(ibiza_wrecks, start, count), count, "Wrecks found"))
    for area in range(4):
        progress.append(_entry(105 + area, _count_taken(hawaii_wrecks, area * 13, 10), 10, "Wrecks found"))
    for area in range(4):
        progress.append(_entry(109 + area, _count_taken(hawaii_wrecks, area * 13 + 10, 3), 3,
                               "Additional wrecks found"))

    event_save = data.get("EventsSave", {})
    base_states = {row.get("Hashcode"): row.get("State") for row in event_save.get("Events", [])}
    dlc_states = {row.get("Hashcode"): row.get("State") for row in event_save.get("DLCEvents", [])}

    def event_entry(objective, mode, island, area, target):
        hashes = EVENT_AREAS[(mode, island, area)]
        states = dlc_states if mode == "dlc" else base_states
        won = sum(int(_number(states.get(event_hash))) >= 5 for event_hash in hashes)
        # The installed DB has 18 rather than 20 catalogue rows for Ibiza area 2.
        # Treat all catalogue rows won as the game's target while retaining that fact in the detail.
        normalized = target if won == len(hashes) else min(won, target - 1)
        entry = _entry(objective, normalized, target, "Area events won")
        entry["detail"] = f"Area events won: {won}/{len(hashes)} catalogued (objective target {target})"
        return entry

    progress.extend((
        event_entry(113, "base", "Ibiza", 1, 20),
        event_entry(114, "base", "Ibiza", 2, 20),
        event_entry(115, "dlc", "Ibiza", 1, 4),
        event_entry(116, "dlc", "Ibiza", 2, 4),
    ))
    for area in range(1, 5):
        progress.append(event_entry(116 + area, "base", "Hawaii", area, 15))
    for area in range(1, 5):
        progress.append(event_entry(120 + area, "dlc", "Hawaii", area, 3))

    photos = data.get("PhotosGP", {}).get("PhotosGP", [])
    progress.append(_entry(125, _count_taken(photos, 0, 20), 20, "Viewpoints found"))
    progress.append(_entry(126, _count_taken(photos, 20, 20), 20, "Viewpoints found"))

    progress.append(_entry(127, len(data.get("PlayersList", [])), 5, "Friends in player list"))
    progress.append(_entry(128, stats.get("NbFriendsInvited", 0), 10, "Friends invited home"))
    progress.append(_entry(129, int(_number(stats.get("ChaseHunterWon"))) +
                           int(_number(stats.get("ChaseHunterLost"))), 20, "Cop chases played"))
    progress.append(_entry(130, int(_number(stats.get("ChasePreyWon"))) +
                           int(_number(stats.get("ChasePreyLost"))), 20, "Outlaw chases played"))
    progress.append(_entry(131, int(_number(stats.get("ChaseHunterWon"))) +
                           int(_number(stats.get("ChasePreyWon"))), 20, "Chases won"))
    progress.append(_bool_entry(132, _event_xp(data, 10) > 0, "Club membership reward"))
    progress.append(_entry(133, int(_number(stats.get("ClubWon"))) + int(_number(stats.get("ClubLost"))),
                           50, "Inner-club challenges"))
    progress.append(_entry(134, int(_number(stats.get("InterClubWonByTeam"))) +
                           int(_number(stats.get("InterClubLostByTeam"))), 50, "Versus-club challenges"))
    progress.append(_bool_entry(135, _event_xp(data, 137) > 0, "Club level 2 reward"))
    progress.append(_bool_entry(136, _event_xp(data, 136) > 0, "Club level 3 reward"))
    progress.append(_entry(137, int(_number(stats.get("FTLWon"))) + int(_number(stats.get("FTLLost"))),
                           30, "Follow The Leader games"))
    progress.append(_entry(138, int(_number(stats.get("KYDWon"))) + int(_number(stats.get("KYDLost"))),
                           30, "Keep Your Distance games"))
    codriving = max(_event_xp(data, 135), int(_number(stats.get("NbCodrivingAsPilot"))) +
                    int(_number(stats.get("NbCodrivingAsCopilot"))))
    progress.append(_entry(139, codriving, 20, "Co-driving sessions"))
    progress.append(_entry(140, stats.get("CrcChaRuns", 0), 25, "CRC challenges played"))
    progress.append(_entry(141, _event_xp(data, 177), 15, "CRC challenges uploaded"))
    return progress


def build_objective_progress(data, contests):
    """Return one evidence-backed progress row for every Track Records objective."""
    progress = _base_progress(data, contests)
    progress.extend(_casino_progress(data, row) for row in CASINO_OBJECTIVES)
    ids = [row["objective_id"] for row in progress]
    if len(ids) != 286 or len(set(ids)) != 286 or min(ids) != 1 or max(ids) != 286:
        raise ValueError("Objective progress map is incomplete or contains duplicate IDs.")
    return sorted(progress, key=lambda row: row["objective_id"])
