from __future__ import annotations

import argparse
import json

from .dataset import find_shards, iter_shard


def main() -> None:
    parser = argparse.ArgumentParser(description="Validate and summarize ArkCard dataset shards")
    parser.add_argument("paths", nargs="+")
    args = parser.parse_args()

    shards = find_shards(args.paths)
    samples = 0
    actions = 0
    wins = 0
    losses = 0
    draws = 0
    for shard in shards:
        for record in iter_shard(shard):
            samples += 1
            actions += len(record.actions)
            wins += int(record.outcome > 0)
            losses += int(record.outcome < 0)
            draws += int(record.outcome == 0)
    print(
        json.dumps(
            {
                "shards": len(shards),
                "samples": samples,
                "candidateActions": actions,
                "meanActionsPerSample": actions / samples if samples else 0,
                "positiveOutcomes": wins,
                "negativeOutcomes": losses,
                "drawOutcomes": draws,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()

