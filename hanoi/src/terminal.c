#include "terminal.h"

#include <stdio.h>
#include <stdlib.h>
#include <sys/ioctl.h>
#include <sys/select.h>
#include <termios.h>
#include <unistd.h>

static struct termios original;
static int active;

void terminal_stop(void)
{
    if (!active) return;
    printf("\033[?25h\033[0m\033[?1049l");
    fflush(stdout);
    tcsetattr(STDIN_FILENO, TCSAFLUSH, &original);
    active = 0;
}

int terminal_start(void)
{
    struct termios raw;
    if (!isatty(STDIN_FILENO) || tcgetattr(STDIN_FILENO, &original) == -1) return 0;
    raw = original;
    raw.c_lflag &= (tcflag_t) ~(ECHO | ICANON | IEXTEN | ISIG);
    raw.c_iflag &= (tcflag_t) ~(BRKINT | ICRNL | INPCK | ISTRIP | IXON);
    raw.c_cflag |= (tcflag_t) CS8;
    raw.c_cc[VMIN] = 0;
    raw.c_cc[VTIME] = 1;
    if (tcsetattr(STDIN_FILENO, TCSAFLUSH, &raw) == -1) return 0;
    active = 1;
    atexit(terminal_stop);
    printf("\033[?1049h\033[2J\033[H\033[?25l");
    return 1;
}

int terminal_read_key(void)
{
    unsigned char key;
    while (read(STDIN_FILENO, &key, 1) != 1) {}
    if (key != 27) return key;

    unsigned char sequence[2];
    if (read(STDIN_FILENO, &sequence[0], 1) != 1 || sequence[0] != '[') return key;
    if (read(STDIN_FILENO, &sequence[1], 1) != 1) return key;
    if (sequence[1] == 'A') return TERM_UP;
    if (sequence[1] == 'B') return TERM_DOWN;
    if (sequence[1] == 'C') return TERM_RIGHT;
    if (sequence[1] == 'D') return TERM_LEFT;
    return key;
}

int terminal_columns(void)
{
    struct winsize size;
    if (ioctl(STDOUT_FILENO, TIOCGWINSZ, &size) == 0 && size.ws_col > 0) return size.ws_col;
    return 80;
}
