from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description="Check an ArkCard Unity arena report for promotion")
    parser.add_argument("report")
    parser.add_argument("--minimum-games", type=int, default=1000)
    parser.add_argument("--minimum-score-rate", type=float, default=0.55)
    parser.add_argument("--maximum-p95-ms", type=float, default=50.0)
    args = parser.parse_args()

    report = json.loads(Path(args.report).read_text(encoding="utf-8"))
    checks = {
        "minimumGames": int(report["games"]) >= args.minimum_games,
        "scoreRate": float(report["candidateScoreRate"]) >= args.minimum_score_rate,
        "latencyP95": float(report["decisionP95Milliseconds"]) <= args.maximum_p95_ms,
    }
    passed = all(checks.values())
    print(json.dumps({"passed": passed, "checks": checks, "report": report}, indent=2))
    if not passed:
        raise SystemExit(1)


if __name__ == "__main__":
    main()

