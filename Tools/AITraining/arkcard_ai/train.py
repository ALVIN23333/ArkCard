from __future__ import annotations

import argparse
import hashlib
import json
import queue
import random
import threading
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable, Iterator, Optional, Sequence

import numpy as np
import torch
from torch import Tensor
from torch.nn import functional as F

from .dataset import (
    ACTION_FEATURE_COUNT,
    SCHEMA_VERSION,
    STATE_FEATURE_COUNT,
    TrainingRecord,
    find_shards,
    iter_records,
    iter_shard,
)
from .model import PolicyValueModel


@dataclass
class EpochMetrics:
    epoch: int
    samples: int
    policy_loss: float
    value_loss: float
    total_loss: float
    validation_samples: Optional[int] = None
    validation_policy_loss: Optional[float] = None
    validation_value_loss: Optional[float] = None
    validation_total_loss: Optional[float] = None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train ArkCard policy/value networks")
    parser.add_argument("dataset", nargs="+", help="Dataset shards, globs, or directories")
    parser.add_argument("--output", default="Artifacts/AI/Checkpoints")
    parser.add_argument("--epochs", type=int, default=20)
    parser.add_argument("--batch-size", type=int, default=64, help="Decision records per optimizer step")
    parser.add_argument("--shuffle-buffer", type=int, default=4096)
    parser.add_argument("--learning-rate", type=float, default=3e-4)
    parser.add_argument("--weight-decay", type=float, default=1e-4)
    parser.add_argument("--value-weight", type=float, default=1.0)
    parser.add_argument("--seed", type=int, default=20260806)
    parser.add_argument("--device", default="auto", choices=("auto", "cuda", "cpu"))
    parser.add_argument("--resume", default=None)
    parser.add_argument(
        "--validation-fraction",
        type=float,
        default=0.1,
        help="Fraction of games (by game_id) held out for validation; 0 disables validation.",
    )
    parser.add_argument("--amp", action="store_true", help="Use mixed precision on CUDA.")
    parser.add_argument(
        "--prefetch-buffer",
        type=int,
        default=2048,
        help="Records to buffer in the background decode/prefetch thread; <=1 disables prefetch.",
    )
    return parser.parse_args()


def batched(records: Iterable[TrainingRecord], size: int) -> Iterator[list[TrainingRecord]]:
    batch: list[TrainingRecord] = []
    for record in records:
        batch.append(record)
        if len(batch) >= size:
            yield batch
            batch = []
    if batch:
        yield batch


def train_batch(
    model: PolicyValueModel,
    records: Sequence[TrainingRecord],
    device: torch.device,
    value_weight: float,
) -> tuple[Tensor, Tensor, Tensor]:
    states = torch.from_numpy(np.stack([record.state for record in records])).to(device)
    outcomes = torch.tensor([record.outcome for record in records], dtype=torch.float32, device=device)
    action_arrays = [record.actions for record in records]
    actions = torch.from_numpy(np.concatenate(action_arrays, axis=0)).to(device)
    owners = torch.repeat_interleave(
        torch.arange(len(records), device=device),
        torch.tensor([len(actions_for_record) for actions_for_record in action_arrays], device=device),
    )

    state_embeddings = model.encode_states(states)
    logits = model.policy_logits_grouped(state_embeddings, actions, owners)
    values = model.values_from_embeddings(state_embeddings)

    policy_losses: list[Tensor] = []
    offset = 0
    for record in records:
        count = len(record.actions)
        target = torch.from_numpy(record.policy_target).to(device)
        policy_losses.append(-(target * F.log_softmax(logits[offset : offset + count], dim=0)).sum())
        offset += count
    policy_loss = torch.stack(policy_losses).mean()
    value_loss = F.huber_loss(values, outcomes, delta=1.0)
    return policy_loss + value_weight * value_loss, policy_loss, value_loss


def _game_id_hash(game_id: int, seed: int) -> int:
    digest = hashlib.sha256(f"{seed}:{game_id}".encode("utf-8")).digest()
    return int.from_bytes(digest[:8], "little")


def partition_game_ids(
    shards: Sequence[str | Path],
    validation_fraction: float,
    seed: int,
) -> set[int]:
    """Deterministically select validation game ids so no game leaks across splits."""
    if validation_fraction <= 0.0:
        return set()
    if validation_fraction >= 1.0:
        raise ValueError("validation-fraction must be in [0, 1)")
    threshold = int(validation_fraction * (1 << 64))
    validation: set[int] = set()
    for path in find_shards(shards):
        for record in iter_shard(path):
            if _game_id_hash(record.game_id, seed) < threshold:
                validation.add(record.game_id)
    return validation


