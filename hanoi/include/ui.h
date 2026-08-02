#ifndef HANOI_UI_H
#define HANOI_UI_H

#include "game.h"

void ui_menu(void);
void ui_game(const Hanoi *game, int selected_peg, const char *notice);

#endif
