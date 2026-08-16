CC ?= cc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -pedantic -Isrc
BUILD_DIR := build
SOURCES := src/cts_core.c src/cts_array.c src/cts_cJSON.c src/cJSON.c
OBJECTS := $(SOURCES:%.c=$(BUILD_DIR)/%.o)

.PHONY: all test clean
all: $(BUILD_DIR)/libcts.a

$(BUILD_DIR)/libcts.a: $(OBJECTS)
	$(AR) rcs $@ $^

$(BUILD_DIR)/%.o: %.c
	@mkdir -p $(@D)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/cts_tests: test/main.c $(SOURCES)
	@mkdir -p $(@D)
	$(CC) $(CFLAGS) $^ -lm -o $@

test: $(BUILD_DIR)/cts_tests
	./$(BUILD_DIR)/cts_tests

clean:
	rm -rf $(BUILD_DIR)
