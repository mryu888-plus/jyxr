# 战斗运行时后续议题

本文只记录当前重构完成后仍未实施或仍需规则确认的事项。现行结构见
`battle-runtime-architecture.md`，伤害顺序见 `battle-damage-timing-design.md`。


## 2. 死亡与特殊胜负条件

Core 当前只根据阵营存活情况输出 `Ongoing`、胜方或 `Draw`；外层暂把平局按我方失败
处理。公共伤害结算管线对所有伤害来源都必经 `BeforeDefeated`、`OnDefeated`、战败 Fact 和
范围效果清理，败北阻止经 `TryPreventDefeat` 原子提交。后续议题是独立复活、击杀触发和特殊胜负条件。

## 3. 通用 Effect timing 白名单

当前对真武增伤、真武挡刀、怪病等专用 Effect 同时做能力类型和 timing 校验；通用 Effect
仍沿用现有 timing policy。待内容规模确实需要时，再把所有 Effect 的合法 timing 建成统一
装配校验，不为形式完整性扩充矩阵。

## 4. 回放与诊断

命令结果已经返回有序的 Fact、Cue、Trace，`BattleState` 不保存 UI 消费队列。若后续需要
回放或长期诊断，应建立独立 `BattleHistory`，由宿主或调试设施选择性保存消息批次，不能
恢复状态与 UI 双事件源。

## 5. 仍明确不纳入

- 改变 `BattleUnit -> CharacterInstance` 的直接关联；
- 禁止战斗中即时成长或拆 `BattleUnit` 内部结构；
- 调整 `BattleEngine` 公共构造签名；
- 统一 AI 与预览执行模型；
- 重做战斗物品原子性；
- 引入完整 ASC、AttributeSet 或 Tag 体系；
- 在现有职责仍内聚时继续机械拆分 `BattleBoardView`。