def iter_training_records(
    shards: Sequence[str | Path],
    validation_game_ids: set[int],
    seed: int,
    shuffle_buffer: int,
) -> Iterator[TrainingRecord]:
    for record in iter_records(
        shards,
        seed=seed,
        shuffle_shards=True,
        shuffle_buffer=shuffle_buffer,
    ):
        if record.game_id in validation_game_ids:
            continue
        yield record


def iter_validation_records(
    shards: Sequence[str | Path],
    validation_game_ids: set[int],
) -> Iterator[TrainingRecord]:
    for path in find_shards(shards):
        for record in iter_shard(path):
            if record.game_id in validation_game_ids:
                yield record


def prefetch_records(
    records: Iterable[TrainingRecord],
    buffer_size: int,
) -> Iterator[TrainingRecord]:
    """Decode/shuffle records on a background thread into a bounded queue."""
    if buffer_size <= 1:
        yield from records
        return

    buffer: queue.Queue = queue.Queue(maxsize=buffer_size)
    producer_errors: list[BaseException] = []

    def produce() -> None:
        try:
            for record in records:
                buffer.put(record)
        except BaseException as exception:  # noqa: BLE001 - re-raised on the consumer side
            producer_errors.append(exception)
        finally:
            buffer.put(None)

    thread = threading.Thread(target=produce, daemon=True)
    thread.start()
    while True:
        record = buffer.get()
        if record is None:
            break
        yield record
    if producer_errors:
        raise producer_errors[0]


def evaluate(
    model: PolicyValueModel,
    records: Iterable[TrainingRecord],
    device: torch.device,
    batch_size: int,
    value_weight: float,
    use_amp: bool,
) -> dict[str, float]:
    model.eval()
    sample_count = 0
    policy_total = 0.0
    value_total = 0.0
    total = 0.0
    autocast_context = (
        torch.autocast(device_type="cuda", dtype=torch.float16)
        if use_amp and device.type == "cuda"
        else torch.autocast(device_type="cpu", enabled=False)
    )
    with torch.no_grad(), autocast_context:
        for batch in batched(records, batch_size):
            loss, policy_loss, value_loss = train_batch(model, batch, device, value_weight)
            count = len(batch)
            sample_count += count
            total += float(loss.detach()) * count
            policy_total += float(policy_loss.detach()) * count
            value_total += float(value_loss.detach()) * count
    if sample_count == 0:
        raise RuntimeError("validation set contained no records")
    return {
        "samples": sample_count,
        "policy_loss": policy_total / sample_count,
        "value_loss": value_total / sample_count,
        "total_loss": total / sample_count,
    }


def _history_path(output: Path) -> Path:
    return output / "training-history.json"


def load_history(output: Path) -> list[EpochMetrics]:
    path = _history_path(output)
    if not path.exists():
        return []
    items = json.loads(path.read_text(encoding="utf-8"))
    return [EpochMetrics(**item) for item in items]


def backup_history(output: Path) -> None:
    path = _history_path(output)
    if path.exists():
        backup = output / "training-history-before-resume.json"
        backup.write_text(path.read_text(encoding="utf-8"), encoding="utf-8")


def _checkpoint_dict(
    model: PolicyValueModel,
    optimizer: torch.optim.Optimizer,
    epoch: int,
    metrics: EpochMetrics,
) -> dict:
    return {
        "schema_version": SCHEMA_VERSION,
        "state_feature_count": STATE_FEATURE_COUNT,
        "action_feature_count": ACTION_FEATURE_COUNT,
        "epoch": epoch,
        "model_state": model.state_dict(),
        "optimizer_state": optimizer.state_dict(),
        "metrics": asdict(metrics),
        "model_config": {"state_width": 256, "action_width": 128},
    }


