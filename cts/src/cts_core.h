/* Lightweight runtime type information for C. */
#ifndef CTS_CORE_H
#define CTS_CORE_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
#define CTS_EXTERN extern "C"
#else
#define CTS_EXTERN extern
#endif

#define CTS_COUNT_OF(a) (sizeof(a) / sizeof((a)[0]))
#define COUNT_OF(a) CTS_COUNT_OF(a)
#define EXPAND(...) __VA_ARGS__
#define CTS_INNER_TYPE(name) cts_##name##__type

typedef struct cts_type cts_type;
typedef struct cts_field cts_field;
typedef struct cts_iterator cts_iterator;

typedef enum cts_type_kind {
    CTS_TYPE_UNKNOWN = 0,
    CTS_TYPE_VALUE,
    CTS_TYPE_ENUM,
    CTS_TYPE_STRUCT,
    CTS_TYPE_UNION,
    CTS_TYPE_POINTER,
    CTS_TYPE_COLLECTION
} cts_type_kind;

typedef enum cts_value_kind {
    CTS_VALUE_NONE = 0,
    CTS_VALUE_CHAR,
    CTS_VALUE_UCHAR,
    CTS_VALUE_INT16,
    CTS_VALUE_UINT16,
    CTS_VALUE_INT32,
    CTS_VALUE_UINT32,
    CTS_VALUE_INT64,
    CTS_VALUE_UINT64,
    CTS_VALUE_FLOAT,
    CTS_VALUE_DOUBLE,
    CTS_VALUE_BOOL,
    CTS_VALUE_CSTR
} cts_value_kind;

typedef enum cts_flag {
    CTS_FLAG_NONE = 0,
    CTS_FLAG_IGNORE_CASE = 1
} cts_flag;

/* Backwards-compatible spelling used by the original prototype. */
#define CTS_ENUM_FLAG_IgnoreCase CTS_FLAG_IGNORE_CASE

typedef struct cts_enum_value {
    int value;
    const char *name;
} cts_enum_value;

struct cts_field {
    size_t offset;
    const char *name;
    const cts_type *type;
    cts_flag flag;
    const void *default_value;
};

struct cts_iterator {
    void (*free)(cts_iterator *self);
    bool (*has_next)(const cts_iterator *self);
    void *(*get_value)(const cts_iterator *self);
    void (*move_next)(cts_iterator *self);
};

struct cts_type {
    const char *name;
    cts_type_kind kind;
    size_t size;
    const void *fields;
    size_t field_count;
    const void *default_value;
    const cts_type *item_type;
    cts_flag flags;
    cts_iterator *(*get_iterator)(const cts_type *type, void *data);
};

CTS_EXTERN void *(*cts_malloc)(size_t size);
CTS_EXTERN void (*cts_free)(void *ptr);

CTS_EXTERN cts_type CTS_INNER_TYPE(char);
CTS_EXTERN cts_type CTS_INNER_TYPE(uchar);
CTS_EXTERN cts_type CTS_INNER_TYPE(int16);
CTS_EXTERN cts_type CTS_INNER_TYPE(uint16);
CTS_EXTERN cts_type CTS_INNER_TYPE(int32);
CTS_EXTERN cts_type CTS_INNER_TYPE(uint32);
CTS_EXTERN cts_type CTS_INNER_TYPE(int64);
CTS_EXTERN cts_type CTS_INNER_TYPE(uint64);
CTS_EXTERN cts_type CTS_INNER_TYPE(float);
CTS_EXTERN cts_type CTS_INNER_TYPE(double);
CTS_EXTERN cts_type CTS_INNER_TYPE(bool);
CTS_EXTERN cts_type CTS_INNER_TYPE(cstr);

CTS_EXTERN const char *cts_enum_to_name(const cts_type *type, int value);
CTS_EXTERN int cts_enum_to_value(const cts_type *type, const char *name);
CTS_EXTERN bool cts_type_check(const cts_type *type);
CTS_EXTERN void *cts_new_instance(const cts_type *type);
CTS_EXTERN void cts_free_instance(const cts_type *type, void *data);
CTS_EXTERN void *cts_deep_copy(const cts_type *type, const void *data);

#define CTS_ENUM_DECLARE(prefix, name) \
    prefix const cts_type *name##_get_type(void); \
    prefix const char *name##_to_name(int value); \
    prefix int name##_to_value(const char *text)

#define CTS_ENUM_IMPLEMENT(prefix, name, values_init, default_init, enum_flags) \
    static const cts_enum_value name##_values[] = { values_init }; \
    static const cts_enum_value name##_default = default_init; \
    static const cts_type name##_type = { \
        #name, CTS_TYPE_ENUM, sizeof(name), name##_values, \
        CTS_COUNT_OF(name##_values), &name##_default, NULL, enum_flags, NULL \
    }; \
    prefix const cts_type *name##_get_type(void) { return &name##_type; } \
    prefix const char *name##_to_name(int value) { return cts_enum_to_name(&name##_type, value); } \
    prefix int name##_to_value(const char *text) { return cts_enum_to_value(&name##_type, text); } \
    typedef int name##_cts_implementation_requires_semicolon

#endif
