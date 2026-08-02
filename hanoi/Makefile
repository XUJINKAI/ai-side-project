CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Wpedantic -O2 -MMD -MP
CPPFLAGS ?= -Iinclude
PREFIX ?= /usr/local
BINDIR ?= $(PREFIX)/bin
OUT := out
BINARY := $(OUT)/hanoi
SOURCES := src/main.c src/game.c src/terminal.c src/ui.c
OBJECTS := $(SOURCES:src/%.c=$(OUT)/src/%.o)
DEPS := $(OBJECTS:.o=.d)

all: $(BINARY)

$(BINARY): $(OBJECTS) | $(OUT)
	$(CC) $(CFLAGS) $(OBJECTS) -o $@

$(OUT):
	mkdir -p $@

$(OUT)/src:
	mkdir -p $@

$(OUT)/src/%.o: src/%.c | $(OUT)/src
	$(CC) $(CPPFLAGS) $(CFLAGS) -c $< -o $@

test: src/game.c tests/game_test.c
	$(CC) $(CPPFLAGS) $(CFLAGS) src/game.c tests/game_test.c -o /tmp/hanoi-game-test
	/tmp/hanoi-game-test

install: $(BINARY)
	@target="$(DESTDIR)$(BINDIR)/hanoi"; \
	if [ -e "$$target" ] && [ "$(FORCE)" != "1" ]; then \
		printf '%s' "$$target already exists. Overwrite? [y/N] "; \
		read answer; \
		case "$$answer" in y|Y|yes|YES|Yes) ;; *) echo "Installation cancelled."; exit 0;; esac; \
	fi; \
	install -d "$(DESTDIR)$(BINDIR)"; \
	install -m 755 $(BINARY) "$$target"

.PHONY: all clean install test
clean:
	rm -rf out

-include $(DEPS)
