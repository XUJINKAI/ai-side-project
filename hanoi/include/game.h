#ifndef HANOI_GAME_H
#define HANOI_GAME_H

#define HANOI_MIN_DISKS 3
#define HANOI_MAX_DISKS 12

typedef struct {
    int disks;
    int pegs[3][HANOI_MAX_DISKS];
    int heights[3];
    int moves;
    int finished;
} Hanoi;

void hanoi_init(Hanoi *game, int disks);
int hanoi_can_move(const Hanoi *game, int source, int target);
int hanoi_move(Hanoi *game, int source, int target);
int hanoi_is_solved(const Hanoi *game);

#endif
