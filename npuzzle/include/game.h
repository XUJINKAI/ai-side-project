#ifndef NPUZZLE_GAME_H
#define NPUZZLE_GAME_H

#define NPUZZLE_MIN_SIZE 3
#define NPUZZLE_MAX_SIZE 12

typedef enum {
    MOVE_UP,
    MOVE_DOWN,
    MOVE_LEFT,
    MOVE_RIGHT
} Move;

typedef struct {
    int tiles[NPUZZLE_MAX_SIZE * NPUZZLE_MAX_SIZE];
    int size;
    int blank;
    int moves;
    int finished;
} Puzzle;

void puzzle_init(Puzzle *puzzle, int size);
int puzzle_move(Puzzle *puzzle, Move move);
int puzzle_move_to(Puzzle *puzzle, int index);
int puzzle_is_solved(const Puzzle *puzzle);
int puzzle_can_move_to(const Puzzle *puzzle, int index);

#endif
