#include "cts_array.h"

#include <string.h>

typedef struct array_iterator {
    cts_iterator base;
    cts_array *array;
    size_t index;
} array_iterator;

static void iter_free(cts_iterator *base) { cts_free(base); }
static bool iter_has_next(const cts_iterator *base)
{
    const array_iterator *it = (const array_iterator *)base;
    return it->index < it->array->count;
}
static void *iter_get(const cts_iterator *base)
{
    const array_iterator *it = (const array_iterator *)base;
    return cts_array_get(it->array, it->index);
}
static void iter_next(cts_iterator *base) { ((array_iterator *)base)->index++; }

static cts_iterator *array_get_iterator(const cts_type *type, void *data)
{
    (void)type;
    array_iterator *it = (array_iterator *)cts_malloc(sizeof(*it));
    if (!it) return NULL;
    it->base.free = iter_free;
    it->base.has_next = iter_has_next;
    it->base.get_value = iter_get;
    it->base.move_next = iter_next;
    it->array = (cts_array *)data;
    it->index = 0;
    return &it->base;
}

void cts_array_init(cts_array *array, const cts_type *item_type)
{
    if (!array) return;
    array->data = NULL;
    array->count = array->capacity = 0;
    array->item_type = item_type;
}

void cts_array_dispose(cts_array *array)
{
    if (!array) return;
    cts_free(array->data);
    cts_array_init(array, array->item_type);
}

bool cts_array_push(cts_array *array, const void *value)
{
    if (!array || !array->item_type || !value) return false;
    if (array->count == array->capacity) {
        size_t capacity = array->capacity ? array->capacity * 2 : 4;
        void *data = cts_malloc(capacity * array->item_type->size);
        if (!data) return false;
        if (array->data) {
            memcpy(data, array->data, array->count * array->item_type->size);
            cts_free(array->data);
        }
        array->data = data;
        array->capacity = capacity;
    }
    memcpy((char *)array->data + array->count * array->item_type->size,
           value, array->item_type->size);
    array->count++;
    return true;
}

void *cts_array_get(cts_array *array, size_t index)
{
    if (!array || index >= array->count) return NULL;
    return (char *)array->data + index * array->item_type->size;
}

const void *cts_array_get_const(const cts_array *array, size_t index)
{
    return cts_array_get((cts_array *)array, index);
}

bool cts_array_set(cts_array *array, size_t index, const void *value)
{
    void *slot = cts_array_get(array, index);
    if (!slot || !value) return false;
    memcpy(slot, value, array->item_type->size);
    return true;
}

cts_type cts_array_type(const char *name, const cts_type *item_type)
{
    cts_type type = {name, CTS_TYPE_COLLECTION, sizeof(cts_array), NULL, 0, NULL,
                     item_type, CTS_FLAG_NONE, array_get_iterator};
    return type;
}
