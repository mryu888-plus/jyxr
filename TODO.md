# TODO

- `nvliumang`（移动）、`nvpanjun`（移动）、`huangrong`（移动、受击） 的模型动画帧区域、播放顺序与偏移均由 AI 根据现有图集推断，可能不正确；后续需要结合原始动画数据或人工逐帧校验并修正。
- 内容加载目前只是“DTO 校验 + 按顺序构建 runtime definitions”，还不是真正的二阶段加载。后续如需支持定义间循环依赖，应改成“先注册 runtime definition 空壳，再统一 resolve 引用”的两阶段装配流程。
- 当前 affix 引用解析集中在 `JsonContentLoader.ResolveAffixes(...)`，而不是各 definition 自己的 `Resolve(...)` 方法。后续如继续扩展 affix 来源，应把 affix 引用解析入口收敛成更明确的专门服务或 definition 统一协议，避免新增来源时漏掉 `GrantTalentAffix` 等需要 resolve 的条目。
- `CharacterProjection.EffectiveTalents` / `CharacterInstance.HasEffectiveTalent(...)` 当前实际表达“已学习或经 Affix 授予的候选天赋闭包”：被其他天赋通过 `ReplaceTalentIds` 替换后，只屏蔽其 Affix 效果，仍保留在该集合中并继续满足天赋存在性判断。这个行为符合当前设计，但 `Effective` 命名容易让人误以为被替换天赋已从集合移除；后续应将其重命名为能表达候选/可判断语义的名称，或显式拆分“候选天赋”和“实际贡献效果的天赋”两套查询，避免内容条件和定制 handler 误用。
- C# 自定义战斗效果当前只在 `CustomBattleEffectRegistry.Bind(...)` 中反序列化并校验参数形状，`JsonContentLoader` 会跳过其参数内的内容引用；`random_debuff`、`soul_chasing`、`attribute_contest_debuff` 等效果仍在战斗触发时按字符串解析 Buff。后续应让自定义效果参数显式参与内容装配和引用解析，把 Buff、技能、天赋等稳定 ID 转成已校验的 runtime definition 引用，使缺失引用在 MOD 加载阶段失败，不延迟到战斗路径抛错。
- `博览群书` 当前先用现有无参数 trait 表达，并在奥义触发率计算中写死触发倍率 `+0.5`；后续如果类似规则增多，可考虑把 trait 升级为带参数规则 trait，避免规则数值长期散落在代码里。
- 四系中文名称目前分为属性名 `拳掌 / 剑法 / 刀法 / 奇门` 与技能系名 `拳掌 / 剑 / 刀 / 奇门`；后者为复刻 legacy 的“拳掌系 / 剑系 / 刀系 / 奇门系技能加成”文案，暂在 `AffixFormatter` 内局部映射。后续应把两套语义不同的名称集中到 `FormatterTextCn` 等统一文本入口，明确区分属性名与技能系名，避免新增四系词条、筛选或 UI 文案时重复散落映射。
- 统一空值校验策略：内部代码以 C# nullable reference types 为主，边界层（宿主入口、内容加载入口、外部输入入口）保留显式运行时校验，避免在内部领域代码中堆积大量 `ArgumentNullException.ThrowIfNull`。
- 当前地图采用地点与事件模型，已由 `MapDefinition`、`LocationState` 和 `MapEventProgressState` 承载。后续如需连续地形、区域、可通行性或场景切换锚点，再扩展相应模型。
- 地图交互对象待建模：`InteractableDefinition`、`InteractableState`、`MapObjectDefinition`、`MapObjectState`，用于承载宝箱、门、机关、调查点、采集点、触发器等。
- 剧情已有对白、选项及宿主演出命令。后续如需要统一镜头、角色入退场和多轨演出的时序，再设计独立演出序列模型。
- 地图与战斗的衔接对象待建模：`BattleEncounterDefinition`、`EncounterTriggerDefinition`、`BattleResultProjection`，用于表达地图遭遇战入口、触发条件，以及战斗结果如何回写世界状态。
- `BeforeHitResolved` 应只承载命中结果确定前的闪避、破闪避、失手等逻辑。后续内容校验应禁止来源侧 `context_hit_state == hit` 的 hook，命中确认后的追加效果应使用 `OnHitConfirmed`，避免数据写法被运行时静默跳过。
- 当前 DTO 与 runtime definition 存在较多重复字段。后续应收缩 DTO 噪音：优先只保留确有 raw/runtime 差异的类型，并考虑把 DTO 模型进一步内聚到 loader 内部，避免扩散成整层平行定义。
