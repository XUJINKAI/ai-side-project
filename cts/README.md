# CTS — C Type System

CTS is a small C11 runtime type-information library. It describes primitive
values, enums, structs and dynamic arrays with `cts_type`, then uses that
metadata for JSON serialization/deserialization, default construction and
deep-copy helpers.

## Build and test

Requirements: a C11 compiler and GNU Make.

```sh
make
make test
```

The static library is written to `build/libcts.a`.

## Example

```c
#include "cts.h"
#include <stddef.h>

typedef enum Gender { GENDER_UNKNOWN, GENDER_MALE, GENDER_FEMALE } Gender;

CTS_ENUM_DECLARE(static, Gender);
CTS_ENUM_IMPLEMENT(static, Gender,
    EXPAND({GENDER_MALE, "Male"}, {GENDER_FEMALE, "Female"}),
    EXPAND({GENDER_UNKNOWN, "Unknown"}),
    CTS_FLAG_IGNORE_CASE);

typedef struct User {
    int32_t id;
    char *name;
    Gender gender;
} User;

static const cts_field user_fields[] = {
    {offsetof(User, id), "id", &CTS_INNER_TYPE(int32), CTS_FLAG_NONE, NULL},
    {offsetof(User, name), "name", &CTS_INNER_TYPE(cstr), CTS_FLAG_NONE, NULL},
    {offsetof(User, gender), "gender", &Gender_type, CTS_FLAG_NONE, NULL},
};

static const cts_type user_type = {
    "User", CTS_TYPE_STRUCT, sizeof(User), user_fields,
    CTS_COUNT_OF(user_fields), NULL, NULL, CTS_FLAG_NONE, NULL
};

int main(void)
{
    User user = {1, "Ada", GENDER_FEMALE};
    cJSON *json = cts_cJSON_serialize(&user_type, &user);
    char *text = cJSON_Print(json);

    /* text: {"id":1,"name":"Ada","gender":"Female"} */

    User *copy = cts_cJSON_deserialize(&user_type, json);
    cts_free_instance(&user_type, copy);
    cJSON_free(text);
    cJSON_Delete(json);
}
```

## Supported types

- Built-in signed/unsigned integers, floating-point values, booleans and
  owned C strings
- Enums represented in JSON by their symbolic name
- Nested structs described with `cts_field` and `offsetof`
- Dynamic `cts_array` collections
- JSON serialization and deserialization through the bundled cJSON
- Zero/default construction, recursive cleanup and string-aware deep copies

## Ownership

Objects returned by `cts_cJSON_deserialize`, `cts_new_instance`, and
`cts_deep_copy` belong to the caller and must be released with
`cts_free_instance`. A caller-owned, standalone `cts_array` must be released
with `cts_array_dispose`.

JSON objects and printed strings follow cJSON's ownership rules: use
`cJSON_Delete` and `cJSON_free` respectively.

## Notes

JSON stores numbers as doubles. Consequently, exact round trips for 64-bit
integers outside the IEEE-754 exact integer range are not guaranteed.
