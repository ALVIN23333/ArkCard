from __future__ import annotations

import argparse
import json
import random
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable, Iterator, Sequence

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
)
from .model import PolicyValueModel


@dataclass
class EpochMetrics:
    epoch: int
    samples: int
    policy_loss: float
    value_loss: float
    total_loss: float


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


def main() -> None:
    args = parse_args()
    if args.epochs <= 0 or args.batch_size <= 0:
        raise ValueError("epochs and batch size must be positive")
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
    if args.resume:
        checkpoint = torch.load(args.resume, map_location=device, weights_only=False)
        _validate_checkpoint_schema(checkpoint)
        model.load_state_dict(checkpoint["model_state"])
        optimizer.load_state_dict(checkpoint["optimizer_state"])
        start_epoch = int(checkpoint["epoch"]) + 1

    history: list[EpochMetrics] = []
    for epoch in range(start_epoch, args.epochs):
        model.train()
        policy_total = 0.0
        value_total = 0.0
        total = 0.0
        sample_count = 0
        records = iter_records(
            shards,
            seed=args.seed + epoch,
            shuffle_shards=True,
            shuffle_buffer=args.shuffle_buffer,
        )
        for batch in batched(records, args.batch_size):
            optimizer.zero_grad(set_to_none=True)
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
        history.append(metrics)
        checkpoint = {
            "schema_version": SCHEMA_VERSION,
            "state_feature_count": STATE_FEATURE_COUNT,
            "action_feature_count": ACTION_FEATURE_COUNT,
            "epoch": epoch,
            "model_state": model.state_dict(),
            "optimizer_state": optimizer.state_dict(),
            "metrics": asdict(metrics),
            "model_config": {"state_width": 256, "action_width": 128},
        }
        torch.save(checkpoint, output / "latest.pt")
        print(json.dumps(asdict(metrics), sort_keys=True))

    (output / "training-history.json").write_text(
        json.dumps([asdict(item) for item in history], indent=2), encoding="utf-8"
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