def main() -> None:
    args = parse_args()
    if args.epochs <= 0 or args.batch_size <= 0:
        raise ValueError("epochs and batch size must be positive")
    if not 0.0 <= args.validation_fraction < 1.0:
        raise ValueError("validation-fraction must be in [0, 1)")
    if args.prefetch_buffer <= 0:
        raise ValueError("prefetch-buffer must be positive")

    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(args.seed)

    device_name = "cuda" if args.device == "auto" and torch.cuda.is_available() else args.device
    if device_name == "auto":
        device_name = "cpu"
    device = torch.device(device_name)
    shards = find_shards(args.dataset)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    model = PolicyValueModel().to(device)
    optimizer = torch.optim.AdamW(
        model.parameters(), lr=args.learning_rate, weight_decay=args.weight_decay
    )
    start_epoch = 0
    history: list[EpochMetrics] = []
    best_validation_loss: Optional[float] = None

    if args.resume:
        checkpoint = torch.load(args.resume, map_location=device, weights_only=False)
        _validate_checkpoint_schema(checkpoint)
        model.load_state_dict(checkpoint["model_state"])
        optimizer.load_state_dict(checkpoint["optimizer_state"])
        start_epoch = int(checkpoint["epoch"]) + 1
        backup_history(output)
        history = load_history(output)
        if history and history[-1].epoch != start_epoch - 1:
            raise ValueError(
                f"history last epoch {history[-1].epoch} does not precede resume epoch {start_epoch}"
            )
        best_path = output / "best.pt"
        if best_path.exists():
            best_checkpoint = torch.load(best_path, map_location="cpu", weights_only=False)
            best_metrics = best_checkpoint.get("metrics") or {}
            best_validation_loss = best_metrics.get("validation_total_loss")

    validation_game_ids = partition_game_ids(shards, args.validation_fraction, args.seed)
    use_amp = args.amp and device.type == "cuda"
    scaler = torch.amp.GradScaler("cuda") if use_amp else None

    for epoch in range(start_epoch, args.epochs):
        model.train()
        policy_total = 0.0
        value_total = 0.0
        total = 0.0
        sample_count = 0
        records = prefetch_records(
            iter_training_records(
                shards,
                validation_game_ids,
                seed=args.seed + epoch,
                shuffle_buffer=args.shuffle_buffer,
            ),
            args.prefetch_buffer,
        )
        for batch in batched(records, args.batch_size):
            optimizer.zero_grad(set_to_none=True)
            if scaler is not None:
                with torch.autocast(device_type="cuda", dtype=torch.float16):
                    loss, policy_loss, value_loss = train_batch(model, batch, device, args.value_weight)
                scaler.scale(loss).backward()
                scaler.unscale_(optimizer)
                torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=5.0)
                scaler.step(optimizer)
                scaler.update()
            else:
                loss, policy_loss, value_loss = train_batch(model, batch, device, args.value_weight)
                loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=5.0)
                optimizer.step()
            count = len(batch)
            sample_count += count
            total += float(loss.detach()) * count
            policy_total += float(policy_loss.detach()) * count
            value_total += float(value_loss.detach()) * count

        if sample_count == 0:
            raise RuntimeError("dataset contained no training records")
        metrics = EpochMetrics(
            epoch=epoch,
            samples=sample_count,
            policy_loss=policy_total / sample_count,
            value_loss=value_total / sample_count,
            total_loss=total / sample_count,
        )
        if validation_game_ids:
            validation = evaluate(
                model,
                iter_validation_records(shards, validation_game_ids),
                device,
                args.batch_size,
                args.value_weight,
                use_amp,
            )
            metrics.validation_samples = int(validation["samples"])
            metrics.validation_policy_loss = validation["policy_loss"]
            metrics.validation_value_loss = validation["value_loss"]
            metrics.validation_total_loss = validation["total_loss"]

        history.append(metrics)
        checkpoint = _checkpoint_dict(model, optimizer, epoch, metrics)
        torch.save(checkpoint, output / "latest.pt")
        if (
            metrics.validation_samples
            and metrics.validation_total_loss is not None
            and (
                best_validation_loss is None
                or metrics.validation_total_loss < best_validation_loss
            )
        ):
            best_validation_loss = metrics.validation_total_loss
            torch.save(checkpoint, output / "best.pt")
            print(
                json.dumps(
                    {"savedBest": True, "epoch": epoch, "validationTotalLoss": best_validation_loss},
                    sort_keys=True,
                )
            )
        print(json.dumps(asdict(metrics), sort_keys=True))

    _history_path(output).write_text(
        json.dumps([asdict(item) for item in history], indent=2),
        encoding="utf-8",
    )


def _validate_checkpoint_schema(checkpoint: dict) -> None:
    expected = (SCHEMA_VERSION, STATE_FEATURE_COUNT, ACTION_FEATURE_COUNT)
    actual = (
        checkpoint.get("schema_version"),
        checkpoint.get("state_feature_count"),
        checkpoint.get("action_feature_count"),
    )
    if actual != expected:
        raise ValueError(f"checkpoint schema mismatch: expected {expected}, got {actual}")


if __name__ == "__main__":
    main()

