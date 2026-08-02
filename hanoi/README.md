# Towers of Hanoi Studio

一个纯 C、零第三方依赖的终端汉诺塔游戏。将所有圆盘移至第三根柱子，且任意时刻都不能将大圆盘放在小圆盘上。

## 构建与运行

```sh
make
./out/hanoi
```

也可使用 CMake：

```sh
cmake -S . -B build
cmake --build build
./build/hanoi
```

## 安装

```sh
sudo make install
```

默认安装到 `/usr/local/bin/hanoi`。已有安装时会询问是否覆盖；自动化安装可使用 `sudo make install FORCE=1`。也可通过 `PREFIX` 或 `DESTDIR` 自定义安装路径。

## 操作

- 菜单：`3` 至 `9`、`0`、`1`、`2` 分别选择 3 至 12 个圆盘。
- 游戏：先按 `1`、`2`、`3` 或 `←`、`↓`、`→` 选择来源柱，再按目标柱编号或对应方向键移动圆盘；左、下、右分别对应第 1、2、3 根柱。
- `R`：重新开始当前挑战。
- `Q`：返回菜单；菜单中按 `Q` 退出。

构建产物集中在 `out/`，不会与源码混放。
