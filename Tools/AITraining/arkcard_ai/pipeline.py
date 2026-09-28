from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

try:
    import tomllib
except ModuleNotFoundError:  # Python 3.10 compatibility
    import tomli as tomllib

configured_project_root = os.environ.get("ARKCARD_PROJECT_ROOT")
PROJECT_ROOT = (
    Path(configured_project_root).absolute()
    if configured_project_root
    else Path(__file__).resolve().parents[3]
)
TOOLS_DIR = Path(__file__).resolve().parents[1]
DEFAULT_CONFIG = TOOLS_DIR / "configs" / "round.toml"
# Unity editors are installed per machine; probe the documented location first and keep
# the legacy path as a fallback so stale configs still resolve on the current machine.
DEFAULT_UNITY_CANDIDATES = (
    "D:/unityEditor/2022.3.62f3c1/Editor/Unity.exe",
    "D:/unity/2022.3.62f3c1/Editor/Unity.exe",
)


# --------------------------------------------------------------------------- config

def load_config(path: Path, overrides: list[str] | None = None) -> dict:
    with open(path, "rb") as stream:
        config = tomllib.load(stream)
    for override in overrides or []:
        section_name, separator, rest = override.partition(".")
        if not separator or "=" not in rest:
            raise ValueError(f"Invalid --set override: {override!r} (expected section.key=value)")
        key, _, raw = rest.partition("=")
        section = config.setdefault(section_name, {})
        if not isinstance(section, dict):
            raise ValueError(f"Override target {section_name!r} is not a table")
        section[key] = _coerce(section.get(key), raw)
    return config


def _coerce(existing, raw: str):
    if isinstance(existing, bool):
        return raw.strip().lower() in ("1", "true", "yes", "on")
    if isinstance(existing, int):
        return int(raw)
    if isinstance(existing, float):
        return float(raw)
    if isinstance(existing, list):
        return [item.strip() for item in raw.split(",") if item.strip()]
    lowered = raw.strip().lower()
    if lowered in ("true", "yes", "on", "1"):
        return True
    if lowered in ("false", "no", "off", "0"):
        return False
    try:
        return int(raw)
    except ValueError:
        try:
            return float(raw)
        except ValueError:
            return raw


# --------------------------------------------------------------------------- paths

def project_path(value: str) -> Path:
    if not value:
        return PROJECT_ROOT
    path = Path(value)
    return path if path.is_absolute() else (PROJECT_ROOT / path)


def unity_rel(value: str) -> str:
    path = Path(value)
    if path.is_absolute():
        try:
            return path.absolute().relative_to(PROJECT_ROOT.absolute()).as_posix()
        except ValueError as exception:
            raise ValueError(f"Path is outside the project: {value}") from exception
    return path.as_posix()


def model_version(config: dict) -> str:
    version = (config.get("round") or {}).get("modelVersion")
    if not version:
        raise ValueError("config [round] modelVersion is required")
    return str(version)


def run_dir(config: dict) -> Path:
    round_cfg = config.get("round") or {}
    configured = round_cfg.get("runDir") or f"Tools/runs/{model_version(config)}"
    return project_path(configured)


# --------------------------------------------------------------------------- runner

