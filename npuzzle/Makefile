CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Wpedantic -O2 -MMD -MP
CPPFLAGS ?= -Iinclude
PREFIX ?= /usr/local
BINDIR ?= $(PREFIX)/bin
SOURCES := src/main.c src/game.c src/terminal.c src/ui.c
OUT := out
BINARY := $(OUT)/npuzzle
OBJECTS := $(SOURCES:src/%.c=$(OUT)/src/%.o)
DEPS := $(OBJECTS:.o=.d)

all: $(BINARY)

$(BINARY): $(OBJECTS) | $(OUT)
	$(CC) $(CFLAGS) $(OBJECTS) -o $@

test: src/game.c tests/game_test.c src/ui.c tests/ui_test.c
	$(CC) $(CPPFLAGS) $(CFLAGS) src/game.c tests/game_test.c -o /tmp/npuzzle-game-test
	/tmp/npuzzle-game-test
	$(CC) $(CPPFLAGS) $(CFLAGS) src/game.c src/ui.c src/terminal.c tests/ui_test.c -o /tmp/npuzzle-ui-test
	/tmp/npuzzle-ui-test

$(OUT):
	mkdir -p $@

$(OUT)/src:
	mkdir -p $@

$(OUT)/src/%.o: src/%.c | $(OUT)/src
	$(CC) $(CPPFLAGS) $(CFLAGS) -c $< -o $@

install: $(BINARY)
	@target="$(DESTDIR)$(BINDIR)/npuzzle"; \
	if [ -e "$$target" ] && [ "$(FORCE)" != "1" ]; then \
		printf '%s' "$$target already exists. Overwrite? [y/N] "; \
		read answer; \
		case "$$answer" in y|Y|yes|YES|Yes) ;; *) echo "Installation cancelled."; exit 0;; esac; \
	fi; \
	install -d "$(DESTDIR)$(BINDIR)"; \
	install -m 755 $(BINARY) "$$target"

.PHONY: all clean install test
clean:
	rm -f npuzzle src/main.o src/game.o src/terminal.o src/ui.o
	rm -rf out

-include $(DEPS)
