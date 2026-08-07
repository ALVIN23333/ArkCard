from __future__ import annotations

import torch
from torch import Tensor, nn

from .dataset import ACTION_FEATURE_COUNT, STATE_FEATURE_COUNT


class PolicyValueModel(nn.Module):
    def __init__(self, state_width: int = 256, action_width: int = 128) -> None:
        super().__init__()
        self.state_encoder = nn.Sequential(
            nn.Linear(STATE_FEATURE_COUNT, 512),
            nn.ReLU(),
            nn.Linear(512, state_width),
            nn.ReLU(),
        )
        self.action_encoder = nn.Sequential(
            nn.Linear(ACTION_FEATURE_COUNT, 256),
            nn.ReLU(),
            nn.Linear(256, action_width),
            nn.ReLU(),
        )
        self.policy_head = nn.Sequential(
            nn.Linear(state_width + action_width, 256),
            nn.ReLU(),
            nn.Linear(256, 1),
        )
        self.value_head = nn.Sequential(
            nn.Linear(state_width, 128),
            nn.ReLU(),
            nn.Linear(128, 1),
            nn.Tanh(),
        )

    def encode_states(self, states: Tensor) -> Tensor:
        return self.state_encoder(states)

    def policy_logits(self, states: Tensor, actions: Tensor) -> Tensor:
        if states.shape[0] != actions.shape[0]:
            raise ValueError("policy requires one state row per action row")
        state_embedding = self.state_encoder(states)
        action_embedding = self.action_encoder(actions)
        return self.policy_head(torch.cat((state_embedding, action_embedding), dim=1)).squeeze(1)

    def policy_logits_grouped(
        self, state_embeddings: Tensor, actions: Tensor, action_owners: Tensor
    ) -> Tensor:
        action_embedding = self.action_encoder(actions)
        owned_states = state_embeddings.index_select(0, action_owners)
        return self.policy_head(torch.cat((owned_states, action_embedding), dim=1)).squeeze(1)

    def values_from_embeddings(self, state_embeddings: Tensor) -> Tensor:
        return self.value_head(state_embeddings).squeeze(1)

    def values(self, states: Tensor) -> Tensor:
        return self.values_from_embeddings(self.state_encoder(states))


class PolicyExport(nn.Module):
    def __init__(self, model: PolicyValueModel) -> None:
        super().__init__()
        self.state_encoder = model.state_encoder
        self.action_encoder = model.action_encoder
        self.policy_head = model.policy_head

    def forward(self, policy_input: Tensor) -> Tensor:
        state = policy_input[:, :STATE_FEATURE_COUNT]
        action = policy_input[:, STATE_FEATURE_COUNT:]
        state_embedding = self.state_encoder(state)
        action_embedding = self.action_encoder(action)
        return self.policy_head(torch.cat((state_embedding, action_embedding), dim=1))


class ValueExport(nn.Module):
    def __init__(self, model: PolicyValueModel) -> None:
        super().__init__()
        self.state_encoder = model.state_encoder
        self.value_head = model.value_head

    def forward(self, state_input: Tensor) -> Tensor:
        return self.value_head(self.state_encoder(state_input))

