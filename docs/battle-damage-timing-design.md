# 战斗伤害 Timing 设计

> 本文说明伤害阶段的职责划分，以及所有伤害共用的公共结算管线。阶段操作受能力型 Context 限制。
> Effect、Context 和消息流总览见 `battle-runtime-architecture.md` 与
> `battle-effect-phase-hook-design.md`。

## 目标

本文只定义伤害相关 timing 的职责边界，不讨论完整战斗状态机。

本文目标：

- 明确每个 timing 允许放哪些类型的效果
- 明确普通伤害与真实伤害分别走哪些 timing
- 避免把“命中前拦截”“防御修正”“扣血后触发”混在同一个 timing

## 伤害描述与公共结算管线

所有扣血都先生成不可变的 `BattleDamageDescriptor`，再交给 `BattleDamageResolver` 的唯一入口
`BattleEngine.ResolveDamage(...)`。不再有 `ApplyDirectDamage`、毒伤专用入口或 `runBefore/runAfter` 开关。
描述用强类型字段表达结算不变量，管线只解释字段，不按技能类别分支；内容 Tag 不承载这些不变量。

| 字段 | 取值 | 作用 |
| --- | --- | --- |
| `HitMode` | `Resolved` / `Judged` / `Guaranteed` | 调用方已判定命中；结算前按技能语义判定（闪避、目标侧反制、来源侧破闪避）；必中 |
| `Mitigation` | `Normal` / `IgnoreDefence` / `True` | 普通攻防公式；独立公式但仍是可被反制的攻击；真实伤害（不允许落血前改写） |
| `Origin` | `Skill` / `ExtraStrike` / `Ability` / `Reflection` / `Share` / `Periodic` / `SelfCost` | 归因 |
| `Reactions` | `PreApplication` / `TakenResponses` / `TakenRage` / `DealtResponses` / `Lifesteal` | 允许经过的可选阶段 |
| `UncappedBonusAmount` | ≥ 0 | 不受落血前封顶、随取消作废的追加伤害（见下文） |
| `MinimumRemainingHp` | ≥ 0 | 承伤后至少保留的 HP |

管线顺序：

1. 命中：`Judged` 时调用与普通技能相同的命中判定；被判定 MISS 时只记录 0 伤害事实。
2. 落血前改写（`PreApplication`）：目标的 `BeforeDamageApplied`。可封顶、取消、改写落点、`TransferDamage`
   转移为同一次攻击的其他承伤分量、`ReflectDamage` 反弹、`ShareDamage` 分摊。
3. 未被取消时，把 `UncappedBonusAmount` 加到主承伤者。
4. 逐个承伤分量扣血（转移出去的分量先结算，与 legacy 八卦阵一致），各自记录 `Damaged` 事实，并按开关触发
   承伤者的 `OnDamageTaken`、受击回怒；HP 归零时**总会**经过 `BeforeDefeated` / `OnDefeated`，不受开关影响。
5. 来源侧（`DealtResponses`）：按全部承伤分量的实际 HP 损失汇总，`OnDamageDealt` 与吸血整次只执行一次。

各来源的阶段策略由描述工厂集中定义：

