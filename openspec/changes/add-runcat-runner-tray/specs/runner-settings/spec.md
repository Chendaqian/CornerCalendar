## Purpose

定义设置窗口的跑者预览与选择能力：新增"跑者"分类实时动画预览全部跑者并支持点击选中持久化，"关于"分类补充 RunCat365 项目与 RunnerGallery 资源来源署名外链；同时移除"显示"分类中随任务栏时钟功能一并废弃的时间格式设置。

## ADDED Requirements

### Requirement: 设置窗口提供"跑者"分类
设置窗口左侧分类导航 SHALL 新增"跑者"分类，位于"显示"与"关于"之间；选中该分类时 SHALL 显示全部已分发跑者，每个跑者以可读名称（由文件夹名转换，如 welsh-corgi-frames 显示为 Welsh Corgi）标识。分类切换、滚轮切换与窗口拖动等既有交互 SHALL 不受影响。

#### Scenario: 打开跑者分类
- **WHEN** 用户打开设置窗口并在左侧导航选择"跑者"
- **THEN** 内容区显示全部 40 个跑者条目及其可读名称，其他分类面板保持隐藏

#### Scenario: 既有分类不受影响
- **WHEN** 用户在新增"跑者"分类后依次切换常规、日历、森日程、节日、天气、今昔、显示、关于
- **THEN** 每个分类显示各自原有内容，无错位或空白面板

### Requirement: 跑者预览实时播放动画
"跑者"分类中每个跑者 SHALL 以动画形式实时预览其全部帧，当前选中的跑者 SHALL 有明确的选中态视觉标识。动画预览 SHALL 仅在"跑者"分类可见时运行，切换到其他分类或关闭设置窗口时 SHALL 停止，避免无谓的 CPU 消耗。

#### Scenario: 预览动画播放
- **WHEN** 用户停留在"跑者"分类
- **THEN** 每个跑者预览循环播放各自的帧动画，当前选中跑者带选中态标识

#### Scenario: 离开分类停止预览
- **WHEN** 用户切换到其他设置分类或关闭设置窗口
- **THEN** 全部预览动画停止，不占用定时器资源，托盘动画不受影响

### Requirement: 点击选中跑者并持久化
用户点击任一跑者预览 SHALL 将其标记为选中；点击"应用"或"保存"后，选中跑者名称 SHALL 写入 settings.json，托盘动画 SHALL 立即切换为该跑者。应用重启后 SHALL 恢复上次保存的跑者。"恢复默认" SHALL 将选中跑者重置为默认值 cat（仍需应用/保存后生效）。

#### Scenario: 选中并保存
- **WHEN** 用户点击 Shiba Inu 预览并点击"保存"
- **THEN** 设置持久化为该跑者，托盘立即切换为 Shiba Inu 动画，设置窗口关闭

#### Scenario: 重启后恢复
- **WHEN** 应用以已保存的非默认跑者设置重新启动
- **THEN** 托盘从启动起即播放该跑者动画

#### Scenario: 恢复默认
- **WHEN** 用户点击"恢复默认"并保存
- **THEN** 选中跑者回到 cat，托盘显示 cat 动画

#### Scenario: 已保存跑者缺失时回退
- **WHEN** settings.json 中保存的跑者名在分发目录中不存在（如文件被删除）
- **THEN** 应用以默认跑者 cat 启动，不崩溃，设置界面选中项显示为 cat

### Requirement: "跑者"分类末尾署名跑者动画来源
设置窗口"跑者"分类末尾 SHALL 提供两个可点击外部链接：RunCat365 项目 `https://github.com/runcat-dev/RunCat365` 与跑者资源来源 RunnerGallery `https://runcat-dev.github.io/RunnerGallery/`，点击 SHALL 用系统默认浏览器打开，样式与"关于"分类现有"项目地址"链接一致。

#### Scenario: 打开 RunCat365 链接
- **WHEN** 用户在"跑者"分类末尾点击 RunCat365 项目链接
- **THEN** 默认浏览器打开 https://github.com/runcat-dev/RunCat365

#### Scenario: 打开 RunnerGallery 链接
- **WHEN** 用户在"跑者"分类末尾点击跑者资源来源链接
- **THEN** 默认浏览器打开 https://runcat-dev.github.io/RunnerGallery/

### Requirement: 移除任务栏时间格式设置
设置窗口"显示"分类 SHALL NOT 再提供"任务栏时间格式"输入框、说明文字与预览；settings.json SHALL NOT 再包含任务栏时间格式字段。旧版本配置文件中遗留的该字段 SHALL 在加载时被静默忽略，保存后不再写入。

#### Scenario: 显示分类无时间格式设置
- **WHEN** 用户打开设置窗口"显示"分类
- **THEN** 面板中不存在任务栏时间格式相关控件，其余显示设置保持不变

#### Scenario: 旧配置兼容
- **WHEN** 应用读取包含 `TaskbarTimeFormat` 字段的旧版 settings.json
- **THEN** 应用正常启动并忽略该字段，下次保存时配置中不再包含该字段
