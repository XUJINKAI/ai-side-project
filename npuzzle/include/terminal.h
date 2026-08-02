#ifndef NPUZZLE_TERMINAL_H
#define NPUZZLE_TERMINAL_H

typedef enum {
    TERM_NONE,
    TERM_KEY,
    TERM_MOUSE
} TerminalEventType;

typedef struct {
    TerminalEventType type;
    int key;
    int x;
    int y;
} TerminalEvent;

int terminal_start(void);
void terminal_stop(void);
TerminalEvent terminal_read(void);
int terminal_columns(void);

#endif
