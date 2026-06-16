# St6 BUFF 同步规则草稿

创建时间：2026-06-15

本文件用于后续制作“St6-BUFF”页面和“虚幻同步台 -> BUFF 同步”。当前阶段只做调查和规则沉淀，不直接实装页面。

## 目标范围

- Unreal 项目：`D:\UnrealMap\CrossingVoid`
- 角色蓝图根目录：`Content\GameActor2D\<角色英文代号>`
- BUFF 目录：`Content\GameActor2D\<角色英文代号>\BUFF`
- Unreal 内显示路径：`/All/Game/GameActor2D/<角色英文代号>/BUFF`
- 示例 BUFF：`D:\UnrealMap\CrossingVoid\Content\GameActor2D\ALO_Yuki\BUFF\Alo_Yuki_BUFF1.uasset`
- BUFF 基类：`UDreamTask`
- BUFF 基类头文件：`D:\UnrealMap\CrossingVoid\Plugins\DreamGameplayTask-master\Source\DreamGameplayTask\Public\Classes\DreamTask.h`
- 战斗角色底层：`ACrossvoid2DatkNew`
- 战斗角色头文件：`D:\UnrealMap\CrossingVoid\Plugins\CrossMainData\Source\CrossMainData\Public\CrossTurn2D\Crossvoid2DatkNew.h`

## 读取原则

`.uasset` 不能作为稳定数据源直接手搓解析。二进制字符串可以辅助发现字段名和资源混杂情况，但不能可信读取默认值、父类、图标路径、中文文本、蓝图变量值。

正式同步必须走 Unreal Python 导出：

1. 通过 AssetRegistry 扫描 `/Game/GameActor2D/<角色>/BUFF`。
2. 加载资产并判断是否为 Blueprint。
3. 判断 GeneratedClass / ParentClass 是否继承 `UDreamTask`。
4. 只有继承 `UDreamTask` 的蓝图才进入 BUFF 列表。
5. 同目录下的 Texture2D、`*_Icon` 等素材不能当作 BUFF。
6. 导出 JSON 后由工具箱读取，St6 保存仍然落到角色自己的工具箱 JSON，不做 UI 缓存。

## 已确认的 DreamTask 字段

`UDreamTask` 本体提供的是任务框架字段：

- `TaskName`：任务唯一名，适合作为 BUFF 识别名或内部代号。（这是BUFF的名称）
- `TaskDisplayName`：显示名称，适合作为 St6 的 BUFF 名称。（这里会写BUFF归属于谁，一般由工具箱直接填写）
- `TaskDesc`：描述，适合作为 St6 的 BUFF 说明。
- `TaskType`：任务类型，暂时只作为原始信息预览，不建议第一版强行分类。（我明说一下类型枚举，分别是发送方-属性、发送方-最终、接收方-属性、接收方-最终）
- `SubTasks`：子任务，第一版可只展示数量或忽略。（忽略，用不到这个）
- `TaskPriority`：优先级，第一版可展示原始值。（低、正常、高、紧急，默认为正常）
- `TaskData`：扩展数据，`UDreamTaskData` 内含 `TaskIcon`、`TaskImage`、`TaskType`。（其中TaskIcon是BUFF的图标，可以根据这个作为BUFF卡片的封面，TaskType的枚举是：增益型、削弱型）
- `TaskCompletedCondition`：条件容器，包含条件映射、完成模式、自定义完成数量。
- `TaskState`：运行时状态，编辑器配置里通常不作为制作字段。

`UDreamTaskData` 关键字段：

- `TaskIcon`：BUFF 图标优先来源。
- `TaskImage`：BUFF 大图或补充图，第一版可先不做主流程。
- `TaskType`：任务类型补充来源。

`UDreamTaskConditionTemplate` 关键字段：

- `ConditionDisplayName`：条件显示名。
- `ConditionDesc`：条件描述。
- `Count`：条件数量。
- `CompletedCount`：完成数量，默认值在头文件中为 `1`。
- `bTaskMustBeCompleted`：是否必完成。

## 强度和层数

