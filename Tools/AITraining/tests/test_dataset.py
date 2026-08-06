from __future__ import annotations

import gzip
import struct

import numpy as np

from arkcard_ai.dataset import (
    ACTION_FEATURE_COUNT,
    FORMAT_VERSION,
    MAGIC,
    SCHEMA_VERSION,
    STATE_FEATURE_COUNT,
    iter_shard,
)


def test_reads_unity_binary_contract(tmp_path):
    state = np.zeros(STATE_FEATURE_COUNT, dtype="<f4")
    state[0] = 0.25
    action = np.zeros(ACTION_FEATURE_COUNT, dtype="<f4")
    action[3] = 1.0
    payload = bytearray()
    payload.extend(struct.pack("<qiiif", 123456789, 7, 13, 1, -1.0))
    payload.extend(struct.pack("<i3i", 3, 1, 2, 3))
    payload.extend(struct.pack("<i2i", 2, 4, 5))
    payload.extend(state.tobytes())
    payload.extend(struct.pack("<i", 1))
    payload.extend(action.tobytes())
    payload.extend(struct.pack("<i", 17))

    path = tmp_path / "fixture.arkds.gz"
    with gzip.open(path, "wb") as stream:
        stream.write(MAGIC)
        stream.write(struct.pack("<iiii", FORMAT_VERSION, SCHEMA_VERSION, STATE_FEATURE_COUNT, ACTION_FEATURE_COUNT))
        stream.write(struct.pack("<i", len(payload)))
        stream.write(payload)
        stream.write(struct.pack("<i", 0))

    records = list(iter_shard(path))
    assert len(records) == 1
    record = records[0]
    assert record.seed == 123456789
    assert record.game_id == 7
    assert record.ply == 13
    assert record.observer_player_index == 1
    assert record.outcome == -1.0
    np.testing.assert_array_equal(record.first_deck, [1, 2, 3])
    np.testing.assert_array_equal(record.second_deck, [4, 5])
    assert record.state[0] == 0.25
    assert record.actions[0, 3] == 1.0
    assert record.visits.tolist() == [17]
    np.testing.assert_allclose(record.policy_target, [1.0])
