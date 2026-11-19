"""Read a TDU2 DATA save and emit a Track Records import snapshot.

This packaged decoder is intentionally read-only: it opens the selected save,
prints one JSON object to stdout, and never writes beside or back to the save.
"""

import argparse
import hashlib
import json
import math
import os
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from functools import lru_cache
from pathlib import Path

import tdu2_save_tool as engine
from objective_progress import build_objective_progress


ROOT = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
CONTEST_REFERENCE = ROOT / "data" / "contest_reference.json"
HOUSE_REFERENCE = ROOT / "data" / "house_reference.json"
MAX_SAVE_SIZE = 64 * 1024 * 1024


@dataclass(frozen=True)
class SaveSession:
    path: Path
    original: bytes
    data: dict
    profile: str


def resolve_data_path(path):
    # The importer identifies the source and profile by the path it selected.
    # Keep that path intact when a folder is a junction or symbolic link.
    path = Path(os.path.abspath(Path(path).expanduser()))
    if path.is_dir():
        if (path / "PLAYERSAVE").is_dir():
            path /= "PLAYERSAVE"
        path /= "DATA"
    if not path.is_file() or path.name.upper() != "DATA":
        raise ValueError("Choose a DATA file, PLAYERSAVE folder, or player profile folder.")
    return path


def load_save(path, profile=None):
    path = resolve_data_path(path)
    if path.stat().st_size > MAX_SAVE_SIZE:
        raise ValueError("This file exceeds the decoder's 64 MB save size limit.")
    raw = path.read_bytes()
    identity = profile or engine.detect_profile_name(path, raw)
    try:
        xmbf, _, name = engine.decrypt_save_file(raw, identity, path.name.upper())
    except ValueError:
        if profile:
            raise
        xmbf, _, name = engine.decrypt_save_file(raw, None, path.name.upper())
    if name.upper() != "DATA" or not xmbf.startswith(b"XMBF"):
        raise ValueError("This is not a supported TDU2 DATA save container.")
    root, data = engine.decode_xmbf_to_dict(xmbf)
    if root != "PlayerData" or not isinstance(data.get("Driver"), dict):
        raise ValueError("The selected DATA file does not contain a PlayerData driver.")
    label = data["Driver"].get("Name") or path.parent.parent.name
    return SaveSession(path, raw, data, str(label))


@lru_cache(maxsize=1)
def contest_references():
    rows = json.loads(CONTEST_REFERENCE.read_text(encoding="utf-8"))["events"]
    return {row["id"]: row for row in rows}


def contest_reference(row):
    reference = contest_references().get(row.get("ID"))
    if reference and row.get("Name") == reference["saved_name"] and [
        race.get("File") for race in row.get("Challenge", [])
    ] == reference["challenge_files"]:
        return reference
    return None


def contest_title(row):
    reference = contest_reference(row)
    return reference["title"] if reference else str(row.get("Name", "Unknown contest"))


def _source_signature(path):
    stat = path.stat()
    return stat.st_size, stat.st_mtime_ns


