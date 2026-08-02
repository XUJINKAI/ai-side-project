#include "ui.h"
#include "terminal.h"

#include <stdio.h>
#include <string.h>

#define RESET "\033[0m"
#define DIM "\033[2m"
#define BOLD "\033[1m"
#define GREEN "\033[38;5;120m"
#define BLUE "\033[38;5;117m"
#define PANEL "\033[48;5;236m"

static Puzzle previous_puzzle;
static BoardLayout previous_layout;
static int game_rendered;

void ui_clear(void) { printf("\033[2J\033[H"); }

static void title(void)
{
    int width = terminal_columns();
    int rule = width > 4 ? width - 4 : 1;
    if (rule > 59) rule = 59;
    printf(BOLD GREEN "  N-PUZZLE" RESET DIM " / STUDIO EDITION · gpt-5.6-terra-260801" RESET "\n");
    printf(DIM "  ");
    for (int i = 0; i < rule; i++) printf("─");
    printf(RESET "\n\n");
}

static void menu_choice(int key, int size)
{
    char label[8];
    snprintf(label, sizeof(label), "%dx%d", size, size);
    printf(GREEN BOLD "[%d]" RESET " %-6s", key, label);
}

void ui_menu(int tile_controls)
{
    game_rendered = 0;
    ui_clear(); title();
    printf("\n\n");
    printf("       " BLUE "┌──────────────────────────────┐" RESET "\n");
    printf("       " BLUE "│" RESET "     " BOLD "MAKE SPACE FOR ORDER" RESET "     " BLUE "│" RESET "\n");
    printf("       " BLUE "│" RESET "                              " BLUE "│" RESET "\n");
    printf("       " BLUE "│" RESET "  A quiet puzzle for focused  " BLUE "│" RESET "\n");
    printf("       " BLUE "│" RESET "  minds and curious hands.    " BLUE "│" RESET "\n");
    printf("       " BLUE "└──────────────────────────────┘" RESET "\n\n\n");
    printf("  " DIM "SELECT A BOARD" RESET "\n\n");
    printf("   ");
    for (int size = 3; size <= 7; size++) menu_choice(size, size);
    printf("\n   ");
    for (int size = 8; size <= 12; size++) menu_choice(size == 10 ? 0 : size == 11 ? 1 : size == 12 ? 2 : size, size);
    printf("\n\n");
    printf("  " DIM "Arrow keys / WASD to move    Click tiles to slide    Q to quit" RESET "\n");
    printf("  " GREEN BOLD "[M]" RESET " controls: " DIM "%s" RESET "\n", tile_controls ? "MOVE TILES" : "MOVE SPACE (INVERTED)");
    fflush(stdout);
}

BoardLayout ui_layout(int size)
{
    BoardLayout layout = { 2, 7, 11, 4 };
    int available = terminal_columns() - layout.left - 1;
    int width = available / size - 1;
    if (width < 3) width = 3;
    if (width < layout.tile_width) layout.tile_width = width;
    return layout;
}

static void border(const BoardLayout *layout, int size, const char *left, const char *mid, const char *right)
{
    printf("%*s%s", layout->left, "", left);
    for (int col = 0; col < size; col++) {
        for (int i = 0; i < layout->tile_width; i++) printf("─");
        printf("%s", col == size - 1 ? right : mid);
    }
    putchar('\n');
}

static void draw_cell(const Puzzle *puzzle, const BoardLayout *layout, int row, int col)
{
    int value = puzzle->tiles[row * puzzle->size + col];
    const char *panel = puzzle->finished ? RESET : PANEL;
    const char *number_style = puzzle->finished ? BOLD : GREEN BOLD;
    int x = layout->left + 2 + col * (layout->tile_width + 1);
    int y = layout->top + 1 + row * layout->tile_height;

    for (int line = 0; line < layout->tile_height - 1; line++) {
        printf("\033[%d;%dH", y + line, x);
        if (value == 0 || line != 1) printf("%s%*s" RESET, panel, layout->tile_width, "");
        else {
            char label[12];
            snprintf(label, sizeof(label), "%d", value);
            int left = (layout->tile_width - (int)strlen(label)) / 2;
            int right = layout->tile_width - (int)strlen(label) - left;
            printf("%s%s%*s%s%*s" RESET, panel, number_style, left, "", label, right, "");
        }
    }
}

