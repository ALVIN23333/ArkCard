from __future__ import annotations

import gzip
import random
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, Iterable, Iterator, Sequence

import numpy as np

MAGIC = b"ARKDS001"
FORMAT_VERSION = 1
SCHEMA_VERSION = 1
STATE_FEATURE_COUNT = 1892
ACTION_FEATURE_COUNT = 655
MAX_RECORD_BYTES = 64 * 1024 * 1024


@dataclass(slots=True)
class TrainingRecord:
    seed: int
    game_id: int
    ply: int
    observer_player_index: int
    outcome: float
    first_deck: np.ndarray
    second_deck: np.ndarray
    state: np.ndarray
    actions: np.ndarray
    visits: np.ndarray

    @property
    def policy_target(self) -> np.ndarray:
        total = int(self.visits.sum())
        if total <= 0:
            raise ValueError("record has no policy visits")
        return self.visits.astype(np.float32) / float(total)


def find_shards(paths: Sequence[str | Path]) -> list[Path]:
    shards: list[Path] = []
    for raw_path in paths:
        path = Path(raw_path)
        if path.is_dir():
            shards.extend(path.rglob("*.arkds.gz"))
        elif any(character in str(path) for character in "*?["):
            shards.extend(path.parent.glob(path.name))
        elif path.is_file():
            shards.append(path)
    unique = sorted({item.resolve() for item in shards})
    if not unique:
        raise FileNotFoundError("no .arkds.gz dataset shards were found")
    return unique


def iter_shard(path: str | Path) -> Iterator[TrainingRecord]:
    with gzip.open(path, "rb") as stream:
        _read_header(stream)
        while True:
            (record_size,) = _unpack(stream, "<i")
            if record_size == 0:
                return
            if record_size < 0 or record_size > MAX_RECORD_BYTES:
                raise ValueError(f"invalid dataset record size: {record_size}")
            payload = memoryview(_read_exact(stream, record_size))
            yield _decode_record(payload)


def iter_records(
    shards: Sequence[str | Path],
    *,
    seed: int = 0,
    shuffle_shards: bool = False,
    shuffle_buffer: int = 0,
) -> Iterator[TrainingRecord]:
    paths = find_shards(shards)
    generator = random.Random(seed)
    if shuffle_shards:
        generator.shuffle(paths)

    records: Iterable[TrainingRecord] = (
        record for path in paths for record in iter_shard(path)
    )
    if shuffle_buffer <= 1:
        yield from records
        return

    buffer: list[TrainingRecord] = []
    for record in records:
        if len(buffer) < shuffle_buffer:
            buffer.append(record)
            continue
        index = generator.randrange(len(buffer))
        yield buffer[index]
        buffer[index] = record
    generator.shuffle(buffer)
    yield from buffer


def _read_header(stream: BinaryIO) -> None:
    magic = _read_exact(stream, len(MAGIC))
    if magic != MAGIC:
        raise ValueError("invalid ArkCard dataset magic")
    format_version, schema_version, state_count, action_count = _unpack(stream, "<iiii")
    expected = (FORMAT_VERSION, SCHEMA_VERSION, STATE_FEATURE_COUNT, ACTION_FEATURE_COUNT)
    actual = (format_version, schema_version, state_count, action_count)
    if actual != expected:
        raise ValueError(f"incompatible dataset header: expected {expected}, got {actual}")


def _decode_record(payload: memoryview) -> TrainingRecord:
    offset = 0

    def unpack(fmt: str) -> tuple:
        nonlocal offset
        size = struct.calcsize(fmt)
        if offset + size > len(payload):
            raise EOFError("truncated ArkCard dataset record")
        values = struct.unpack_from(fmt, payload, offset)
        offset += size
        return values

    seed, game_id, ply, observer, outcome = unpack("<qiiif")

    def read_int_array() -> np.ndarray:
        nonlocal offset
        (count,) = unpack("<i")
        if count < 0 or count > 10_000:
            raise ValueError(f"invalid integer array length: {count}")
        byte_count = count * 4
        if offset + byte_count > len(payload):
            raise EOFError("truncated integer array")
        result = np.frombuffer(payload[offset : offset + byte_count], dtype="<i4").copy()
        offset += byte_count
        return result

    first_deck = read_int_array()
    second_deck = read_int_array()

    state_bytes = STATE_FEATURE_COUNT * 4
    if offset + state_bytes > len(payload):
        raise EOFError("truncated state feature vector")
    state = np.frombuffer(payload[offset : offset + state_bytes], dtype="<f4").copy()
    offset += state_bytes

    (action_count,) = unpack("<i")
    if action_count <= 0 or action_count > 100_000:
        raise ValueError(f"invalid legal action count: {action_count}")
    actions = np.empty((action_count, ACTION_FEATURE_COUNT), dtype=np.float32)
    visits = np.empty(action_count, dtype=np.int64)
    action_bytes = ACTION_FEATURE_COUNT * 4
    for action_index in range(action_count):
        if offset + action_bytes > len(payload):
            raise EOFError("truncated action feature matrix")
        actions[action_index] = np.frombuffer(
            payload[offset : offset + action_bytes], dtype="<f4"
        )
        offset += action_bytes
        (visit_count,) = unpack("<i")
        if visit_count < 0:
            raise ValueError("negative action visit count")
        visits[action_index] = visit_count

    if offset != len(payload):
        raise ValueError(f"unexpected trailing bytes in record: {len(payload) - offset}")
    if not np.isfinite(state).all() or not np.isfinite(actions).all() or not np.isfinite(outcome):
        raise ValueError("dataset contains non-finite values")
    if not -1.0 <= outcome <= 1.0:
        raise ValueError(f"outcome outside [-1, 1]: {outcome}")
    if visits.sum() <= 0:
        raise ValueError("record has no positive policy visit mass")

    return TrainingRecord(
        seed=seed,
        game_id=game_id,
        ply=ply,
        observer_player_index=observer,
        outcome=outcome,
        first_deck=first_deck,
        second_deck=second_deck,
        state=state,
        actions=actions,
        visits=visits,
    )


def _read_exact(stream: BinaryIO, count: int) -> bytes:
    data = stream.read(count)
    if len(data) != count:
        raise EOFError(f"expected {count} bytes, received {len(data)}")
    return data


def _unpack(stream: BinaryIO, fmt: str) -> tuple:
    return struct.unpack(fmt, _read_exact(stream, struct.calcsize(fmt)))
