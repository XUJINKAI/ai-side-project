#ifndef CTS_ARRAY_H
#define CTS_ARRAY_H

#include "cts_core.h"

typedef struct cts_array {
    void *data;
    size_t count;
    size_t capacity;
    const cts_type *item_type;
} cts_array;

CTS_EXTERN void cts_array_init(cts_array *array, const cts_type *item_type);
CTS_EXTERN void cts_array_dispose(cts_array *array);
CTS_EXTERN bool cts_array_push(cts_array *array, const void *value);
CTS_EXTERN void *cts_array_get(cts_array *array, size_t index);
CTS_EXTERN const void *cts_array_get_const(const cts_array *array, size_t index);
CTS_EXTERN bool cts_array_set(cts_array *array, size_t index, const void *value);
CTS_EXTERN cts_type cts_array_type(const char *name, const cts_type *item_type);

#endif
