#include "terminal.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/select.h>
#include <termios.h>
#include <unistd.h>
#include <sys/ioctl.h>

static struct termios original;
static int active;

void terminal_stop(void)
{
    if (!active) return;
    printf("\033[?1000l\033[?1006l\033[?25h\033[0m\033[?1049l");
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
    /* Keep output processing enabled so '\n' is rendered as a real new line
       (CR+LF) instead of moving only one row down in raw input mode. */
    raw.c_cflag |= (tcflag_t) CS8;
    raw.c_cc[VMIN] = 0; raw.c_cc[VTIME] = 1;
    if (tcsetattr(STDIN_FILENO, TCSAFLUSH, &raw) == -1) return 0;
    active = 1;
    atexit(terminal_stop);
    printf("\033[?1049h\033[2J\033[H\033[?1000h\033[?1006h\033[?25l");
    return 1;
}

int terminal_columns(void)
{
    struct winsize size;
    if (ioctl(STDOUT_FILENO, TIOCGWINSZ, &size) == 0 && size.ws_col > 0) return size.ws_col;
    return 80;
}

static int read_byte(unsigned char *byte, int timeout)
{
    fd_set set;
    struct timeval tv = { timeout / 1000, (timeout % 1000) * 1000 };
    FD_ZERO(&set); FD_SET(STDIN_FILENO, &set);
    int ready = select(STDIN_FILENO + 1, &set, NULL, NULL, timeout < 0 ? NULL : &tv);
    return ready > 0 ? (int)read(STDIN_FILENO, byte, 1) : ready;
}

TerminalEvent terminal_read(void)
{
    TerminalEvent event = { TERM_NONE, 0, 0, 0 };
    unsigned char first;
    if (read_byte(&first, -1) <= 0) return event;
    if (first != 27) { event.type = TERM_KEY; event.key = first; return event; }

    unsigned char sequence[64]; int length = 0;
    while (length < 63 && read_byte(&sequence[length], 100) > 0) {
        unsigned char end = sequence[length++];
        if ((end >= 'A' && end <= 'Z') || (end >= 'a' && end <= 'z') || end == '~') break;
    }
    sequence[length] = '\0';
    if (length == 2 && sequence[0] == '[') {
        event.type = TERM_KEY;
        event.key = sequence[1] == 'A' ? 1001 : sequence[1] == 'B' ? 1002 : sequence[1] == 'C' ? 1003 : sequence[1] == 'D' ? 1004 : 0;
        return event;
    }
    int button;
    if (sscanf((char *)sequence, "[<%d;%d;%dM", &button, &event.x, &event.y) == 3) event.type = TERM_MOUSE;
    return event;
}
