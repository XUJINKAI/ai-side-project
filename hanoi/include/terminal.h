#ifndef HANOI_TERMINAL_H
#define HANOI_TERMINAL_H

#define TERM_UP 1001
#define TERM_DOWN 1002
#define TERM_RIGHT 1003
#define TERM_LEFT 1004

int terminal_start(void);
void terminal_stop(void);
int terminal_read_key(void);
int terminal_columns(void);

#endif