所有已识别的 BUFF 蓝图二进制字符串中都出现了：

- `BCount`
- `BPower`

这两个字段很可能就是当前项目 BUFF 制作里约定的：

- `BCount`：层数或可叠加数量。（这个BUFF能有多少次触发，比如Complete是2，默认的Count是0，那么BUFF获取后，触发两次BUFF就会进入完成状态）
- `BPower`：强度或倍率参数。（这个BUFF的强度，Complete只用作强度上限的限制）

但是这里只能确认字段名存在，不能直接确认数值。正式同步时必须由 Unreal Python 读取蓝图 CDO 或 Blueprint Generated Class 的默认值后再写入工具箱。St6 第一版建议把这两个字段作为核心列：

- 层数：读取 `BCount`，没有读到则显示“未读取”。
- 强度：读取 `BPower`，没有读到则显示“未读取”。

如果后续发现某些 BUFF 使用 `Count` / `CompletedCount` 表示层数，则 St6 可以增加“条件数量”展示，但不要把条件数量直接覆盖 `BCount`。

## 战斗角色中的 BUFF 入口

`ACrossvoid2DatkNew` 内有：

- `BuffComponent`：类型为 `UDreamTaskComponent`，显示名“Buff组件”，是角色持有 BUFF 的任务组件。
- `UDreamTaskComponent` 支持 `GiveTaskByClass`、`RemoveTaskByClass`、`HasTaskByClass`、`HasTaskByName`、`UpdateTask` 等接口。

当前 BUFF 蓝图主要通过角色上的委托触发逻辑。已在战斗角色头文件中确认的委托：

- `FActionStart`：回合开始时。
- `FActionEnd`：回合结束时。
- `FSkillStart`：技能开始时。
- `FSkillEnd`：技能结束时。
- `FDamageStart`：伤害打出时。
- `FOnDamage`：受到伤害时，带攻击者参数。
- `FOnDamageSuccess`：成功打出伤害时。
- `FOnHealth`：受到治疗时。
- `FOnHealthSuccess`：成功治疗时。
- `FDefDefenseTriggerOn`：防御守备触发时。
- `FAtkDefenseTriggerOn`：反击守备触发时。
- `FDogDefenseTriggerOn`：闪避守备触发时。
- `FOnDeath`：死亡时。
- `FOnKill`：成功击杀时。
- `FSkillDiscard`：技能丢弃时。

当前二进制字符串抽样中，明确出现的触发委托主要是：

- `FActionStart`：19 个 BUFF。
- `FActionEnd`：15 个 BUFF。
- `FOnDamage`：1 个 BUFF。

这不代表其他委托没有用，只能说明当前已有资产里主要使用了回合开始、回合结束和受伤触发。

## 当前项目 BUFF 清单

以下清单根据 `Content\GameActor2D\<角色>\BUFF` 扫描，并用二进制字符串粗判是否包含 `DreamGameplayTask` / `DreamTask`。正式实现仍需用 Unreal Python 父类判断复核。

