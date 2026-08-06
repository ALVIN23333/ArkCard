# AI 基线

神经 MCTS 的对照组是迁移前的启发式 MCTS。任何训练或竞技场报告都必须记录以下两组参数，避免把场景配置与源码默认值混为一谈。

| 来源 | Iterations | Time budget | Rollout limit | Max root turns | Max actions/node |
| --- | ---: | ---: | ---: | ---: | ---: |
| `BattleScene/player2/AIController` 发布基线 | 300 | 35 ms | 4 | 2 | 10 |
| `MCTSSettings` 源码默认值 | 400 | 50 ms | 8 | 2 | 10 |

教师数据生成和候选模型竞技场默认使用发布基线 `300 / 35 ms / 4`。旧 MCTS 仍保留动作启发式排序和每节点前 10 个非结束回合动作裁剪；神经 MCTS 对全部合法动作评分。

每次正式基线运行应保存：Git commit、Unity 版本、CPU、随机种子范围、牌组索引、先后手、胜负/平局、每次决策耗时和 P95。
