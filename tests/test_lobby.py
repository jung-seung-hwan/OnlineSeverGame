import dataclasses
import unittest

from game.lobby import Lobby, RoomFull, RoomInfo


class CreateRoomTests(unittest.TestCase):
    def setUp(self):
        self.lobby = Lobby()

    def test_ids_start_at_one_and_increase(self):
        self.assertEqual(self.lobby.create_room("a", 2), 1)
        self.assertEqual(self.lobby.create_room("b", 2), 2)
        self.assertEqual(self.lobby.create_room("c", 2), 3)

    def test_capacity_bounds_accepted(self):
        self.lobby.create_room("min", 1)
        self.lobby.create_room("max", 8)
        self.assertEqual([r.capacity for r in self.lobby.rooms()], [1, 8])

    def test_capacity_out_of_range_rejected(self):
        for cap in (0, 9, -1, 100):
            with self.subTest(cap=cap):
                with self.assertRaises(ValueError):
                    self.lobby.create_room("x", cap)
        self.assertEqual(self.lobby.rooms(), [])

    def test_capacity_non_int_rejected(self):
        for cap in (True, False, 2.0, "2", None):
            with self.subTest(cap=cap):
                with self.assertRaises(ValueError):
                    self.lobby.create_room("x", cap)

    def test_blank_room_name_rejected(self):
        for name in ("", "   ", "\t\n", None, 5):
            with self.subTest(name=name):
                with self.assertRaises(ValueError):
                    self.lobby.create_room(name, 2)

    def test_failed_create_does_not_consume_id(self):
        with self.assertRaises(ValueError):
            self.lobby.create_room("x", 0)
        self.assertEqual(self.lobby.create_room("ok", 2), 1)

    def test_never_joined_room_is_kept(self):
        rid = self.lobby.create_room("empty", 3)
        self.assertEqual([r.id for r in self.lobby.rooms()], [rid])


class JoinTests(unittest.TestCase):
    def setUp(self):
        self.lobby = Lobby()
        self.rid = self.lobby.create_room("r", 2)

    def test_join_records_players_in_order(self):
        self.lobby.join(self.rid, "bob")
        self.lobby.join(self.rid, "alice")
        self.assertEqual(self.lobby.rooms()[0].players, ("bob", "alice"))
        self.assertEqual(self.lobby.room_of("bob"), self.rid)
        self.assertEqual(self.lobby.room_of("alice"), self.rid)

    def test_join_missing_room_raises_key_error(self):
        with self.assertRaises(KeyError):
            self.lobby.join(99, "bob")
        self.assertIsNone(self.lobby.room_of("bob"))

    def test_join_full_room_raises_room_full(self):
        self.lobby.join(self.rid, "a")
        self.lobby.join(self.rid, "b")
        with self.assertRaises(RoomFull):
            self.lobby.join(self.rid, "c")
        self.assertEqual(self.lobby.rooms()[0].players, ("a", "b"))
        self.assertIsNone(self.lobby.room_of("c"))

    def test_room_full_is_plain_exception_subclass(self):
        self.assertTrue(issubclass(RoomFull, Exception))
        self.assertFalse(issubclass(RoomFull, (ValueError, KeyError)))

    def test_join_twice_same_room_raises_value_error(self):
        self.lobby.join(self.rid, "a")
        with self.assertRaises(ValueError):
            self.lobby.join(self.rid, "a")
        self.assertEqual(self.lobby.rooms()[0].players, ("a",))

    def test_join_second_room_raises_value_error(self):
        other = self.lobby.create_room("other", 2)
        self.lobby.join(self.rid, "a")
        with self.assertRaises(ValueError):
            self.lobby.join(other, "a")
        self.assertEqual(self.lobby.room_of("a"), self.rid)
        self.assertEqual(self.lobby.rooms()[1].players, ())

    def test_blank_player_name_rejected(self):
        for player in ("", "  ", "\t", None, 3):
            with self.subTest(player=player):
                with self.assertRaises(ValueError):
                    self.lobby.join(self.rid, player)
        self.assertEqual(self.lobby.rooms()[0].players, ())

    def test_non_int_room_id_raises_key_error(self):
        for room_id in (True, 1.0, [1], "1", None):
            with self.subTest(room_id=room_id):
                with self.assertRaises(KeyError):
                    self.lobby.join(room_id, "bob")
        self.assertIsNone(self.lobby.room_of("bob"))
        self.assertEqual(self.lobby.rooms()[0].players, ())

    def test_blank_player_checked_before_missing_room(self):
        with self.assertRaises(ValueError):
            self.lobby.join(99, " ")

    def test_full_room_checked_before_already_in_room(self):
        other = self.lobby.create_room("other", 1)
        self.lobby.join(other, "x")
        self.lobby.join(self.rid, "a")
        with self.assertRaises(RoomFull):
            self.lobby.join(other, "a")
        self.assertEqual(self.lobby.room_of("a"), self.rid)

    def test_joining_own_full_room_raises_room_full(self):
        self.lobby.join(self.rid, "a")
        self.lobby.join(self.rid, "b")
        with self.assertRaises(RoomFull):
            self.lobby.join(self.rid, "a")
        self.assertEqual(self.lobby.rooms()[0].players, ("a", "b"))


