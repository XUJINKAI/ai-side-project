#include "ui.h"
#include "terminal.h"

#include <stdio.h>

#define RESET "\033[0m"
#define DIM "\033[2m"
#define BOLD "\033[1m"
#define BLUE "\033[38;5;117m"
#define GREEN "\033[38;5;120m"
#define GOLD "\033[38;5;221m"

static int game_visible;

static void clear_screen(void) { printf("\033[2J\033[H"); }

static void title(void)
{
    int rule = terminal_columns() - 4;
    if (rule < 1) rule = 1;
    if (rule > 59) rule = 59;
    printf(BOLD GOLD "  TOWERS OF HANOI" RESET DIM " / STUDIO EDITION · gpt-5.6-terra-260801\n  ");
    for (int index = 0; index < rule; index++) printf("─");
    printf(RESET "\n\n");
}

static void menu_choice(int key, int disks)
{
    char label[12];
    snprintf(label, sizeof(label), "%d disks", disks);
    printf(GREEN BOLD "[%d]" RESET " %-8s", key, label);
}

void ui_menu(void)
{
    game_visible = 0;
    clear_screen();
    title();
    printf("       " BLUE "┌──────────────────────────────┐\n");
    printf("       │" RESET "       " BOLD "MOVE WITH INTENTION" RESET "       " BLUE "│\n");
    printf("       │" RESET "                              " BLUE "│\n");
    printf("       │" RESET "  Transfer every disk to the   " BLUE "│\n");
    printf("       │" RESET "  final tower, never larger    " BLUE "│\n");
    printf("       │" RESET "  on top of smaller.           " BLUE "│\n");
    printf("       " BLUE "└──────────────────────────────┘" RESET "\n\n");
    printf("  " DIM "SELECT A CHALLENGE" RESET "\n\n   ");
    for (int disks = 3; disks <= 7; disks++) menu_choice(disks, disks);
    printf("\n   ");
    for (int disks = 8; disks <= 12; disks++) menu_choice(disks == 10 ? 0 : disks == 11 ? 1 : disks == 12 ? 2 : disks, disks);
    printf("\n\n  " DIM "Choose a number · Q to quit" RESET "\n");
    fflush(stdout);
}

static int disk_at(const Hanoi *game, int peg, int level)
{
    return level < game->heights[peg] ? game->pegs[peg][level] : 0;
}

void ui_game(const Hanoi *game, int selected_peg, const char *notice)
{
    int half = (terminal_columns() - 16) / 6;
    if (half < 2) half = 2;
    if (half > 10) half = 10;
    int cell = half * 2 + 1;
    if (!game_visible) clear_screen();
    else printf("\033[H");
    game_visible = 1;
    title();
    printf("  " BOLD "THE PATIENT ASCENT" RESET "  " DIM "%d DISKS · MINIMUM %d MOVES" RESET "\n", game->disks, (1 << game->disks) - 1);
    printf("  " DIM "Choose a source tower, then its destination." RESET "\n\n");
    for (int level = game->disks - 1; level >= 0; level--) {
        printf("  ");
        for (int peg = 0; peg < 3; peg++) {
            int disk = disk_at(game, peg, level);
            int width = disk ? 1 + disk * (cell - 1) / game->disks : 1;
            int padding = (cell - width) / 2;
            const char *style = peg == selected_peg ? GOLD BOLD : BLUE BOLD;
            printf("%s%*s", style, padding, "");
            for (int index = 0; index < width; index++) printf(disk ? "═" : "│");
            printf("%*s" RESET "   ", cell - width - padding, "");
        }
        putchar('\n');
    }
    printf("  ");
    for (int peg = 0; peg < 3; peg++) {
        for (int index = 0; index < cell; index++) printf("─");
        printf("   ");
    }
    printf("\n  ");
    for (int peg = 0; peg < 3; peg++) printf("%*s" GREEN BOLD "[%d]" RESET "%*s   ", (cell - 3) / 2, "", peg + 1, (cell - 3 + 1) / 2, "");
    printf("\n\n  " DIM "MOVES" RESET "  " BOLD "%04d" RESET "     " DIM "STATUS" RESET "  %s\n", game->moves, game->finished ? GREEN "COMPLETE" RESET : GREEN "IN PLAY" RESET);
    printf("\n  " GREEN BOLD "[R]" RESET " restart    " GREEN BOLD "[Q]" RESET " menu    " DIM "1/2/3 or ←/↓/→ to choose a tower" RESET "\n");
    if (notice && *notice) printf("\n  " BLUE "%s" RESET "\n", notice);
    fflush(stdout);
}
