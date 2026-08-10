from __future__ import annotations

from argparse import Namespace

import pytest

from arkcard_ai.pipeline import (
    DEFAULT_CONFIG,
    PROJECT_ROOT,
    _coerce,
    _export_args,
    _train_args,
    _unity_data_args,
    cmd_arena,
    load_config,
    model_version,
    run_dir,
)


def test_default_config_template_parses():
    config = load_config(DEFAULT_CONFIG, None)
    assert model_version(config) == "self-play-r001-candidate-002"
    assert config["teacher"]["deckMatrix"] == "all"
    assert config["train"]["datasets"]
    assert config["arena"]["maximumP95Ms"] == 50.0


def test_load_config_applies_typed_overrides(tmp_path):
    config_path = tmp_path / "round.toml"
    config_path.write_text(
        "\n".join(
            [
                "[train]",
                "epochs = 40",
                "amp = true",
                "learningRate = 3e-4",
                "datasets = [\"a\", \"b\"]",
                "device = \"cuda\"",
                "",
            ]
        ),
        encoding="utf-8",
    )
    config = load_config(
        config_path,
        [
            "train.epochs=30",
            "train.amp=false",
            "train.learningRate=1e-3",
            "train.datasets=x,y",
            "train.device=cpu",
        ],
    )
    assert config["train"]["epochs"] == 30
    assert config["train"]["amp"] is False
    assert config["train"]["learningRate"] == 1e-3
    assert config["train"]["datasets"] == ["x", "y"]
    assert config["train"]["device"] == "cpu"


def test_load_config_rejects_malformed_override(tmp_path):
    config_path = tmp_path / "round.toml"
    config_path.write_text("[train]\nepochs = 40\n", encoding="utf-8")
    with pytest.raises(ValueError):
        load_config(config_path, ["not-a-set-override"])


def test_coerce_without_existing_type():
    assert _coerce(None, "true") is True
    assert _coerce(None, "42") == 42
    assert _coerce(None, "1.5") == 1.5
    assert _coerce(None, "alpha") == "alpha"


def test_teacher_args_maps_deck_matrix_and_paths():
    config = {
        "round": {"seed": 20260806},
        "teacher": {
            "samples": 2048,
            "shardSamples": 2048,
            "output": "Artifacts/AI/Datasets/smoke",
            "prefix": "legacy-smoke",
            "deckMatrix": "all",
        },
    }
    args = _unity_data_args(config, config["teacher"], output_key="output", prefix_key="prefix", model_config=None)
    assert "-aiDeckMatrix" in args
    assert args[args.index("-aiDeckMatrix") + 1] == "all"
    assert args[args.index("-aiOutput") + 1] == "Artifacts/AI/Datasets/smoke"
    assert args[args.index("-aiSamples") + 1] == "2048"
    assert "-aiModelConfig" not in args


def test_selfplay_args_include_model_config():
    config = {
        "round": {"seed": 20260806},
        "data": {
            "selfplayOutput": "Artifacts/AI/Datasets/self-play-r002",
            "selfplayPrefix": "neural-self-play-r002",
            "modelConfig": "Assets/AI/Configs/DefaultAIModelConfig.asset",
            "deckMatrix": "0,1",
        },
    }
    args = _unity_data_args(
        config,
        config["data"],
        output_key="selfplayOutput",
        prefix_key="selfplayPrefix",
        model_config=config["data"]["modelConfig"],
    )
    assert args[args.index("-aiModelConfig") + 1] == "Assets/AI/Configs/DefaultAIModelConfig.asset"
    assert args[args.index("-aiDeckMatrix") + 1] == "0,1"


def test_train_args_build_expected_flags(tmp_path):
    config = {"round": {"seed": 20260806}}
    train_cfg = {
        "datasets": ["Artifacts/AI/Datasets/teacher-v1"],
        "epochs": 40,
        "batchSize": 64,
        "learningRate": 3e-4,
        "weightDecay": 1e-4,
        "valueWeight": 1.0,
        "device": "cuda",
        "amp": True,
        "validationFraction": 0.1,
        "prefetchBuffer": 2048,
        "resume": "Tools/runs/teacher-v1-001/teacher-v1-001.pt",
    }
    run_dir_path = tmp_path / "run"
    args = _train_args(config, train_cfg, run_dir_path)
    assert "--amp" in args
    assert "--validation-fraction" in args
    assert args[args.index("--epochs") + 1] == "40"
    assert args[args.index("--resume") + 1].endswith("teacher-v1-001.pt")
    assert "--output" in args


def test_export_args_default_to_run_dir_and_version(tmp_path):
    config = {"round": {"modelVersion": "smoke-candidate-001"}}
    run_dir_path = tmp_path / "run"
    args = _export_args(config, {}, run_dir_path)
    assert str(run_dir_path / "best.pt") in args
    assert "--model-version" in args
    assert args[args.index("--model-version") + 1] == "smoke-candidate-001"
    assert "--parity-records" not in args


def test_run_dir_defaults_from_model_version(tmp_path):
    config = {"round": {"modelVersion": "v2-candidate"}}
    assert run_dir(config) == PROJECT_ROOT / "Tools/runs/v2-candidate"


def test_arena_smoke_uses_smoke_game_count_as_its_gate(tmp_path, monkeypatch):
    config_path = tmp_path / "round.toml"
    config_path.write_text("[round]\nmodelVersion = \"candidate\"\n", encoding="utf-8")
    config = {
        "round": {
            "modelVersion": "candidate",
            "seed": 7,
            "runDir": str(tmp_path / "run"),
        },
        "arena": {
            "smokeGames": 20,
            "games": 1000,
            "minimumGames": 1000,
            "reportDir": str(tmp_path / "reports"),
        },
    }
    required_game_counts = []

    monkeypatch.setattr("arkcard_ai.pipeline.run_unity", lambda *args, **kwargs: None)
    monkeypatch.setattr("arkcard_ai.pipeline.unity_rel", lambda value: str(value))

    def fake_check_arena(report, minimum_games, minimum_score_rate, maximum_p95_ms):
        required_game_counts.append(minimum_games)
        return {"passed": True, "checks": {}, "report": {}, "exitCode": 0}

    monkeypatch.setattr("arkcard_ai.pipeline.check_arena", fake_check_arena)

    cmd_arena(config, Namespace(config=str(config_path)))

    assert required_game_counts == [20, 1000]