class LeaveTests(unittest.TestCase):
    def setUp(self):
        self.lobby = Lobby()

    def test_leave_when_not_in_room_raises_key_error(self):
        with self.assertRaises(KeyError):
            self.lobby.leave("ghost")

    def test_leave_twice_raises_key_error(self):
        rid = self.lobby.create_room("r", 2)
        self.lobby.join(rid, "a")
        self.lobby.join(rid, "b")
        self.lobby.leave("a")
        with self.assertRaises(KeyError):
            self.lobby.leave("a")

    def test_leave_keeps_room_with_remaining_players(self):
        rid = self.lobby.create_room("r", 3)
        for p in ("a", "b", "c"):
            self.lobby.join(rid, p)
        self.lobby.leave("b")
        self.assertEqual(self.lobby.rooms()[0].players, ("a", "c"))
        self.assertIsNone(self.lobby.room_of("b"))

    def test_leaving_last_player_deletes_room(self):
        rid = self.lobby.create_room("r", 2)
        self.lobby.join(rid, "a")
        self.lobby.leave("a")
        self.assertEqual(self.lobby.rooms(), [])
        self.assertIsNone(self.lobby.room_of("a"))
        with self.assertRaises(KeyError):
            self.lobby.join(rid, "b")

    def test_player_can_rejoin_after_leaving(self):
        r1 = self.lobby.create_room("r1", 1)
        r2 = self.lobby.create_room("r2", 1)
        self.lobby.join(r1, "a")
        self.lobby.leave("a")
        self.lobby.join(r2, "a")
        self.assertEqual(self.lobby.room_of("a"), r2)

    def test_leave_frees_slot_in_partly_full_room(self):
        rid = self.lobby.create_room("r", 2)
        self.lobby.join(rid, "a")
        self.lobby.join(rid, "b")
        self.lobby.leave("a")
        self.lobby.join(rid, "c")
        self.assertEqual(self.lobby.rooms()[0].players, ("b", "c"))


class IdAndListingTests(unittest.TestCase):
    def setUp(self):
        self.lobby = Lobby()

    def test_ids_never_reused_after_deletion(self):
        r1 = self.lobby.create_room("a", 1)
        self.lobby.join(r1, "p")
        self.lobby.leave("p")
        r2 = self.lobby.create_room("b", 1)
        self.assertEqual((r1, r2), (1, 2))

    def test_rooms_ordered_by_id(self):
        ids = [self.lobby.create_room(f"r{i}", 2) for i in range(4)]
        self.lobby.join(ids[0], "p")
        self.lobby.leave("p")
        self.assertEqual([r.id for r in self.lobby.rooms()], ids[1:])

    def test_room_info_contents_and_frozen(self):
        rid = self.lobby.create_room("lobby", 4)
        self.lobby.join(rid, "a")
        info = self.lobby.rooms()[0]
        self.assertEqual(info, RoomInfo(rid, "lobby", 4, ("a",)))
        self.assertIsInstance(info.players, tuple)
        self.assertTrue(dataclasses.is_dataclass(info))
        with self.assertRaises(dataclasses.FrozenInstanceError):
            info.name = "other"

    def test_rooms_returns_copies(self):
        rid = self.lobby.create_room("r", 2)
        self.lobby.join(rid, "a")
        listing = self.lobby.rooms()
        listing.clear()
        self.assertEqual(len(self.lobby.rooms()), 1)
        snapshot = self.lobby.rooms()
        self.lobby.join(rid, "b")
        self.assertEqual(snapshot[0].players, ("a",))

    def test_room_of_unknown_player_is_none(self):
        self.assertIsNone(self.lobby.room_of("nobody"))

    def test_lobbies_are_independent(self):
        other = Lobby()
        rid = self.lobby.create_room("r", 2)
        self.lobby.join(rid, "a")
        self.assertEqual(other.rooms(), [])
        self.assertEqual(other.create_room("x", 1), 1)
        other.join(1, "a")


if __name__ == "__main__":
    unittest.main()
