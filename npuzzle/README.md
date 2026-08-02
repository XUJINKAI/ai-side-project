# N-Puzzle Studio

一个纯 C、零第三方依赖的现代终端滑块游戏。界面由 `gpt-5.6-terra-260801` 制作，使用 ANSI 颜色和局部刷新，移动方块时只重绘变化的格子与状态栏，避免整屏闪烁。

## 构建

```sh
make
./out/npuzzle
```

或者：

```sh
cmake -S . -B build
cmake --build build
./build/npuzzle
```

安装到系统路径：

```sh
make install
```

默认安装位置为 `/usr/local/bin/npuzzle`，通常需以 `sudo make install` 执行。可通过 `PREFIX` 或 `DESTDIR` 自定义，例如 `make PREFIX=/opt install`。
若目标已存在，安装时会询问是否覆盖；自动化脚本可使用 `make install FORCE=1` 跳过确认。

需要一个支持 ANSI 控制序列的交互式终端。Linux、macOS 和大多数现代 Windows Terminal 均可运行。

## 工程结构

- `src/game.c`：棋盘状态、洗牌、移动与胜利判定
- `src/terminal.c`：raw mode、方向键与 SGR 鼠标事件
- `src/ui.c`：菜单、棋盘、状态栏和颜色面板
- `src/main.c`：游戏生命周期和输入分发
- `include/`：模块公共接口

启动后可用 `3` 至 `9`、`0`、`1`、`2` 选择 3x3 至 12x12 棋盘（其中 `0` 对应 10x10）；游戏中支持方向键、WASD、鼠标点击、`R` 重开、`M` 切换操作方向、`Q` 返回菜单。