| 来源 | 命中 | 减伤 | 落血前改写 | 承伤后响应 | 受击回怒 | OnDamageDealt | 吸血 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ForSkill` 普通技能 | 已判定 | 普通 | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ForExtraStrike` 追加攻击 | 沿用 | 普通 | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ForAbility` 特殊技能伤害 | 判定（可改必中） | 无视护甲（可改真实） | ✓（真实伤害时 ✗） | ✓ | ✓ | ✓ | ✗ |
| `ForReflection` 反弹 | 必中 | 真实 | ✗ | ✓ | ✗ | ✗ | ✗ |
| `ForShare` 分摊 | 必中 | 真实 | ✗ | ✓ | ✓ | ✓ | ✗ |
| `ForPeriodic` 中毒等 | 必中 | 真实 | ✗ | ✗ | ✗ | ✗ | ✗（至少留 1 HP） |
| `ForSelfCost` 施法代价 | 必中 | 真实 | ✗ | ✓ | ✗ | ✗ | ✗ |

说明：

- 特殊技能（火枪、过肩摔、同归剑法等）在 legacy `AttackLogic.Attack()` 中同样经过 MISS 判定和斗转星移、不老长春、
  真武挡刀、五行阵、八卦阵等目标侧反制，只跳过写在普通伤害公式里的乾坤大挪移、宝甲、精打细算和精明。因此能力伤害默认
  `Judged + IgnoreDefence`；`careful_defense`（精打细算/精明）只对 `Normal` 减伤的伤害封顶。
- 反弹以反弹者为来源，不能再被改写（阻止反弹链），被反弹者不因此受击回怒，反弹者也不触发 `OnDamageDealt`。
- 分摊（五行阵）仍是归因于原攻击者、不携带技能的独立伤害；转移（八卦阵）是同一次攻击的承伤分量，保留原攻击者、技能与归因。
- 施法代价（同归剑法、天地同寿的自伤）不可防御，但仍是真实 HP 损失：触发施法者自己的 `OnDamageTaken` 与战败阶段。

### 追加伤害（攀云乘龙、无形剑气）

legacy 把攀云乘龙、无形剑气的追加伤害加在精打细算/精明封顶之后。处理器通过
`BattleDamageCalculationContext.AddUncappedBonusDamage(...)` 记录，不进入普通伤害公式，`BeforeDamageApplied`
看到的伤害值不含它；落血前改写结束后，若伤害未被取消（斗转星移、不老长春等），再加到最终的主承伤者。
它不参与八卦阵的转移比例（legacy 转移的是含追加伤害的总值，这是有意保留的简化）。

## 核心原则

- 命中判断与伤害数值结算分开
- 防御体系与受伤事实分开
- 真实伤害绕过防御，不绕过受伤事实
- timing 按职责划分，不按某个具体技能命名

## 四个核心 Timing

### BeforeHitResolved

语义：

- 这一下是否算命中
- 这一下是否被直接拦截为 MISS / 无效命中

允许放的效果类型：

- 闪避
- 必中
- 破闪避
- 易容
- 溜须拍马
- 其他“命中是否成立”的判定

不应放的效果类型：

- 按攻击/防御/暴击修改伤害值
- 依赖“本次伤害数值”的减伤
- 受伤后触发

攻击方在该时机只会触发判定“未命中”（破闪避、失手）或不看命中状态的 hook；`context_hit_state` 为 `hit` 的条件只对受击方有意义，内容校验要求它显式写 `context_unit_role: target`，命中确认后的追加效果改用 `OnHitConfirmed`。

典型结果：

- 命中
- MISS
- 命中成立但禁止后续附加效果

结算分两阶段：先解析最终命中状态（只执行 `cancel_hit` / `set_hit_success`），再提交与最终结果一致的钩子副作用，规则见 [战斗运行时架构](battle-runtime-architecture.md#结算规则要点)。

当前适合挂载的 effect 原语：

- `cancel_hit`
  - 用于闪避、易容、溜须拍马这类直接让本次命中失效的效果
- `remove_buff`
  - 仅在“命中判定成立后立刻破功”这类场景使用
  - 典型例子是 `易容` 奇袭后移除自身 buff
- 其他 effect 原语默认不建议挂在这一层

### BeforeDamageCalculation

语义：

- 命中已经成立
- 现在进入普通伤害公式的数值修正

允许放的效果类型：

- 攻击乘区
- 防御乘区
- 暴击率修正
- 暴击倍率修正
- `final_damage` 修正
- 常规增伤
- 常规减伤
- 乾坤大挪移
- 宝甲
- 神经病
- 鲁莽

不应放的效果类型：

- 闪避
- 斗转星移
- 不老长春
- 受伤回怒

说明：

- 该 timing 只服务普通伤害
- 真实伤害不走这一层
- `final_damage` 只表示普通伤害公式内部的最后一层纯数值修正
- `final_damage` 不负责分摊、转移、溢出转资源、清零并回血这类带副作用的落血改写

当前适合挂载的 effect 原语：

- `modify_damage_context`
  - 这是这一层的主力原语
  - 常见字段：
    - `source_attack`
    - `source_attack_low`
    - `source_attack_high`
    - `target_defence`
    - `critical_chance`
    - `critical_multiplier`
    - `final_damage`
- 不建议在这一层挂：
  - `cancel_hit`
  - `add_rage`
  - `set_rage`
  - `set_action_gauge`
  - `add_hp`
  - `add_mp`

### BeforeDamageApplied

语义：

- 命中已经成立
- 当前已经得到一个伤害值
- 但目标还没有真正扣血

允许放的效果类型：

- 依赖“本次伤害值”的防御性改写
- 伤害封顶
- 伤害减半
- 伤害分摊
- 伤害转移
- 斗转星移
- 不老长春
- 其他“拿到伤害值后，再决定怎么落血”的效果

不应放的效果类型：

- 闪避
- 攻击/防御/暴击的公式修正
- 受伤后触发

说明：

- 这一层本质上是“伤害落地前的防御与反制”
- 如果某个机制需要读取本次最终伤害值，但又会改写伤害落点，应放在这里

当前适合挂载的 effect 原语：

- 当前运行时已提供 `BeforeDamageApplied`，并以 `modify_damage` 和少量 custom effect 承载落血前改写
- 后续主要会落到这里的 effect 方向：
  - 伤害封顶
  - 溢出转资源
  - 分摊 / 转移
  - 反弹 / 反制
  - 清零并回血
- 已有原语中，原则上只有“确实依赖最终伤害值并带副作用”的将来专用 effect 才应进入这一层
- `modify_damage_context` 不应继续承担这类职责

### OnDamageTaken

语义：

- 目标已经实际扣血
- 现在处理“受伤之后”的反应

允许放的效果类型：

- 受击回怒
- 受到伤害后触发被动
- 受伤叠层
- 受伤后回血/回蓝/回能量
- 受伤统计
- 受伤后的表现事件

不应放的效果类型：

- 闪避
- 命中拦截
- 普通伤害公式修正
- 斗转星移
- 不老长春

说明：

- 这一层看的是“最终实际损失了多少 HP”
- 如果伤害被完全拦截或被改写成 0，则不应进入这一层

当前适合挂载的 effect 原语：

- `add_rage`
  - 受击回怒
- `apply_buff`
  - 受伤后叠层或追加状态
- `add_hp`
  - 受伤后回血
- `add_mp`
  - 受伤后回蓝
- `remove_buff`
  - 受伤后消耗某个状态
- `remove_negative_buffs`
  - 极少数“挨打后清异常”机制
- `remove_positive_buffs`
  - 极少数“挨打后破状态”机制
- 原则上不建议在这一层挂：
  - `cancel_hit`
  - `modify_damage_context`

## 普通伤害与真实伤害

普通伤害（外功、招式、追加攻击）：`BeforeHitResolved` → `BeforeDamageCalculation` → `BeforeDamageApplied`
→ 扣血 → `OnDamageTaken` / 战败阶段 → 攻击方 `OnDamageDealt` 与吸血。

真实伤害（`Mitigation = True`）跳过普通公式和全部落血前改写，直接扣血，仍触发描述允许的承伤后响应和战败阶段。
独立公式但仍可被反制的能力伤害使用 `IgnoreDefence`，与真实伤害是两种不同语义，不再把“独立公式”与
“必中 / 不可反弹 / 不可转移 / 不可吸收”绑定。

## 效果归类规则

判断某个效果该挂在哪个 timing，可按下面的顺序判断：

1. 它是在判定“打中没打中”吗？
   如果是，放 `BeforeHitResolved`

2. 它是在改普通伤害公式里的攻击、防御、暴击或最终倍率吗？
   如果是，放 `BeforeDamageCalculation`

3. 它需要先拿到本次伤害值，再决定是否减伤、分摊、转移、反制吗？
   如果是，放 `BeforeDamageApplied`

4. 它要求目标已经真实掉血后才生效吗？
   如果是，放 `OnDamageTaken`

## 典型映射

### 命中前拦截

- 易容：`BeforeHitResolved`
- 溜须拍马：`BeforeHitResolved`
- 闪避类天赋：`BeforeHitResolved`
- 必中类天赋：`BeforeHitResolved`

### 普通伤害公式修正

- 乾坤大挪移：`BeforeDamageCalculation`
- 宝甲：`BeforeDamageCalculation`
- 神经病：`BeforeDamageCalculation`
- 鲁莽：`BeforeDamageCalculation`

### 落血前防御/反制

- 不老长春：`BeforeDamageApplied`
- 斗转星移：`BeforeDamageApplied`
- 分摊/替挡：`BeforeDamageApplied`
- 伤害封顶：`BeforeDamageApplied`
- 溢出转资源：`BeforeDamageApplied`

### 扣血后反应

- 受击回怒：`OnDamageTaken`
- 受到伤害后触发的被动：`OnDamageTaken`

## 非目标

本文不解决以下问题：

- 治疗是否需要独立 `BeforeHealApplied / OnHealed`
- 一次技能多目标命中、多段追加攻击之间的事件聚合（同一次伤害的多个承伤分量已在管线内聚合）

这些问题后续再拆文档。

## 落地建议

- 运行时必须保持 `BeforeDamageCalculation -> BeforeDamageApplied -> TakeDamage -> OnDamageTaken` 的顺序
- 真武七截阵的挡刀属于 `BeforeDamageApplied`；乾坤大挪移和宝甲继续使用 `BeforeDamageCalculation + final_damage`
- 新的伤害来源只新增描述工厂（或选择已有工厂的参数），不新增结算入口
- `BeforeDamageApplied` 只保留真正依赖“本次伤害值”的防御与反制效果，不要再次混入闪避或普通攻防修正
