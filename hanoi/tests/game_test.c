#include "game.h"

#include <assert.h>
#include <stdio.h>

static void test_initial_state(void)
{
    Hanoi game;
    hanoi_init(&game, 5);
    assert(game.disks == 5);
    assert(game.heights[0] == 5 && game.heights[1] == 0 && game.heights[2] == 0);
    assert(game.pegs[0][0] == 5 && game.pegs[0][4] == 1);
}

static void test_valid_and_invalid_moves(void)
{
    Hanoi game;
    hanoi_init(&game, 3);
    assert(hanoi_move(&game, 0, 2));
    assert(!hanoi_move(&game, 0, 2));
    assert(hanoi_move(&game, 0, 1));
    assert(hanoi_move(&game, 2, 1));
    assert(game.moves == 3);
}

int main(void)
{
    test_initial_state();
    test_valid_and_invalid_moves();
    puts("hanoi game tests: ok");
    return 0;
}
