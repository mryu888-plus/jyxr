# 战斗运行时架构

## 分层方向

依赖固定为：

```text
Game.Godot battle host
    ├── Game.Application battle use cases ──→ Game.Core battle runtime
    └── Game.Presentation battle UI flow  ──→ Game.Core battle runtime
```

Core 不依赖 Application、Presentation 或 Godot。Application 负责根据 request 建立战斗、结算和 carryover；
Presentation 负责与 UI 引擎无关的交互状态和展示流程；Godot 负责输入、控件、资源和演出适配。

## Core

`BattleEngine` 保留命令入口和战斗时间线，具体职责下沉到：

- `BattleSkillExecutor`：技能计划；
- `BattleEffectExecutor`：统一 Effect 执行；
- 各状态 resolver：伤害、恢复、Buff、资源与成长；
- `BattleHookRunner`：角色与 Buff Hook 的收集、稳定顺序和 Trace；
- `BattleOutcomeEvaluator`：阵营存活结果；
- `BattleUnitSelectorResolver` / `BattleSkillTargeting`：Effect 与技能空间目标；
- `BattleExecutionScope`：Effect 链顺序和递归保护。

`BattleUnit` 继续直接引用 `CharacterInstance`，战斗成长立即写入角色实例。

### 结算规则要点

- **技能施放上下文**：`BattleSkillExecutor` 每次施放创建 `BattleSkillExecution`（挂在 `BattleState.ActiveSkillExecution`），记录本次实际作用到的单位：技能伤害与技能附带状态的目标，以及技能定义效果经选择器（如 `all_enemies`）解析出的目标；钩子效果不计入。施放结果的 `AffectedUnitIds` 取自这里。
- **主动攻击回怒**：整次施放只判定一次，条件是本次实际作用到了敌方单位（对自身施放、再由效果作用全体敌人的特殊技能也会回怒）。
- **受击回怒**：沿用 legacy `AttackLogic` 的攻击级语义——同一次主动施放内，每个受击单位最多判定一次（按 `50% + 福缘 / 1000`）；追加攻击、多段伤害、直接伤害不再各自判定。没有施放上下文的伤害（Buff 周期伤害等）仍逐次判定，关闭承伤后响应的伤害不判定。
- **两阶段命中结算**：`BeforeHitResolved` 依次执行目标侧闪避/反制、来源侧破闪避、来源侧失手，再按命中率补判；这一阶段只落地 `cancel_hit` / `set_hit_success`。最终命中状态确定后才提交已触发钩子的其余效果、飘字与台词，且只提交与最终结果一致的钩子：取消命中的钩子要求最终未命中，强制命中的钩子要求最终命中，声明了 `context_hit_state` 的钩子要求最终状态仍满足条件。条件里的概率只判定一次。
- **Debuff 施加顺序**（与 legacy 一致）：圣战免疫 → 抗性判定 → `BeforeBuffApplied` 增强 → 与现有同 ID 状态比较。圣战与抗性成功都静默跳过，不产生事实、不飘字，也不触发 `BeforeBuffApplied`；进入增强阶段后即使最终比较未覆盖现有状态，增强钩子的台词与飘字仍会出现（legacy 同样如此）。
- **active buff 快照**：`BattleUnit.ActiveBuffs` / `TryGetBuff` 读取按需构建的快照；buff 增删以及 buff 等级、剩余回合变化（`Strengthen`、`Reduce`、回合消耗）都会通知所属单位，统一刷新快照、局部投影并钳制资源。
- **CanImpactSelf**：表示施法者本人一定承受该技能（legacy `HitSelf`）。施法者位于覆盖范围内时直接成为目标，不经过阵营、难度和 `AvoidFriendlyFire` 的友伤过滤；其它友军仍按友伤规则判定。
- **方向型覆盖**：`line` / `fan` / `cleave` 按“施法者 → 目标格”的方向展开，禁止对自身格施法。内容装配拒绝为这些覆盖类型（含按武器默认得到的直线/劈砍，以及继承父技能覆盖类型的招式）显式开启 `canCastAtSelf`；对自身格查询覆盖范围得到空集。
- **技能范围修改**：`skill_targeting_modifier` 用可选的 `selector` 选择技能（省略表示全部技能）：`sourceSkillIds`（按来源技能匹配，招式/奥义匹配父技能）、`weaponTypes`、`skillKinds`（技能形态 `External/Internal/Form/Special/Legend`）、`sourceKinds`（来源类别 `external/internal/special`，招式与奥义继承父技能）。同一条件内为“或”，不同条件为“且”；声明的条件不能为空列表。单位的全部范围修改按内容原始顺序（角色 → 战斗内 Buff → 场景效果）依次应用，多个 `override` 以最后一个为准。

