cmake_minimum_required(VERSION 3.16)
project(npuzzle VERSION 1.0 LANGUAGES C)
enable_testing()

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

add_executable(npuzzle
    src/main.c
    src/game.c
    src/terminal.c
    src/ui.c
)
target_include_directories(npuzzle PRIVATE include)
target_compile_options(npuzzle PRIVATE -Wall -Wextra -Wpedantic -O2)

add_executable(game_test src/game.c tests/game_test.c)
target_include_directories(game_test PRIVATE include)
target_compile_options(game_test PRIVATE -Wall -Wextra -Wpedantic -O2)
add_test(NAME game_test COMMAND game_test)
