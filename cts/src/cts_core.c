#include "cts_core.h"
#include "cts_array.h"

#include <stdlib.h>
#include <string.h>

void *(*cts_malloc)(size_t size) = malloc;
void (*cts_free)(void *ptr) = free;

#define VALUE_TYPE(symbol, label, ctype, value_kind) \
    cts_type symbol = { label, CTS_TYPE_VALUE, sizeof(ctype), NULL, 0, NULL, NULL, \
                        (cts_flag)(value_kind), NULL }

/* For value types, flags stores cts_value_kind. */
VALUE_TYPE(CTS_INNER_TYPE(char), "char", int8_t, CTS_VALUE_CHAR);
VALUE_TYPE(CTS_INNER_TYPE(uchar), "uchar", uint8_t, CTS_VALUE_UCHAR);
VALUE_TYPE(CTS_INNER_TYPE(int16), "int16", int16_t, CTS_VALUE_INT16);
VALUE_TYPE(CTS_INNER_TYPE(uint16), "uint16", uint16_t, CTS_VALUE_UINT16);
VALUE_TYPE(CTS_INNER_TYPE(int32), "int32", int32_t, CTS_VALUE_INT32);
VALUE_TYPE(CTS_INNER_TYPE(uint32), "uint32", uint32_t, CTS_VALUE_UINT32);
VALUE_TYPE(CTS_INNER_TYPE(int64), "int64", int64_t, CTS_VALUE_INT64);
VALUE_TYPE(CTS_INNER_TYPE(uint64), "uint64", uint64_t, CTS_VALUE_UINT64);
VALUE_TYPE(CTS_INNER_TYPE(float), "float", float, CTS_VALUE_FLOAT);
VALUE_TYPE(CTS_INNER_TYPE(double), "double", double, CTS_VALUE_DOUBLE);
VALUE_TYPE(CTS_INNER_TYPE(bool), "bool", bool, CTS_VALUE_BOOL);
VALUE_TYPE(CTS_INNER_TYPE(cstr), "cstr", char *, CTS_VALUE_CSTR);

static int text_equal(const char *a, const char *b, bool ignore_case)
{
    if (!a || !b) return 0;
    while (*a && *b) {
        char ca = *a++, cb = *b++;
        if (ignore_case) {
            if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 'a' - 'A');
            if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 'a' - 'A');
        }
        if (ca != cb) return 0;
    }
    return *a == *b;
}

const char *cts_enum_to_name(const cts_type *type, int value)
{
    if (!type || type->kind != CTS_TYPE_ENUM) return NULL;
    const cts_enum_value *values = (const cts_enum_value *)type->fields;
    for (size_t i = 0; i < type->field_count; ++i)
        if (values[i].value == value) return values[i].name;
    const cts_enum_value *fallback = (const cts_enum_value *)type->default_value;
    return fallback ? fallback->name : NULL;
}

int cts_enum_to_value(const cts_type *type, const char *name)
{
    if (!type || type->kind != CTS_TYPE_ENUM) return 0;
    const cts_enum_value *values = (const cts_enum_value *)type->fields;
    bool ignore_case = (type->flags & CTS_FLAG_IGNORE_CASE) != 0;
    for (size_t i = 0; i < type->field_count; ++i)
        if (text_equal(values[i].name, name, ignore_case)) return values[i].value;
    const cts_enum_value *fallback = (const cts_enum_value *)type->default_value;
    return fallback ? fallback->value : 0;
}

bool cts_type_check(const cts_type *type)
{
    if (!type || !type->name || !*type->name || type->size == 0) return false;
    if (type->kind == CTS_TYPE_STRUCT && type->field_count && !type->fields) return false;
    if (type->kind == CTS_TYPE_COLLECTION && (!type->item_type || !type->get_iterator)) return false;
    return type->kind > CTS_TYPE_UNKNOWN && type->kind <= CTS_TYPE_COLLECTION;
}

void *cts_new_instance(const cts_type *type)
{
    if (!cts_type_check(type)) return NULL;
    void *result = cts_malloc(type->size);
    if (!result) return NULL;
    memset(result, 0, type->size);
    if (type->default_value) memcpy(result, type->default_value, type->size);
    if (type->kind == CTS_TYPE_STRUCT) {
        const cts_field *fields = (const cts_field *)type->fields;
        for (size_t i = 0; i < type->field_count; ++i)
            if (fields[i].default_value)
                memcpy((char *)result + fields[i].offset, fields[i].default_value,
                       fields[i].type->size);
    }
    return result;
}

static void cleanup_value(const cts_type *type, void *data)
{
    if (!data) return;
    if (type && type->kind == CTS_TYPE_VALUE && (cts_value_kind)type->flags == CTS_VALUE_CSTR)
        cts_free(*(char **)data);
    else if (type && type->kind == CTS_TYPE_STRUCT) {
        const cts_field *fields = (const cts_field *)type->fields;
        for (size_t i = 0; i < type->field_count; ++i) {
            const cts_type *ft = fields[i].type;
            void *fp = (char *)data + fields[i].offset;
            if (ft->kind == CTS_TYPE_VALUE && (cts_value_kind)ft->flags == CTS_VALUE_CSTR)
                cts_free(*(char **)fp);
            else if (ft->kind == CTS_TYPE_STRUCT)
                cleanup_value(ft, fp);
            else if (ft->kind == CTS_TYPE_COLLECTION) {
                cts_array *array = (cts_array *)fp;
                for (size_t j = 0; j < array->count; ++j)
                    cleanup_value(ft->item_type, cts_array_get(array, j));
                cts_array_dispose(array);
            }
        }
    } else if (type && type->kind == CTS_TYPE_COLLECTION) {
        cts_array *array = (cts_array *)data;
        for (size_t i = 0; i < array->count; ++i)
            cleanup_value(type->item_type, cts_array_get(array, i));
        cts_array_dispose(array);
    }
}

void cts_free_instance(const cts_type *type, void *data)
{
    if (!data) return;
    cleanup_value(type, data);
    cts_free(data);
}

void *cts_deep_copy(const cts_type *type, const void *data)
{
    if (!type || !data) return NULL;
    void *copy = cts_malloc(type->size);
    if (!copy) return NULL;
    memcpy(copy, data, type->size);
    if (type->kind == CTS_TYPE_VALUE && (cts_value_kind)type->flags == CTS_VALUE_CSTR) {
        const char *s = *(char *const *)data;
        *(char **)copy = NULL;
        if (s) {
            size_t n = strlen(s) + 1;
            *(char **)copy = cts_malloc(n);
            if (*(char **)copy) memcpy(*(char **)copy, s, n);
        }
    } else if (type->kind == CTS_TYPE_STRUCT) {
        const cts_field *fields = (const cts_field *)type->fields;
        for (size_t i = 0; i < type->field_count; ++i) {
            if (fields[i].type->kind == CTS_TYPE_VALUE &&
                (cts_value_kind)fields[i].type->flags == CTS_VALUE_CSTR) {
                const char *s = *(char *const *)((const char *)data + fields[i].offset);
                char **target = (char **)((char *)copy + fields[i].offset);
                *target = NULL;
                if (s) {
                    size_t n = strlen(s) + 1;
                    *target = cts_malloc(n);
                    if (*target) memcpy(*target, s, n);
                }
            }
        }
    }
    return copy;
}