```json
{
  "type": "skill_targeting_modifier",
  "selector": {"weaponTypes": ["jianfa"], "skillKinds": ["Form"]},
  "field": "cast_size",
  "value": {"op": "add", "delta": 1}
}
```

## 命令与消息

所有改变状态的引擎命令返回 `BattleCommandResult<T>`：

- `Success` 和失败信息；
- 命令业务结果；
- 本次命令产生的单一有序 `BattleMessage` 列表。

消息分为：

- `BattleFact`：伤害、恢复、移动、Buff、成长等已发生事实；
- `BattleCue`：飘字、语音等宿主表现请求；
- `BattleTrace`：Hook 触发与 execution scope 诊断。

`BattleState` 不保存 UI 事件队列，调用方也不能再从 action result 和 state 两处消费事件。

## Application

`BattleService` 是 `GameSession` 的薄门面，对外接收 `SpecialBattleRequest`。内部由：

- `BattleStateFactory` 装配普通、竞技场和珍珑战斗；
- `ProceduralBattleCharacterFactory` 生成随机敌人和难度强化；
- `ZhenlongqijuBattleFactory` 处理珍珑专属内容；
- `BattleSettlementService` 预览并应用奖励；
- `BattleCarryoverService` 回写角色资源并恢复队伍资源。

不保留字符串/数组构建重载或测试专用公开入口。

## Presentation

`Game.Presentation` 是普通 .NET 程序集，只依赖 `Game.Core`。战斗流程包括：

- `BattleFlowStateMachine`：串行分发标准化 UI intent、校验状态转换，并丢弃来源状态已经失效的排队输入；
- 各 `IBattleFlowState`：负责进入/退出、允许的后继状态、当前交互能力、规则操作和后续转换；
- `IBattleFlowContext`：声明规则命令、AI、演出、结算和视图提交所需的宿主能力。

这里不引用 Godot 节点、信号、资源或 Tween。普通 .NET 测试直接引用该程序集。

## Godot

`BattleScreen` 保留为场景根脚本和控制器装配点。普通类分别负责：

- `BattleFlowContext`：实现 Presentation 定义的宿主能力，集中适配规则命令、AI、演出、结算和视图提交；
- `BattleBoardController`：被动渲染棋盘单位与目标高亮；
- `BattleActionPanelController`：被动渲染行动按钮、技能和物品/状态面板；
- `BattleEventPresenter`：Fact、Cue 的宿主呈现；Trace 当前保留给按需诊断，不进入玩家表现；
- `BattleSkillPresentationController`：技能与移动演出；
- `BattleSettingsController`：速度、自动战斗和设置；按钮操作立即影响本场运行态，但只在战斗结束时统一持久化；
- `BattleSettlementController`：carryover、奖励和结束 Task。

Godot 画面采用动作级提交：`RenderInteraction` 只更新高亮与操作能力，
`CommitBattleStateToView` 才把规则态中的单位、血条、Buff、位置和败北状态提交到棋盘。
技能或移动进入 `PresentingActionState` 时先清除交互高亮，规则可以即时结算；演出完成后再提交最终状态，
避免败北单位在攻击动画结束前消失。玩家与 AI 动作共用同一演出和提交边界。

胜负由 Core evaluator 输出；Core 的平局目前由外层按我方失败处理。演出期间投降会立即登记并锁定后续命令，
但等待当前动作演出完成后再进入失败结算。