| 角色              | DreamTask BUFF 数 | BUFF 蓝图候选                                                                                                                                      | 非 BUFF 资源                                                                                                             |
| --------------- | ----------------:| ---------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| ALO_Yuki        | 3                | Alo_Yuki_BUFF1, Alo_Yuki_BUFF2, Alo_Yuki_BUFF3                                                                                                 |                                                                                                                       |
| Angel_Yuki      | 8                | Angel_Yuki_Buff1, Angel_Yuki_Buff2, Angel_Yuki_Buff3, Angel_Yuki_Buff4, Angel_Yuki_Buff5, Angel_Yuki_Buff6, Angel_Yuki_Buff7, Angel_Yuki_Buff8 |                                                                                                                       |
| N_Sinclair      | 2                | N_Sinclair_Buff1, N_Sinclair_Buff2                                                                                                             | 狂信, SAN_De                                                                                                            |
| Origin_Akatsuki | 1                | Origin_Akatsuki_BUFF1                                                                                                                          |                                                                                                                       |
| Origin_Ako      | 3                | Origin_Ako_BUFF1, Origin_Ako_BUFF2, Origin_Ako_BUFF3                                                                                           |                                                                                                                       |
| Origin_Miku     | 9                | Miku_BUFF1, Miku_BUFF2, Miku_BUFF3, Miku_BUFF4, Miku_BUFF5, Miku_BUFF6, Miku_BUFF7, Miku_BUFF8, Miku_BUFF9                                     | Miku_BUFF1_Icon, Miku_BUFF2_Icon, Miku_BUFF3_Icon, Miku_BUFF4_Icon, Miku_BUFF5_Icon, Miku_BUFF6_Icon, Miku_BUFF8_Icon |
| Origin_Tatsuya  | 3                | Origin_Tatsuya_BUFF1, Origin_Tatsuya_BUFF2, Origin_Tatsuya_BUFF3                                                                               |                                                                                                                       |
| SAO_Ausna       | 3                | SAO_Asuna_DamageBuff, SAO_Asuna_Sklock, SAO_Asuna_SpeedBuff                                                                                    | SAO_Asuna_SpeedBuff_Icon                                                                                              |
| SAO_Kirito      | 4                | SAOtr_Buff1, SAOtr_Buff2, SAOtr_Buff3, SAOtr_Buff4                                                                                             |                                                                                                                       |
| UW_Alice        | 3                | UW_Alice_BUFF1, UW_Alice_BUFF2, UW_Alice_BUFF3                                                                                                 | AliceBUFF1                                                                                                            |

注意：

- `N_Sinclair\狂信.uasset` 和 `N_Sinclair\SAN_De.uasset` 当前表现为 Texture2D / 非 DreamTask，不应进入 BUFF 列表。
- `UW_Alice\AliceBUFF1.uasset` 当前表现为 Texture2D / 非 DreamTask，不应进入 BUFF 列表。
- `Origin_Miku` 和 `SAO_Ausna` 的 `*_Icon` 是图标资源，不应进入 BUFF 列表，但可作为图标来源或校验对象。
- 命名不统一，不能依赖 `BUFF` / `Buff` / 中文名判断类型。（之后工具箱内制作的BUFF要统一命名：{角色英文名}_BUFF-{默认排序/手动备注的英文}）

## St6 页面建议字段

第一版 St6 建议每张 BUFF 卡显示：

- 图标：来自 `TaskData.TaskIcon`，如果没有则尝试同目录命名图标，再失败则显示“图标未读取”。
- 名称：优先 `TaskDisplayName`，为空则用资产名。
- 内部名：`TaskName` 或资产名。
- 描述：`TaskDesc`。
- 层数：`BCount`。
- 强度：`BPower`。
- 触发时机：从蓝图引用的角色委托推断，例如回合开始、回合结束、受伤时。
- 条件摘要：`TaskCompletedCondition.Conditions` 的 key、`Count`、`CompletedCount`、完成模式。
- 原始资产路径：折叠显示，方便排查。
- 读取状态：成功、缺图标、缺名称、缺 BCount、缺 BPower、非 DreamTask 被忽略等。

第一版不建议直接让用户编辑复杂蓝图逻辑。St6 可以先做“读取/预览/同步到工具箱”，之后再做“工具箱同步回虚幻”。

## 同步到工具箱的建议数据落点

工具箱已有：

- `Models\BuffModels.cs`
- `Services\BuffService.cs`
- `ViewModels\BuffsViewModel.cs`
- `CharacterToolboxData.Buffs`

BUFF 数据应该继续保存在角色自己的工具箱 JSON 中，而不是存在 UI 控件里。同步逻辑建议：

1. Unreal 同步台读取 BUFF 候选。
2. 用户选择角色候选。
3. St6 预览显示当前 Unreal 角色的 BUFF。
4. 点击“同步到工具箱”后写入当前角色 `CharacterToolboxData.Buffs`。
5. 图标复制到当前角色目录 `BUFF\<GeneratedCode>\<GeneratedCode>-Icon.png`，或者沿用 St2/St4 的素材复制规范。
6. 同步后刷新 St6 ViewModel，并触发延迟保存。

