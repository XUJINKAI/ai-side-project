#include "game.h"

#include <assert.h>
#include <stdio.h>

static void test_board_shape(void)
{
    for (int size = NPUZZLE_MIN_SIZE; size <= NPUZZLE_MAX_SIZE; size++) {
        Puzzle puzzle;
        puzzle_init(&puzzle, size);
        assert(puzzle.size == size);
        assert(puzzle.moves == 0);
        for (int i = 0; i < size * size; i++) assert(puzzle.tiles[i] >= 0 && puzzle.tiles[i] < size * size);
    }
}

static void test_move_and_reverse(void)
{
    Puzzle puzzle;
    puzzle_init(&puzzle, 3);
    int blank = puzzle.blank;
    int row = blank / puzzle.size;
    Move move = row > 0 ? MOVE_UP : MOVE_DOWN;
    assert(puzzle_move(&puzzle, move));
    assert(puzzle.moves == 1);
    assert(puzzle.blank != blank);
    assert(!puzzle_move_to(&puzzle, puzzle.blank));
}

int main(void)
{
    test_board_shape();
    test_move_and_reverse();
    puts("game tests: ok");
    return 0;
}
