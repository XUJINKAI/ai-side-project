#include "ui.h"

#include <assert.h>
#include <stdio.h>

int main(void)
{
    BoardLayout layout = { 2, 7, 11, 4 };

    assert(ui_board_hit(&layout, 3, 3, 8) == 0);
    assert(ui_board_hit(&layout, 3, 15, 8) == 1);
    assert(ui_board_hit(&layout, 3, 3, 12) == 3);
    assert(ui_board_hit(&layout, 3, 3, 7) == -1);
    assert(ui_board_hit(&layout, 3, 3, 11) == -1);
    assert(ui_board_hit(&layout, 3, 14, 8) == -1);
    puts("ui tests: ok");
    return 0;
}