## Unreal Python 导出建议

后续应在 `Tools\Unreal\export_zd_assets.py` 内加入固定导出函数，不动态生成脚本。

建议 manifest 新增字段：

```json
{
  "characterBuffs": [
    {
      "characterCode": "ALO_Yuki",
      "assetName": "Alo_Yuki_BUFF1",
      "objectPath": "/Game/GameActor2D/ALO_Yuki/BUFF/Alo_Yuki_BUFF1.Alo_Yuki_BUFF1",
      "generatedClassPath": "...",
      "parentClass": "DreamTask",
      "taskName": "...",
      "displayName": "...",
      "description": "...",
      "taskPriority": "...",
      "taskIconObjectPath": "...",
      "taskIconExportedFilePath": "...",
      "bCount": 0,
      "bPower": 0,
      "conditions": [
        {
          "key": "BCount",
          "displayName": "...",
          "description": "...",
          "count": 0,
          "completedCount": 1,
          "mustBeCompleted": false
        }
      ],
      "completionMode": "All",
      "triggerDelegates": ["FActionStart"],
      "readMessage": ""
    }
  ]
}
```

导出注意点：

- `asset.get_asset()` 得到 Blueprint 后，优先读 `generated_class` 的 CDO。
- 用 `unreal.get_default_object(generated_class)` 或同等方式读默认对象字段。
- 读取 `TaskData` 后再取 `TaskIcon`。
- 图标如果是 Texture2D，复用现有 PNG 导出逻辑。
- `BCount` / `BPower` 要用多候选属性名读取：`BCount`、`bCount`、`b_count`；`BPower`、`bPower`、`b_power`。
- 父类判断必须递归检查是否继承 `UDreamTask`，不要只判断名字。
- 导出过程属于长操作，工具箱调用时必须走全局进度条。
- 读取失败要写入 `readMessage`，并在 UI 和 Log 中明确显示具体资产和字段。

## F4 填写法则草案

后续进入 St6 页面时，F4 可以显示：

- BUFF 蓝图必须放在 `Content/GameActor2D/<角色英文代号>/BUFF`。
- BUFF 蓝图必须继承 `DreamTask` 或其子类。
- 图标优先放在 `TaskData.TaskIcon`。
- BUFF 名称优先填写 `TaskDisplayName`。
- BUFF 描述优先填写 `TaskDesc`。
- 层数建议统一使用 `BCount`。
- 强度建议统一使用 `BPower`。
- 触发逻辑建议绑定角色委托，例如回合开始、回合结束、受伤时。
- 同目录贴图、`*_Icon`、普通 Texture2D 不会被当作 BUFF 蓝图。

## 未确认问题

这些问题需要下一步用 Unreal Python 实际导出后确认：

- `BCount` 的真实默认值和单位。（是整数类型，Count默认值是0，Complete默认是3）
- `BPower` 的真实默认值和单位。（是整数类型，Count默认值是1，Complete默认是10）
- `TaskDisplayName` / `TaskDesc` 是否所有 BUFF 都有中文内容。（这些是给玩家看的介绍，所以基本都是中文）
- `TaskData.TaskIcon` 是否所有 BUFF 都设置；未设置时是否存在稳定的同目录图标命名。（这是所有BUFF的图标，很重要，不过有很多时候图标是复用的，所以要想个办法让复用的图片好检查，然后专属的图标就直接分类仅对应的BUFF文件夹内，我现在就是手动这么摆放的。然后如果有BUFF还没设置图标，就用【"D:\BUFFatk.PNG"】这个图来当默认图标吧。记得图标的分辨率也要规范到125x125）
- 部分 BUFF 未在二进制字符串里显示触发委托，是否是靠条件或其他节点触发。（关于BUFF的委托和内部逻辑触发/函数不用管，我会自己在引擎内制作）
- `TaskCompletedCondition.Conditions` 的 key 是否固定使用 `BCount`、`BPower`，还是每个蓝图自定义。（是固定的，这是BUFF机制的基础）