static void draw_footer(const Puzzle *puzzle, const BoardLayout *layout, const char *notice, int tile_controls)
{
    int status_row = layout->top + puzzle->size * layout->tile_height + 2;
    printf("\033[%d;1H\033[2K  " DIM "MOVES" RESET "  " BOLD "%04d" RESET "     " DIM "STATUS" RESET "  %s", status_row, puzzle->moves, puzzle->finished ? GREEN "COMPLETE" RESET : GREEN "IN PLAY" RESET);
    printf("\033[%d;1H\033[2K  " GREEN BOLD "[R]" RESET " restart    " GREEN BOLD "[M]" RESET " %s    " GREEN BOLD "[Q]" RESET " menu", status_row + 2, tile_controls ? "tiles" : "space");
    printf("\033[%d;1H\033[2K", status_row + 4);
    if (notice && *notice) printf("  " BLUE "%s" RESET, notice);
}

static void draw_full_game(const Puzzle *puzzle, const char *notice, int tile_controls)
{
    BoardLayout layout = ui_layout(puzzle->size);
    const char *panel = puzzle->finished ? RESET : PANEL;
    const char *number_style = puzzle->finished ? BOLD : GREEN BOLD;
    ui_clear(); title();
    printf("  " BOLD "THE DAILY SHUFFLE" RESET "  " DIM "BOARD %dx%d" RESET "\n", puzzle->size, puzzle->size);
    printf("  " DIM "Arrange the tiles in order." RESET "\n\n");
    border(&layout, puzzle->size, "┌", "┬", "┐");
    for (int row = 0; row < puzzle->size; row++) {
        for (int line = 0; line < layout.tile_height - 1; line++) {
            printf("%*s│", layout.left, "");
            for (int col = 0; col < puzzle->size; col++) {
                int value = puzzle->tiles[row * puzzle->size + col];
                if (value == 0) printf("%s%*s" RESET "│", panel, layout.tile_width, "");
                else if (line != 1) printf("%s%*s" RESET "│", panel, layout.tile_width, "");
                else {
                    char label[12];
                    snprintf(label, sizeof(label), "%d", value);
                    int left = (layout.tile_width - (int)strlen(label)) / 2;
                    int right = layout.tile_width - (int)strlen(label) - left;
                    printf("%s%s%*s%s%*s" RESET "│", panel, number_style, left, "", label, right, "");
                }
            }
            putchar('\n');
        }
        if (row != puzzle->size - 1) border(&layout, puzzle->size, "├", "┼", "┤");
    }
    border(&layout, puzzle->size, "└", "┴", "┘");
    draw_footer(puzzle, &layout, notice, tile_controls);
}

void ui_game(const Puzzle *puzzle, const char *notice, int tile_controls)
{
    BoardLayout layout = ui_layout(puzzle->size);
    int needs_full_redraw = !game_rendered || previous_puzzle.size != puzzle->size || previous_layout.tile_width != layout.tile_width;

    if (needs_full_redraw) draw_full_game(puzzle, notice, tile_controls);
    else {
        if (previous_puzzle.finished != puzzle->finished) {
            for (int row = 0; row < puzzle->size; row++) {
                for (int col = 0; col < puzzle->size; col++) draw_cell(puzzle, &layout, row, col);
            }
        } else {
            for (int index = 0; index < puzzle->size * puzzle->size; index++) {
                if (previous_puzzle.tiles[index] != puzzle->tiles[index]) draw_cell(puzzle, &layout, index / puzzle->size, index % puzzle->size);
            }
        }
        draw_footer(puzzle, &layout, notice, tile_controls);
    }
    previous_puzzle = *puzzle;
    previous_layout = layout;
    game_rendered = 1;
    fflush(stdout);
}

int ui_board_hit(const BoardLayout *layout, int size, int x, int y)
{
    int local_x = x - layout->left - 1;
    int local_y = y - layout->top - 1;
    if (local_x < 0 || local_y < 0 || local_y % layout->tile_height == layout->tile_height - 1) return -1;
    int col = local_x / (layout->tile_width + 1);
    int row = local_y / layout->tile_height;
    if (col >= size || row >= size || local_x % (layout->tile_width + 1) >= layout->tile_width) return -1;
    return row * size + col;
}
