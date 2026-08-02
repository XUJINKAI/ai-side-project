#include "game.h"

#include <string.h>

void hanoi_init(Hanoi *game, int disks)
{
    memset(game, 0, sizeof(*game));
    game->disks = disks;
    game->heights[0] = disks;
    for (int index = 0; index < disks; index++) game->pegs[0][index] = disks - index;
}

int hanoi_can_move(const Hanoi *game, int source, int target)
{
    if (source < 0 || source > 2 || target < 0 || target > 2 || source == target) return 0;
    if (game->heights[source] == 0) return 0;
    if (game->heights[target] == 0) return 1;
    return game->pegs[source][game->heights[source] - 1] < game->pegs[target][game->heights[target] - 1];
}

int hanoi_is_solved(const Hanoi *game)
{
    return game->heights[2] == game->disks;
}

int hanoi_move(Hanoi *game, int source, int target)
{
    if (game->finished || !hanoi_can_move(game, source, target)) return 0;
    int disk = game->pegs[source][--game->heights[source]];
    game->pegs[target][game->heights[target]++] = disk;
    game->moves++;
    game->finished = hanoi_is_solved(game);
    return 1;
}
