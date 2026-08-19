弈境 · Yijing Go
专业围棋分析台——基于 .NET 10 / WPF 的桌面围棋应用，内置 KataGo 最强 Transformer 模型（b11 768×12，7044 万参数），人机对弈与局面分析一体。
核心特性
- AI 对弈：支持 19/13/9 路，可执黑/执白/随机，AI 自动落子
- 智能思考时间：开局下快、后程下深——思考时长从开局值线性递增至上限（第 1 手 → 第 150 手），设置里随时可调
- 全自动最强引擎：启动时自动基准测试 TensorRT → OpenCL → CPU 四后端并选最快者；设置里一键「重新基准测试」可换引擎
- 实时分析：胜率、目差、候选点与胜率渐变实时显示
- 棋谱管理：SGF 打开/保存（FF4 全兼容），自动保存对局，损坏自动恢复
- 完整对局流程：悔棋、停一手、认输、数目定胜负
性能（RTX 5070 Laptop 实测）
后端	速度
TensorRT 10.16.1 + CUDA 13.2	~1000 visits/s
OpenCL	~97 visits/s
发布三件套
包	说明
便携版 Yijing-0.1.0-portable-win-x64.zip	解压即用，免安装，自带 .NET 运行时、KataGo 引擎与模型
安装版 Yijing-Setup-0.1.0-x64.exe	中文向导安装，开始菜单 + 桌面快捷方式
源码 Yijing-0.1.0-source.zip	完整源码与测试（180 项测试全绿），scripts\Fetch-KataGoAssets.ps1 拉取引擎资产
技术栈
.NET 10 · WPF · C# · KataGo v1.17 · 分层架构（Domain / Application / Infrastructure / Desktop）· TDD 开发（xUnit 180 测试）· 引擎崩溃自恢复 · 原子化 JSON 存储
