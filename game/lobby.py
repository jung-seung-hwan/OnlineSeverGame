from __future__ import annotations

from dataclasses import dataclass

MIN_CAPACITY = 1
MAX_CAPACITY = 8


class RoomFull(Exception):
    pass


@dataclass(frozen=True)
class RoomInfo:
    id: int
    name: str
    capacity: int
    players: tuple[str, ...]


@dataclass
class _Room:
    id: int
    name: str
    capacity: int
    players: list[str]


def _check_text(value: object, what: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{what} must be a non-empty string")


class Lobby:
    def __init__(self) -> None:
        self._rooms: dict[int, _Room] = {}
        self._player_room: dict[str, int] = {}
        self._next_id = 1

    def create_room(self, name: str, capacity: int) -> int:
        _check_text(name, "room name")
        if (
            isinstance(capacity, bool)
            or not isinstance(capacity, int)
            or not MIN_CAPACITY <= capacity <= MAX_CAPACITY
        ):
            raise ValueError(
                f"capacity must be an int in {MIN_CAPACITY}..{MAX_CAPACITY}"
            )
        room_id = self._next_id
        self._next_id += 1
        self._rooms[room_id] = _Room(room_id, name, capacity, [])
        return room_id

    def join(self, room_id: int, player: str) -> None:
        _check_text(player, "player name")
        if isinstance(room_id, bool) or not isinstance(room_id, int):
            raise KeyError(room_id)
        room = self._rooms.get(room_id)
        if room is None:
            raise KeyError(room_id)
        if len(room.players) >= room.capacity:
            raise RoomFull(room_id)
        if player in self._player_room:
            raise ValueError(f"player {player!r} is already in a room")
        room.players.append(player)
        self._player_room[player] = room_id

    def leave(self, player: str) -> None:
        room_id = self._player_room.get(player)
        if room_id is None:
            raise KeyError(player)
        room = self._rooms[room_id]
        room.players.remove(player)
        del self._player_room[player]
        if not room.players:
            del self._rooms[room_id]

    def rooms(self) -> list[RoomInfo]:
        return [
            RoomInfo(r.id, r.name, r.capacity, tuple(r.players))
            for _, r in sorted(self._rooms.items())
        ]

    def room_of(self, player: str) -> int | None:
        return self._player_room.get(player)
