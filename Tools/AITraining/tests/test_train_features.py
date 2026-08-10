from __future__ import annotations

import gzip
import json
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
from arkcard_ai.train import (
    EpochMetrics,
    _history_path,
    iter_training_records,
    iter_validation_records,
    load_history,
    partition_game_ids,
    prefetch_records,
    save_history,
)


def _write_shard(path, records):
    def record_payload(seed, game_id, ply, outcome, visits):
        payload = bytearray()
        state = np.zeros(STATE_FEATURE_COUNT, dtype="<f4")
        state[0] = 0.25
        action = np.zeros(ACTION_FEATURE_COUNT, dtype="<f4")
        action[3] = 1.0
        payload.extend(struct.pack("<qiiif", seed, game_id, ply, 1, outcome))
        payload.extend(struct.pack("<i3i", 3, 1, 2, 3))
        payload.extend(struct.pack("<i2i", 2, 4, 5))
        payload.extend(state.tobytes())
        payload.extend(struct.pack("<i", 1))
        payload.extend(action.tobytes())
        payload.extend(struct.pack("<i", visits))
        return payload

    with gzip.open(path, "wb") as stream:
        stream.write(MAGIC)
        stream.write(
            struct.pack(
                "<iiii",
                FORMAT_VERSION,
                SCHEMA_VERSION,
                STATE_FEATURE_COUNT,
                ACTION_FEATURE_COUNT,
            )
        )
        for seed, game_id, ply, outcome, visits in records:
            payload = record_payload(seed, game_id, ply, outcome, visits)
            stream.write(struct.pack("<i", len(payload)))
            stream.write(payload)
        stream.write(struct.pack("<i", 0))


def test_partition_game_ids_deterministic_and_no_leakage(tmp_path):
    shard = tmp_path / "synthetic.arkds.gz"
    _write_shard(shard, [(1000 + game, game, ply, 0.0, 17) for game in range(20) for ply in (0, 1)])

    first = partition_game_ids([shard], 0.5, 20260806)
    second = partition_game_ids([shard], 0.5, 20260806)
    assert first == second
    assert 0 < len(first) < 20

    train_ids = {record.game_id for record in iter_training_records([shard], first, 20260806, 32)}
    validation_ids = {record.game_id for record in iter_validation_records([shard], first)}
    assert train_ids.isdisjoint(validation_ids)
    assert train_ids | validation_ids == set(range(20))
    assert train_ids == set(range(20)) - first
    assert validation_ids == first


def test_partition_fraction_zero_disables_validation(tmp_path):
    shard = tmp_path / "synthetic.arkds.gz"
    _write_shard(shard, [(1, 0, 0, 0.0, 17)])
    assert partition_game_ids([shard], 0.0, 20260806) == set()


def test_partition_rejects_full_validation(tmp_path):
    shard = tmp_path / "synthetic.arkds.gz"
    _write_shard(shard, [(1, 0, 0, 0.0, 17)])
    try:
        partition_game_ids([shard], 1.0, 20260806)
    except ValueError:
        return
    raise AssertionError("validation-fraction 1.0 must be rejected")


def test_prefetch_records_preserves_order_and_content(tmp_path):
    shard = tmp_path / "synthetic.arkds.gz"
    _write_shard(shard, [(game, game, 0, 0.0, 17) for game in range(25)])
    records = list(iter_shard(shard))

    assert [record.game_id for record in prefetch_records(records, 8)] == list(range(25))
    assert [record.game_id for record in prefetch_records(records, 1)] == list(range(25))


def test_history_load_append_roundtrip(tmp_path):
    output = tmp_path / "run"
    output.mkdir()
    _history_path(output).write_text(
        json.dumps(
            [
                {
                    "epoch": 19,
                    "samples": 10,
                    "policy_loss": 1.0,
                    "value_loss": 0.5,
                    "total_loss": 1.5,
                }
            ]
        ),
        encoding="utf-8",
    )

    history = load_history(output)
    history.append(
        EpochMetrics(
            epoch=20,
            samples=10,
            policy_loss=0.9,
            value_loss=0.4,
            total_loss=1.3,
            validation_samples=2,
            validation_policy_loss=0.95,
            validation_value_loss=0.45,
            validation_total_loss=1.4,
        )
    )
    save_history(output, history)

    reloaded = load_history(output)
    assert [item.epoch for item in reloaded] == [19, 20]
    assert reloaded[1].validation_total_loss == 1.4
    assert not (_history_path(output).with_suffix(".json.tmp")).exists()
