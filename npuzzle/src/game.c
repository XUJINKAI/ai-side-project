#include "game.h"

#include <stdlib.h>
#include <string.h>

static int row_of(const Puzzle *puzzle, int index) { return index / puzzle->size; }
static int col_of(const Puzzle *puzzle, int index) { return index % puzzle->size; }

void puzzle_init(Puzzle *puzzle, int size)
{
    int total = size * size;
    memset(puzzle, 0, sizeof(*puzzle));
    puzzle->size = size;
    puzzle->blank = total - 1;
    for (int i = 0; i < total - 1; i++) puzzle->tiles[i] = i + 1;
    puzzle->tiles[total - 1] = 0;

    int previous = -1;
    for (int step = 0; step < size * size * 16; step++) {
        int options[4], count = 0;
        int row = row_of(puzzle, puzzle->blank), col = col_of(puzzle, puzzle->blank);
        if (row > 0) options[count++] = puzzle->blank - size;
        if (row < size - 1) options[count++] = puzzle->blank + size;
        if (col > 0) options[count++] = puzzle->blank - 1;
        if (col < size - 1) options[count++] = puzzle->blank + 1;
        int target = options[rand() % count];
        if (target == previous && count > 1) target = options[(rand() + 1) % count];
        puzzle->tiles[puzzle->blank] = puzzle->tiles[target];
        puzzle->tiles[target] = 0;
        previous = puzzle->blank;
        puzzle->blank = target;
    }
    puzzle->moves = 0;
}

int puzzle_can_move_to(const Puzzle *puzzle, int index)
{
    if (index < 0 || index >= puzzle->size * puzzle->size || index == puzzle->blank) return 0;
    int row_gap = abs(row_of(puzzle, index) - row_of(puzzle, puzzle->blank));
    int col_gap = abs(col_of(puzzle, index) - col_of(puzzle, puzzle->blank));
    return row_gap + col_gap == 1;
}

int puzzle_move_to(Puzzle *puzzle, int index)
{
    if (puzzle->finished || !puzzle_can_move_to(puzzle, index)) return 0;
    puzzle->tiles[puzzle->blank] = puzzle->tiles[index];
    puzzle->tiles[index] = 0;
    puzzle->blank = index;
    puzzle->moves++;
    puzzle->finished = puzzle_is_solved(puzzle);
    return 1;
}

int puzzle_move(Puzzle *puzzle, Move move)
{
    int row = row_of(puzzle, puzzle->blank);
    int col = col_of(puzzle, puzzle->blank);
    int target = puzzle->blank;
    if (move == MOVE_UP && row > 0) target -= puzzle->size;
    if (move == MOVE_DOWN && row < puzzle->size - 1) target += puzzle->size;
    if (move == MOVE_LEFT && col > 0) target--;
    if (move == MOVE_RIGHT && col < puzzle->size - 1) target++;
    return puzzle_move_to(puzzle, target);
}

int puzzle_is_solved(const Puzzle *puzzle)
{
    int total = puzzle->size * puzzle->size;
    for (int i = 0; i < total - 1; i++) if (puzzle->tiles[i] != i + 1) return 0;
    return puzzle->tiles[total - 1] == 0;
}