def run_command(args: list, cwd: Path, log_path: Path, env_extra: dict | None = None) -> None:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    env.update(env_extra or {})
    with open(log_path, "wb") as log:
        result = subprocess.run(args, cwd=str(cwd), env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode != 0:
        raise RuntimeError(
            f"Command failed (exit {result.returncode}): {' '.join(map(str, args))}\nlog: {log_path}"
        )


def resolve_unity_editor(config: dict) -> str:
    candidates: list[str] = []
    configured = (config.get("project") or {}).get("unity")
    if configured:
        candidates.append(str(configured))
    env_override = os.environ.get("ARKCARD_UNITY")
    if env_override:
        candidates.append(env_override)
    candidates.extend(DEFAULT_UNITY_CANDIDATES)

    for candidate in candidates:
        if Path(candidate).is_file():
            return candidate
    raise FileNotFoundError(
        "Unity editor executable not found. Set [project].unity in the round config or the "
        f"ARKCARD_UNITY environment variable. Tried: {', '.join(candidates)}"
    )


def run_unity(config: dict, execute_method: str, extra_args: list, log_path: Path) -> None:
    unity = resolve_unity_editor(config)
    args = [
        unity,
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(PROJECT_ROOT),
        "-quit",
        "-executeMethod",
        execute_method,
        *extra_args,
        "-logFile",
        str(log_path),
    ]
    run_command(args, PROJECT_ROOT, log_path)


def archive_environment(config: dict, run_dir_path: Path) -> None:
    run_dir_path.mkdir(parents=True, exist_ok=True)
    head = subprocess.run(
        ["git", "-C", str(PROJECT_ROOT), "rev-parse", "HEAD"],
        capture_output=True,
        text=True,
    )
    (run_dir_path / "git-commit.txt").write_text(
        head.stdout.strip() or "UNAVAILABLE", encoding="ascii"
    )
    with open(run_dir_path / "pip-freeze.txt", "w", encoding="utf-8") as stream:
        subprocess.run([sys.executable, "-m", "pip", "freeze"], stdout=stream, check=False)
    try:
        import torch
    except ImportError:
        torch = None
    info = {
        "python": sys.version.split()[0],
        "torch": torch.__version__ if torch is not None else None,
        "torchCuda": torch.version.cuda if torch is not None else None,
        "cudaAvailable": bool(torch is not None and torch.cuda.is_available()),
        "gpu": torch.cuda.get_device_name(0) if torch is not None and torch.cuda.is_available() else "NONE",
        "seed": (config.get("round") or {}).get("seed"),
        "modelVersion": model_version(config),
    }
    (run_dir_path / "environment.json").write_text(
        json.dumps(info, indent=2), encoding="utf-8"
    )


# --------------------------------------------------------------------------- argument builders

def _unity_data_args(
    config: dict,
    section: dict,
    *,
    output_key: str,
    prefix_key: str,
    model_config: str | None,
) -> list[str]:
    seed = (config.get("round") or {}).get("seed", 20260806)
    args = [
        "-aiSamples", str(section.get("samples", 100000)),
        "-aiShardSamples", str(section.get("shardSamples", 2048)),
        "-aiSeed", str(seed),
        "-aiMaxMatches", str(section.get("maxMatches", 10000)),
        "-aiMaxPlies", str(section.get("maxPlies", 256)),
        "-aiOutput", unity_rel(str(section[output_key])),
        "-aiPrefix", str(section.get(prefix_key, "neural-self-play")),
    ]
    if section.get("firstDeck") is not None:
        args += ["-aiFirstDeck", str(section["firstDeck"])]
    if section.get("secondDeck") is not None:
        args += ["-aiSecondDeck", str(section["secondDeck"])]
    if section.get("deckMatrix"):
        args += ["-aiDeckMatrix", str(section["deckMatrix"])]
    if model_config:
        args += ["-aiModelConfig", unity_rel(model_config)]
    return args


def _train_args(config: dict, train_cfg: dict, run_dir_path: Path) -> list[str]:
    datasets = train_cfg.get("datasets") or []
    if not datasets:
        raise ValueError("train.datasets must be configured")
    seed = (config.get("round") or {}).get("seed", 20260806)
    args = [
        sys.executable,
        "-m",
        "arkcard_ai.train",
        *(str(project_path(str(dataset))) for dataset in datasets),
        "--output", str(project_path(str(train_cfg.get("output") or run_dir_path))),
        "--epochs", str(train_cfg.get("epochs", 40)),
        "--batch-size", str(train_cfg.get("batchSize", 64)),
        "--shuffle-buffer", str(train_cfg.get("shuffleBuffer", 4096)),
        "--learning-rate", repr(float(train_cfg.get("learningRate", 3e-4))),
        "--weight-decay", repr(float(train_cfg.get("weightDecay", 1e-4))),
        "--value-weight", repr(float(train_cfg.get("valueWeight", 1.0))),
        "--seed", str(seed),
        "--device", str(train_cfg.get("device", "cuda")),
        "--validation-fraction", repr(float(train_cfg.get("validationFraction", 0.1))),
        "--prefetch-buffer", str(train_cfg.get("prefetchBuffer", 2048)),
    ]
    if not train_cfg.get("balanceGroups", True):
        args.append("--no-balance-groups")
    if train_cfg.get("amp", False):
        args.append("--amp")
    if train_cfg.get("resume"):
        args += ["--resume", str(project_path(str(train_cfg["resume"])))]
    return args


def _export_args(config: dict, export_cfg: dict, run_dir_path: Path) -> list[str]:
    version = model_version(config)
    checkpoint = project_path(
        str(export_cfg.get("checkpoint") or (run_dir_path / "best.pt"))
    )
    output = project_path(
        str(export_cfg.get("output") or (PROJECT_ROOT / "Tools" / "exports" / version))
    )
    args = [
        sys.executable,
        "-m",
        "arkcard_ai.export_onnx",
        str(checkpoint),
        "--output", str(output),
        "--model-version", version,
    ]
    if export_cfg.get("parityRecords"):
        args += [
            "--parity-records",
            *(str(project_path(str(dataset))) for dataset in export_cfg["parityRecords"]),
        ]
    return args


# --------------------------------------------------------------------------- helpers

def inspect_dataset(dataset: Path) -> dict:
    result = subprocess.run(
        [sys.executable, "-m", "arkcard_ai.inspect_dataset", str(dataset)],
        cwd=str(TOOLS_DIR),
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(f"arkcard-inspect-dataset failed: {result.stderr or result.stdout}")
    return json.loads(result.stdout)


def validate_export(export_dir: Path, expected_version: str) -> dict:
    import onnx

    manifest = json.loads((export_dir / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("modelVersion") != expected_version:
        raise ValueError(
            f"manifest modelVersion {manifest.get('modelVersion')} != {expected_version}"
        )
    expected_contract = {
        "policy.onnx": ("policy_input", "policy_logit", 2919),
        "value.onnx": ("state_input", "value", 2180),
    }
    for name in ("policy.onnx", "value.onnx"):
        model = onnx.load(export_dir / name)
        onnx.checker.check_model(model)
        if model.opset_import[0].version != 9:
            raise ValueError(f"{name} opset != 9")
        input_name, output_name, feature_count = expected_contract[name]
        inputs = {item.name: item for item in model.graph.input}
        outputs = {item.name for item in model.graph.output}
        if input_name not in inputs or output_name not in outputs:
            raise ValueError(
                f"{name} contract mismatch: expected {input_name} -> {output_name}"
            )
        dimensions = inputs[input_name].type.tensor_type.shape.dim
        declared_feature_count = dimensions[-1].dim_value if dimensions else 0
        if declared_feature_count not in (0, feature_count):
            raise ValueError(
                f"{name} feature dimension {declared_feature_count} != {feature_count}"
            )
    parity = manifest.get("parity") or {}
    if max(parity.get("policyMaxAbsError", 0.0), parity.get("valueMaxAbsError", 0.0)) > 1e-4:
        raise ValueError(f"ONNX parity exceeded 1e-4: {parity}")
    combined = hashlib.sha256(
        (export_dir / "policy.onnx").read_bytes() + (export_dir / "value.onnx").read_bytes()
    ).hexdigest()
    if combined != manifest.get("combinedSha256"):
        raise ValueError("combined SHA256 does not match manifest")
    lines = []
    for name in ("policy.onnx", "value.onnx", "manifest.json"):
        digest = hashlib.sha256((export_dir / name).read_bytes()).hexdigest()
        lines.append(f"{digest}  {name}")
    (export_dir / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="ascii")
    return manifest


def check_arena(report: Path, minimum_games: int, minimum_score_rate: float, maximum_p95_ms: float) -> dict:
    result = subprocess.run(
        [
            sys.executable,
            "-m",
            "arkcard_ai.arena",
            str(report),
            "--minimum-games", str(minimum_games),
            "--minimum-score-rate", repr(minimum_score_rate),
            "--maximum-p95-ms", repr(maximum_p95_ms),
        ],
        cwd=str(TOOLS_DIR),
        capture_output=True,
        text=True,
    )
    parsed = json.loads(result.stdout) if result.stdout.strip() else {}
    return {
        "passed": result.returncode == 0,
        "checks": parsed.get("checks"),
        "report": parsed.get("report"),
        "exitCode": result.returncode,
    }


# --------------------------------------------------------------------------- commands

def cmd_generate_teacher(config: dict, args: argparse.Namespace) -> None:
    teacher = config.get("teacher") or {}
    if not teacher.get("output"):
        raise ValueError("teacher.output is required")
    run_dir_path = run_dir(config)
    run_dir_path.mkdir(parents=True, exist_ok=True)
    run_unity(
        config,
        "TrainingMatchRunner.RunFromCommandLine",
        _unity_data_args(
            config, teacher, output_key="output", prefix_key="prefix", model_config=None
        ),
        run_dir_path / "teacher-gen.log",
    )
    inspection = inspect_dataset(project_path(str(teacher["output"])))
    (run_dir_path / "teacher-inspection.json").write_text(
        json.dumps(inspection, indent=2), encoding="utf-8"
    )
    shutil.copy2(args.config, run_dir_path / "config.toml")
    archive_environment(config, run_dir_path)
    print(json.dumps(inspection, indent=2))


def generate_data_if_needed(config: dict, run_dir_path: Path) -> None:
    data = config.get("data") or {}
    mode = str(data.get("mode", "skip")).lower()
    if mode == "teacher":
        teacher = config.get("teacher") or {}
        if not teacher.get("output"):
            raise ValueError("teacher.output is required for data.mode=teacher")
        run_unity(
            config,
            "TrainingMatchRunner.RunFromCommandLine",
            _unity_data_args(
                config, teacher, output_key="output", prefix_key="prefix", model_config=None
            ),
            run_dir_path / "teacher-gen.log",
        )
    elif mode == "selfplay":
        if not data.get("selfplayOutput"):
            raise ValueError("data.selfplayOutput is required for data.mode=selfplay")
        run_unity(
            config,
            "TrainingMatchRunner.RunSelfPlayFromCommandLine",
            _unity_data_args(
                config,
                data,
                output_key="selfplayOutput",
                prefix_key="selfplayPrefix",
                model_config=str(data.get("modelConfig", "Assets/AI/Configs/DefaultAIModelConfig.asset")),
            ),
            run_dir_path / "selfplay-gen.log",
        )
    elif mode != "skip":
        raise ValueError(f"data.mode must be teacher|selfplay|skip, got {mode!r}")


def cmd_train(config: dict, args: argparse.Namespace) -> None:
    run_dir_path = run_dir(config)
    run_dir_path.mkdir(parents=True, exist_ok=True)
    generate_data_if_needed(config, run_dir_path)
    train_cfg = config.get("train") or {}
    env = {
        "PYTHONUNBUFFERED": "1",
        "PIP_CACHE_DIR": str(PROJECT_ROOT / "Tools" / ".pip-cache"),
        "TMP": str(PROJECT_ROOT / "Tools" / ".tmp"),
        "TEMP": str(PROJECT_ROOT / "Tools" / ".tmp"),
    }
    run_command(_train_args(config, train_cfg, run_dir_path), TOOLS_DIR, run_dir_path / "train.log", env)
    shutil.copy2(args.config, run_dir_path / "config.toml")
    archive_environment(config, run_dir_path)
    print(f"training complete; run dir: {run_dir_path}")


def cmd_candidate(config: dict, args: argparse.Namespace) -> None:
    version = model_version(config)
    candidate_cfg = config.get("candidate") or {}
    run_dir_path = run_dir(config)
    run_dir_path.mkdir(parents=True, exist_ok=True)
    if not candidate_cfg.get("skipTrain", False):
        cmd_train(config, args)

    export_cfg = config.get("export") or {}
    env = {"PYTHONUNBUFFERED": "1", "TMP": str(PROJECT_ROOT / "Tools" / ".tmp"), "TEMP": str(PROJECT_ROOT / "Tools" / ".tmp")}
    run_command(_export_args(config, export_cfg, run_dir_path), TOOLS_DIR, run_dir_path / "export.log", env)

    export_dir = project_path(
        str(export_cfg.get("output") or (PROJECT_ROOT / "Tools" / "exports" / version))
    )
    manifest = validate_export(export_dir, version)

    models_dir = str(candidate_cfg.get("modelsDir", "Assets/AI/Models/Candidate"))
    target = PROJECT_ROOT / models_dir
    target.mkdir(parents=True, exist_ok=True)
    for name in ("policy.onnx", "value.onnx", "manifest.json"):
        shutil.copy2(export_dir / name, target / name)

    config_path = str(candidate_cfg.get("configPath", "Assets/AI/Configs/CandidateAIModelConfig.asset"))
    run_unity(
        config,
        "ModelPromotionUtility.RefreshCandidateConfigFromCommandLine",
        ["-aiCandidateModelsDir", unity_rel(models_dir), "-aiCandidateConfigPath", unity_rel(config_path)],
        run_dir_path / "candidate-config.log",
    )
    report = {
        "modelVersion": version,
        "manifest": manifest,
        "stagedTo": unity_rel(models_dir),
        "configPath": unity_rel(config_path),
    }
    (run_dir_path / "candidate-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


def cmd_arena(config: dict, args: argparse.Namespace) -> None:
    version = model_version(config)
    arena_cfg = config.get("arena") or {}
    run_dir_path = run_dir(config)
    run_dir_path.mkdir(parents=True, exist_ok=True)
    candidate = unity_rel(str(arena_cfg.get("candidateConfig", "Assets/AI/Configs/CandidateAIModelConfig.asset")))
    champion_config = str(arena_cfg.get("championConfig", "")).strip()
    champion = unity_rel(champion_config) if champion_config else ""
    seed = (config.get("round") or {}).get("seed", 20260806)
    smoke_games = int(arena_cfg.get("smokeGames", 20))
    formal_games = int(arena_cfg.get("games", 1000))
    minimum_games = int(arena_cfg.get("minimumGames", 1000))
    minimum_score_rate = float(arena_cfg.get("minimumScoreRate", 0.55))
    maximum_p95_ms = float(arena_cfg.get("maximumP95Ms", 50.0))
    report_dir = project_path(str(arena_cfg.get("reportDir", "Artifacts/AI/Reports")))
    report_dir.mkdir(parents=True, exist_ok=True)

    def run_arena(
        games: int,
        report_name: str,
        log_name: str,
        required_games: int,
    ) -> dict:
        report = report_dir / report_name
        run_unity(
            config,
            "ArenaRunner.RunFromCommandLine",
            [
                "-aiArenaGames", str(games),
                "-aiSeed", str(seed),
                "-aiCandidateModelConfig", candidate,
                "-aiArenaReport", unity_rel(str(report)),
            ] + (["-aiChampionModelConfig", champion] if champion else []),
            run_dir_path / log_name,
        )
        return check_arena(report, required_games, minimum_score_rate, maximum_p95_ms)

    smoke = run_arena(
        smoke_games,
        f"{version}-smoke.json",
        "arena-smoke.log",
        smoke_games,
    )
    result = {"modelVersion": version, "smoke": smoke, "formal": None, "promotable": False}
    if smoke["passed"]:
        formal = run_arena(
            formal_games,
            f"{version}-arena.json",
            "arena-formal.log",
            minimum_games,
        )
        result["formal"] = formal
        result["promotable"] = bool(formal["passed"])
    else:
        result["formal"] = {"passed": False, "reason": "smoke gate not passed"}
    shutil.copy2(args.config, run_dir_path / "config.toml")
    (run_dir_path / "arena-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    if not result["promotable"]:
        raise SystemExit(1)


def cmd_promote(config: dict, args: argparse.Namespace) -> None:
    """Promote a passed candidate and keep a restorable copy of the champion."""
    version = model_version(config)
    candidate_cfg = config.get("candidate") or {}
    run_dir_path = run_dir(config)
    arena_result_path = run_dir_path / "arena-result.json"
    if not arena_result_path.exists():
        raise FileNotFoundError(
            f"Arena result is missing: {arena_result_path}. Run the arena command first."
        )
    arena_result = json.loads(arena_result_path.read_text(encoding="utf-8"))
    formal = arena_result.get("formal") or {}
    if not arena_result.get("promotable") or not formal.get("passed"):
        raise RuntimeError("Candidate did not pass the formal arena gate; promotion was refused.")

    candidate_dir = project_path(
        str(candidate_cfg.get("modelsDir", "Assets/AI/Models/Candidate"))
    )
    champion_dir = PROJECT_ROOT / "Assets" / "AI" / "Models"
    required_files = ("policy.onnx", "value.onnx", "manifest.json")
    for name in required_files:
        if not (candidate_dir / name).is_file():
            raise FileNotFoundError(f"Candidate model file is missing: {candidate_dir / name}")

    backup_dir = run_dir_path / "champion-backup"
    backup_dir.mkdir(parents=True, exist_ok=True)
    backed_up = []
    if champion_dir.exists():
        for name in required_files:
            source = champion_dir / name
            if source.is_file():
                shutil.copy2(source, backup_dir / name)
                backed_up.append(name)

    champion_dir.mkdir(parents=True, exist_ok=True)
    for name in required_files:
        shutil.copy2(candidate_dir / name, champion_dir / name)

    run_unity(
        config,
        "ModelPromotionUtility.RefreshDefaultConfig",
        [],
        run_dir_path / "promote-config.log",
    )
    promotion = {
        "modelVersion": version,
        "source": unity_rel(str(candidate_dir)),
        "target": unity_rel(str(champion_dir)),
        "backedUpFiles": backed_up,
        "backupDirectory": unity_rel(str(backup_dir)),
        "formalArena": formal,
    }
    (run_dir_path / "promotion-report.json").write_text(
        json.dumps(promotion, indent=2), encoding="utf-8"
    )
    print(json.dumps(promotion, indent=2))


def main() -> None:
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--config", default=str(DEFAULT_CONFIG), help="TOML config path")
    common.add_argument("--set", action="append", default=None, metavar="section.key=value")
    parser = argparse.ArgumentParser(
        description="ArkCard AI pipeline orchestrator", parents=[common]
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("generate-teacher", parents=[common], help="Generate teacher dataset via Unity batchmode")
    subparsers.add_parser("train", parents=[common], help="Generate optional data then train the model")
    subparsers.add_parser("candidate", parents=[common], help="Train/export/stage a candidate model")
    subparsers.add_parser("arena", parents=[common], help="Run arena smoke + formal and check promotion gates")
    subparsers.add_parser("promote", parents=[common], help="Promote a passed candidate and refresh the default Unity config")
    args = parser.parse_args()
    config = load_config(Path(args.config), args.set)
    commands = {
        "generate-teacher": cmd_generate_teacher,
        "train": cmd_train,
        "candidate": cmd_candidate,
        "arena": cmd_arena,
        "promote": cmd_promote,
    }
    commands[args.command](config, args)


if __name__ == "__main__":
    main()
