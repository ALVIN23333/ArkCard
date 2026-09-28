import runpy
import sys
import torch


def main() -> None:
    original_export = torch.onnx.export
    torch.onnx.export = lambda *args, **kwargs: original_export(*args, dynamo=False, **kwargs)
    sys.argv = [
        "mlagents-learn",
        r"Assets\AI\Configs\mlagents_card_battle.yaml",
        "--run-id",
        "ArkCardPPOProbeAscii",
        "--resume",
        "--env",
        r"X:\Builds\MLAgentsTraining\ArkCardTraining.exe",
        "--no-graphics",
        "--results-dir",
        r"Artifacts\MLAgents\ppo-continue-probe-ascii",
    ]
    runpy.run_module("mlagents.trainers.learn", run_name="__main__")


if __name__ == "__main__":
    main()