def build_snapshot(session, path):
    garage = session.data.get("Garage", [])
    stats = session.data.get("StatisticsData", {})
    levels = session.data.get("PlayerLevels", {})
    cruising_levels = levels.get("CruisingLevels", {})

    def category_points(key):
        value = levels.get(key)
        return value if type(value) is int and value >= 0 else None

    discovery_values = [cruising_levels.get(f"Area_{area}") for area in range(6)]
    discovery_points = (
        sum(discovery_values)
        if all(type(value) is int and value >= 0 for value in discovery_values)
        else None
    )

    def played(wins_key, losses_key):
        wins, losses = stats.get(wins_key), stats.get(losses_key)
        if type(wins) is int and type(losses) is int and wins >= 0 and losses >= 0:
            return wins + losses
        return None

    house_slots = {}
    for array_index, entry in enumerate(garage):
        if not isinstance(entry, dict):
            continue
        house_hash = entry.get("House")
        stored_slot = entry.get("IndexInGarage", array_index)
        if type(house_hash) is not int or house_hash <= 0:
            raise ValueError(f"Garage entry {array_index} has no valid house identity.")
        if type(stored_slot) is not int or stored_slot < 0:
            raise ValueError(f"Garage entry {array_index} has an invalid global slot.")
        house_slots.setdefault(house_hash, []).append(stored_slot)

    local_slots = {}
    for house_hash, stored_slots in house_slots.items():
        ordered_slots = sorted(stored_slots)
        if len(ordered_slots) != len(set(ordered_slots)):
            raise ValueError(f"House {house_hash} contains duplicate global garage slots.")
        for local_slot, stored_slot in enumerate(ordered_slots, start=1):
            local_slots[(house_hash, stored_slot)] = local_slot

    vehicles = []
    instance_ids = set()
    for array_index, entry in enumerate(garage):
        if not isinstance(entry, dict) or not isinstance(entry.get("Car"), dict):
            continue
        car = entry["Car"]
        vehicle_id = car.get("Archetype")
        if type(vehicle_id) is not int or vehicle_id <= 0:
            continue
        instance_id = car.get("ID")
        mileage = car.get("OdometerKms")
        stored_slot = entry.get("IndexInGarage", array_index)
        house_hash = entry.get("House")
        if type(instance_id) is not int or instance_id < 0:
            raise ValueError(f"Garage entry {array_index} has no valid per-car instance ID.")
        if instance_id in instance_ids:
            raise ValueError(f"Car.ID {instance_id} occurs more than once in this save.")
        if type(stored_slot) is not int or stored_slot < 0:
            raise ValueError(f"Car.ID {instance_id} has an invalid garage slot.")
        if type(mileage) not in (int, float) or not math.isfinite(mileage) or mileage < 0:
            raise ValueError(f"Car.ID {instance_id} has invalid mileage.")
        instance_ids.add(instance_id)
        upgrades = car.get("Car Upgrades")
        if not isinstance(upgrades, dict):
            upgrades = {}
        upgrade_levels = [upgrades.get(key) for key in ("Acceleration", "Top Speed", "Braking")]
        if not all(type(level) is int and 0 <= level <= 255 for level in upgrade_levels):
            upgrade_levels = None
        vehicles.append({
            "instance_id": instance_id,
            "vehicle_id": vehicle_id,
            "garage_index": stored_slot,
            "house_hash": house_hash,
            "garage_slot": local_slots[(house_hash, stored_slot)],
            "garage_capacity": len(house_slots[house_hash]),
            "mileage_km": float(mileage),
            "upgrade_levels": upgrade_levels,
        })

    contests = []
    for contest in session.data.get("PlayerLicenses", {}).get("Contests", []):
        races = []
        for race in contest.get("Challenge", []):
            races.append({
                "file": race.get("File"),
                "success": race.get("Success"),
                "score": race.get("Score"),
                "first_time_score": race.get("FirstTimeScore"),
                "contest_scores": race.get("ContestScores"),
                "success_date": race.get("S_Date"),
                "try_date": race.get("T_Date"),
                "retries": race.get("NbRetries"),
            })
        contests.append({
            "contest_id": contest.get("ID"),
            "name": contest_title(contest),
            "passed": contest.get("Passed"),
            "cup": contest.get("Cup"),
            "state": contest.get("State"),
            "total_time": contest.get("TotalTime"),
            "races": races,
            "recorded_rounds": sum(
                any(score != 0 for score in (race["contest_scores"] or [])) for race in races
            ),
            "round_count": len(races),
            "verified": contest_reference(contest) is not None,
        })

    return {
        "format_version": 1,
        "profile_key": path.parent.parent.name if path.parent.name.upper() == "PLAYERSAVE" else session.profile,
        "profile": session.profile,
        "captured_utc": datetime.now(timezone.utc).isoformat(),
        "source": {
            "path": str(path),
            "size": len(session.original),
            "sha256": hashlib.sha256(session.original).hexdigest(),
        },
        "vehicles": vehicles,
        "contest_results": contests,
        "licences": [
            {"name": row.get("Name"), "passed": row.get("Passed")}
            for row in session.data.get("PlayerLicenses", {}).get("Licenses", [])
        ],
        "ranked_wins": stats.get("MultiRankedWon"),
        "inner_club_played": played("ClubWon", "ClubLost"),
        "versus_club_played": played("InterClubWonByTeam", "InterClubLostByTeam"),
        "house_hashes": sorted({
            row["HouseHC"]
            for row in session.data.get("MyHouse", {}).get("House", [])
            if isinstance(row, dict) and type(row.get("HouseHC")) is int and row["HouseHC"] > 0
        }),
        "house_reference": [
            {"hash": house_hash, "island": island, "name": name}
            for house_hash, island, name in json.loads(HOUSE_REFERENCE.read_text(encoding="utf-8"))["houses"]
        ],
        "event_xps": session.data.get("Rewards", {}).get("EventXPs"),
        "objective_progress": build_objective_progress(session.data, contests),
        "roads_unlocked": levels.get("AreaRoadsUnlocked"),
        "roads_total": levels.get("AreaTotalRoads"),
        "competition_points": category_points("RacingLevel"),
        "collection_points": category_points("CollectionLevel"),
        "discovery_points": discovery_points,
        "social_points": category_points("SocialLevel"),
    }


def read_live_snapshot(path, profile=None):
    path = resolve_data_path(path)
    last_error = None
    for attempt in range(3):
        before = _source_signature(path)
        try:
            session = load_save(path, profile)
        except (OSError, ValueError) as exc:
            last_error = exc
            after = _source_signature(path)
            if before == after and not isinstance(exc, OSError):
                raise
        else:
            after = _source_signature(path)
            if before == after and len(session.original) == after[0]:
                return build_snapshot(session, path)
            last_error = ValueError("DATA changed while it was being read.")
        if attempt < 2:
            time.sleep(0.35)
    raise ValueError(
        "TDU2 was still writing DATA during all three read attempts. "
        "Wait for the in-game save indicator to finish, then sync again."
    ) from last_error


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Emit a fresh Track Records snapshot from a TDU2 DATA save.")
    parser.add_argument("path", help="DATA file, PLAYERSAVE folder, or profile folder")
    parser.add_argument("--profile", help="Encryption profile name when automatic detection is unavailable")
    args = parser.parse_args()
    try:
        snapshot = read_live_snapshot(args.path, args.profile)
        json.dump(snapshot, sys.stdout, ensure_ascii=False, separators=(",", ":"))
        sys.stdout.write("\n")
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
