#ifndef NPUZZLE_UI_H
#define NPUZZLE_UI_H

#include "game.h"

typedef struct {
    int left;
    int top;
    int tile_width;
    int tile_height;
} BoardLayout;

void ui_menu(int tile_controls);
void ui_game(const Puzzle *puzzle, const char *notice, int tile_controls);
int ui_board_hit(const BoardLayout *layout, int size, int x, int y);
BoardLayout ui_layout(int size);
void ui_clear(void);

#endif
