#include "game.h"
#include "terminal.h"
#include "ui.h"

#include <stdio.h>

static int selected_disks(int key)
{
    if (key >= '3' && key <= '9') return key - '0';
    if (key == '0') return 10;
    if (key == '1') return 11;
    if (key == '2') return 12;
    return 0;
}

static int play(Hanoi *game)
{
    char notice[96] = "";
    int selected_peg = -1;
    ui_game(game, selected_peg, notice);
    while (1) {
        int key = terminal_read_key();
        if (key == TERM_LEFT) key = '1';
        else if (key == TERM_DOWN) key = '2';
        else if (key == TERM_RIGHT) key = '3';
        if (key == 'q' || key == 'Q') return 0;
        if (key == 'r' || key == 'R') {
            hanoi_init(game, game->disks);
            selected_peg = -1;
            snprintf(notice, sizeof(notice), "A fresh tower is waiting for you.");
        } else if (key >= '1' && key <= '3' && !game->finished) {
            int peg = key - '1';
            if (selected_peg < 0) {
                if (game->heights[peg] == 0) snprintf(notice, sizeof(notice), "That tower is empty.");
                else {
                    selected_peg = peg;
                    snprintf(notice, sizeof(notice), "Choose a destination tower.");
                }
            } else if (selected_peg == peg) {
                selected_peg = -1;
                snprintf(notice, sizeof(notice), "Selection cancelled.");
            } else {
                if (hanoi_move(game, selected_peg, peg)) {
                    selected_peg = -1;
                    notice[0] = '\0';
                } else snprintf(notice, sizeof(notice), "A larger disk cannot rest on a smaller one.");
            }
        }
        if (game->finished) snprintf(notice, sizeof(notice), "Tower complete. Press R for another challenge or Q for menu.");
        ui_game(game, selected_peg, notice);
    }
}

int main(void)
{
    if (!terminal_start()) {
        fprintf(stderr, "hanoi needs an interactive terminal.\n");
        return 1;
    }
    while (1) {
        ui_menu();
        int key = terminal_read_key();
        if (key == 'q' || key == 'Q') break;
        int disks = selected_disks(key);
        if (disks) {
            Hanoi game;
            hanoi_init(&game, disks);
            play(&game);
        }
    }
    return 0;
}
