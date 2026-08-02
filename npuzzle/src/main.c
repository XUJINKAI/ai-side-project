#include "game.h"
#include "terminal.h"
#include "ui.h"

#include <stdio.h>
#include <stdlib.h>
#include <time.h>

static int selected_size(TerminalEvent event)
{
    if (event.type != TERM_KEY) return 0;
    if (event.key >= '3' && event.key <= '6') return event.key - '0';
    if (event.key >= '7' && event.key <= '9') return event.key - '0';
    if (event.key == '0') return 10;
    if (event.key == '1') return 11;
    if (event.key == '2') return 12;
    return 0;
}

static int play(Puzzle *puzzle, int *tile_controls)
{
    char notice[96] = "";
    ui_game(puzzle, notice, *tile_controls);
    while (1) {
        TerminalEvent event = terminal_read();
        if (event.type == TERM_KEY) {
            if (event.key == 'q' || event.key == 'Q') return 0;
            if (event.key == 'r' || event.key == 'R') {
                puzzle_init(puzzle, puzzle->size);
                snprintf(notice, sizeof(notice), "A fresh board is waiting for you.");
            } else if (event.key == 'm' || event.key == 'M') {
                *tile_controls = !*tile_controls;
                snprintf(notice, sizeof(notice), "Controls now move %s.", *tile_controls ? "tiles" : "the empty space");
            } else {
                Move move;
                int valid = 1;
                if (event.key == 'w' || event.key == 'W' || event.key == 1001) move = *tile_controls ? MOVE_DOWN : MOVE_UP;
                else if (event.key == 's' || event.key == 'S' || event.key == 1002) move = *tile_controls ? MOVE_UP : MOVE_DOWN;
                else if (event.key == 'a' || event.key == 'A' || event.key == 1004) move = *tile_controls ? MOVE_RIGHT : MOVE_LEFT;
                else if (event.key == 'd' || event.key == 'D' || event.key == 1003) move = *tile_controls ? MOVE_LEFT : MOVE_RIGHT;
                else valid = 0;
                if (valid) { puzzle_move(puzzle, move); notice[0] = '\0'; }
            }
        } else if (event.type == TERM_MOUSE && event.key != 3) {
            BoardLayout layout = ui_layout(puzzle->size);
            int index = ui_board_hit(&layout, puzzle->size, event.x, event.y);
            if (index >= 0) { puzzle_move_to(puzzle, index); notice[0] = '\0'; }
        }
        if (puzzle->finished) snprintf(notice, sizeof(notice), "Puzzle complete. Press R for another round or Q for menu.");
        ui_game(puzzle, notice, *tile_controls);
    }
}

int main(void)
{
    int tile_controls = 1;
    srand((unsigned)time(NULL));
    if (!terminal_start()) {
        fprintf(stderr, "npuzzle needs an interactive terminal.\n");
        return 1;
    }
    while (1) {
        ui_menu(tile_controls);
        TerminalEvent event = terminal_read();
        if (event.type == TERM_KEY && (event.key == 'q' || event.key == 'Q')) break;
        if (event.type == TERM_KEY && (event.key == 'm' || event.key == 'M')) {
            tile_controls = !tile_controls;
            continue;
        }
        int size = selected_size(event);
        if (size) { Puzzle puzzle; puzzle_init(&puzzle, size); if (!play(&puzzle, &tile_controls)) continue; }
    }
    return 0;
}
