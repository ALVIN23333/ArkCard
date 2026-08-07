from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import torch

from .dataset import ACTION_FEATURE_COUNT, SCHEMA_VERSION, STATE_FEATURE_COUNT
from .model import PolicyExport, PolicyValueModel, ValueExport

POLICY_INPUT_NAME = "policy_input"
POLICY_OUTPUT_NAME = "policy_logit"
VALUE_INPUT_NAME = "state_input"
VALUE_OUTPUT_NAME = "value"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export ArkCard Barracuda-compatible ONNX models")
    parser.add_argument("checkpoint")
    parser.add_argument("--output", default="Artifacts/AI/Export")
    parser.add_argument("--model-version", required=True)
    parser.add_argument("--skip-parity", action="store_true")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    checkpoint = torch.load(args.checkpoint, map_location="cpu", weights_only=False)
    _validate_checkpoint(checkpoint)
    model_config = checkpoint.get("model_config", {})
    model = PolicyValueModel(
        state_width=int(model_config.get("state_width", 256)),
        action_width=int(model_config.get("action_width", 128)),
    )
    model.load_state_dict(checkpoint["model_state"])
    model.eval()

    policy = PolicyExport(model).eval()
    value = ValueExport(model).eval()
    policy_path = output / "policy.onnx"
    value_path = output / "value.onnx"
    generator = torch.Generator().manual_seed(20260806)
    policy_input = torch.randn(7, STATE_FEATURE_COUNT + ACTION_FEATURE_COUNT, generator=generator)
    state_input = torch.randn(5, STATE_FEATURE_COUNT, generator=generator)

    torch.onnx.export(
        policy,
        policy_input,
        policy_path,
        input_names=[POLICY_INPUT_NAME],
        output_names=[POLICY_OUTPUT_NAME],
        dynamic_axes={POLICY_INPUT_NAME: {0: "candidate_count"}, POLICY_OUTPUT_NAME: {0: "candidate_count"}},
        opset_version=9,
        do_constant_folding=True,
        dynamo=False,
    )
    torch.onnx.export(
        value,
        state_input,
        value_path,
        input_names=[VALUE_INPUT_NAME],
        output_names=[VALUE_OUTPUT_NAME],
        dynamic_axes={VALUE_INPUT_NAME: {0: "batch"}, VALUE_OUTPUT_NAME: {0: "batch"}},
        opset_version=9,
        do_constant_folding=True,
        dynamo=False,
    )

    parity = None if args.skip_parity else _check_parity(policy, value, policy_path, value_path, policy_input, state_input)
    checksum = hashlib.sha256(policy_path.read_bytes() + value_path.read_bytes()).hexdigest()
    manifest = {
        "modelVersion": args.model_version,
        "featureSchemaVersion": SCHEMA_VERSION,
        "stateFeatureCount": STATE_FEATURE_COUNT,
        "actionFeatureCount": ACTION_FEATURE_COUNT,
        "policyInputFeatureCount": STATE_FEATURE_COUNT + ACTION_FEATURE_COUNT,
        "opset": 9,
        "policy": {"file": policy_path.name, "input": POLICY_INPUT_NAME, "output": POLICY_OUTPUT_NAME},
        "value": {"file": value_path.name, "input": VALUE_INPUT_NAME, "output": VALUE_OUTPUT_NAME},
        "combinedSha256": checksum,
        "parity": parity,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


def _check_parity(
    policy: PolicyExport,
    value: ValueExport,
    policy_path: Path,
    value_path: Path,
    policy_input: torch.Tensor,
    state_input: torch.Tensor,
) -> dict[str, float]:
    try:
        import onnxruntime as ort
    except ImportError as exception:
        raise RuntimeError("onnxruntime is required for parity validation; install the export extra") from exception

    with torch.no_grad():
        torch_policy = policy(policy_input).numpy()
        torch_value = value(state_input).numpy()
    policy_session = ort.InferenceSession(str(policy_path), providers=["CPUExecutionProvider"])
    value_session = ort.InferenceSession(str(value_path), providers=["CPUExecutionProvider"])
    onnx_policy = policy_session.run([POLICY_OUTPUT_NAME], {POLICY_INPUT_NAME: policy_input.numpy()})[0]
    onnx_value = value_session.run([VALUE_OUTPUT_NAME], {VALUE_INPUT_NAME: state_input.numpy()})[0]
    policy_error = float(np.max(np.abs(torch_policy - onnx_policy)))
    value_error = float(np.max(np.abs(torch_value - onnx_value)))
    if max(policy_error, value_error) > 1e-4:
        raise RuntimeError(f"ONNX parity failed: policy={policy_error}, value={value_error}")
    return {"policyMaxAbsError": policy_error, "valueMaxAbsError": value_error}


def _validate_checkpoint(checkpoint: dict) -> None:
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
